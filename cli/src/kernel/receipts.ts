/**
 * Decision receipts — the immutable audit ledger (issue #76).
 *
 * One approve/reject action → one append-only NDJSON row. The writer
 * lives in the kernel's singleton writer queue: the host calls
 * `kernel.recordDecision(...)`, the kernel hands the row to the
 * writer, and the writer serializes in a chained lane so concurrent
 * decisions on the same session never interleave.
 *
 * Storage location follows the XDG state-home convention
 * (`~/.local/state/comuki-cli/approvals/<session-id>.ndjson`). The
 * path is overridable for tests via `COMUKI_STATE_DIR`; production
 * reads `XDG_STATE_HOME` (Linux/macOS) or `%LOCALAPPDATA%` (Windows).
 *
 * Fingerprint stability — two identical approvals carry the same
 * fingerprint (sha256 over a canonical JSON form). The host computes
 * the fingerprint via `fingerprintFor` (a stable, side-effect-free
 * helper) and passes the result on the row; this module does NOT
 * import the typed `ApprovalEntry` so the kernel stays free of tui.
 */

import { createHash } from "node:crypto"
import { appendFile, mkdir } from "node:fs/promises"
import { dirname, join, resolve } from "node:path"

// ---------------------------------------------------------------------------
// DecisionReceipt — one row of the ledger
// ---------------------------------------------------------------------------

/** Approve/reject verdict — terminal; one row per call. */
export type DecisionVerdict = "approved" | "rejected"

/**
 * One immutable audit row. Schema is intentionally flat: the kernel
 * writes exactly these columns, in this order, so log scrapers can
 * rely on the order. `reason` is optional — a reject without a
 * reason still writes a row, with the field absent (NDJSON's missing
 * column is `undefined` → the property is omitted from the line).
 *
 * `ts` is optional in the input — the writer stamps it on append.
 * The output row always carries it.
 */
export interface DecisionReceipt {
  readonly decision: DecisionVerdict
  /** Unix milliseconds — set by the writer on append; callers may leave unset. */
  readonly ts?: number
  readonly approvalId: string
  readonly sessionId: string
  readonly scope: string
  readonly requester: string
  /**
   * Stable sha256 over the typed action payload, or `null` when no
   * fingerprint was computed yet. The writer stamps the row as-is;
   * duplicate detection compares later entries against this field.
   */
  readonly fingerprint: string | null
  /** Optional reject reason — empty string is treated as absent. */
  readonly reason?: string
}

// ---------------------------------------------------------------------------
// Default state directory — XDG with Windows + test overrides
// ---------------------------------------------------------------------------

/**
 * The default `~/.local/state/comuki-cli/` (Linux/macOS) or
 * `%LOCALAPPDATA%\comuki-cli\` (Windows) root for the receipts tree.
 * A test override (`COMUKI_STATE_DIR`) wins so unit tests never
 * touch the user's real state directory.
 */
export function defaultStateDirectory(): string {
  const override = process.env["COMUKI_STATE_DIR"]?.trim()
  if (override !== undefined && override.length > 0) {
    return resolve(override)
  }
  const xdgState = process.env["XDG_STATE_HOME"]?.trim()
  if (xdgState !== undefined && xdgState.length > 0) {
    return join(xdgState, "comuki-cli")
  }
  if (process.platform === "win32") {
    const localAppData = process.env["LOCALAPPDATA"]?.trim()
    if (localAppData !== undefined && localAppData.length > 0) {
      return join(localAppData, "comuki-cli")
    }
    const userProfile = process.env["USERPROFILE"]?.trim()
    if (userProfile !== undefined && userProfile.length > 0) {
      return join(userProfile, "AppData", "Local", "comuki-cli")
    }
  }
  const home = process.env["HOME"]?.trim() ?? process.env["USERPROFILE"]?.trim() ?? ""
  return join(home, ".local", "state", "comuki-cli")
}

/** Sub-directory of the state root that holds per-session NDJSON files. */
export const RECEIPTS_SUBDIR = "approvals"

/**
 * The full file path for one session's receipts. Tests inject the
 * directory; production uses `defaultStateDirectory() + RECEIPTS_SUBDIR`.
 */
export function receiptsFilePath(stateDirectory: string, sessionId: string): string {
  if (sessionId.length === 0) {
    throw new Error("DecisionReceipt: empty sessionId")
  }
  return join(stateDirectory, RECEIPTS_SUBDIR, `${sessionId}.ndjson`)
}

// ---------------------------------------------------------------------------
// Fingerprint — sha256 over a canonical JSON form
// ---------------------------------------------------------------------------

/**
 * Stable sha256 over a structurally-stable shape. Two identical
 * inputs (same keys, same types) produce the same fingerprint — that
 * is the test contract and the out-of-band duplicate detector's
 * contract. Keys are sorted, arrays preserve order, objects recurse.
 *
 * The fingerprint excludes `createdAtUnixMs` and any per-build
 * metadata; it covers the typed action only, so two rebuilds of the
 * same approval over the same wire payload still match.
 */
export function fingerprintFor(value: unknown): string {
  return createHash("sha256").update(canonicalizeForFingerprint(value)).digest("hex")
}

function canonicalizeForFingerprint(value: unknown): string {
  if (value === null || typeof value !== "object") {
    return JSON.stringify(value)
  }
  if (Array.isArray(value)) {
    return `[${value.map(canonicalizeForFingerprint).join(",")}]`
  }
  const record = value as Record<string, unknown>
  const keys = Object.keys(record).sort()
  const parts = keys.map((key) => `${JSON.stringify(key)}:${canonicalizeForFingerprint(record[key])}`)
  return `{${parts.join(",")}}`
}

// ---------------------------------------------------------------------------
// DecisionReceiptStore — the writer the kernel owns
// ---------------------------------------------------------------------------

/**
 * Append-only ledger writer. `append` is sync from the caller's POV;
 * the actual file write runs in a chained lane so concurrent
 * appends on the same session never interleave.
 */
export interface DecisionReceiptStore {
  /** Queue one row; the file write is fire-and-forget into the chained lane. */
  append(receipt: DecisionReceipt): void
  /** Resolve when every queued write has settled (test/stop seam). */
  whenIdle(): Promise<void>
}

export interface DecisionReceiptStoreOptions {
  /** Absolute path to the directory that holds the `approvals/` tree. */
  readonly stateDirectory: string
  /** Injectable clock — defaults to `Date.now`. */
  readonly now?: () => number
}

/**
 * Build a writer over `stateDirectory`. The state directory's
 * `approvals/` subdirectory is created lazily on first append.
 */
export function createDecisionReceiptStore(
  options: DecisionReceiptStoreOptions
): DecisionReceiptStore {
  const now = options.now ?? (() => Date.now())
  const base = options.stateDirectory
  let chain: Promise<void> = Promise.resolve()

  function append(receipt: DecisionReceipt): void {
    const ts = now()
    const stamped: DecisionReceipt = { ...receipt, ts }
    chain = chain.then(() => writeOne(base, stamped))
    // The kernel never fails on a receipt write — a corrupt filesystem
    // must not break the dispatch chain. Errors are silently absorbed
    // so the user's interactive flow keeps going.
  }

  async function whenIdle(): Promise<void> {
    await chain
  }

  return { append, whenIdle }
}

async function writeOne(
  baseDirectory: string,
  receipt: DecisionReceipt
): Promise<void> {
  const path = receiptsFilePath(baseDirectory, receipt.sessionId)
  await mkdir(dirname(path), { recursive: true, mode: 0o700 })
  await appendFile(path, serializeRow(receipt), "utf8")
}

/** Encode one row as a single NDJSON line (no embedded newlines, trailing `\n`). */
export function serializeRow(receipt: DecisionReceipt): string {
  // Stable property order so a diff of two NDJSON files is meaningful.
  const ordered: Record<string, unknown> = {
    decision: receipt.decision,
    ts: receipt.ts,
    approvalId: receipt.approvalId,
    sessionId: receipt.sessionId,
    scope: receipt.scope,
    requester: receipt.requester,
    fingerprint: receipt.fingerprint,
  }
  if (receipt.reason !== undefined && receipt.reason.length > 0) {
    ordered["reason"] = receipt.reason
  }
  return `${JSON.stringify(ordered)}\n`
}

// ---------------------------------------------------------------------------
// Decoder — used by tests and the (future) show-receipt command
// ---------------------------------------------------------------------------

/** Parse one NDJSON line into a DecisionReceipt. Returns `null` on malformed input. */
export function decodeRow(line: string): DecisionReceipt | null {
  const trimmed = line.trim()
  if (trimmed.length === 0) {
    return null
  }
  let raw: unknown
  try {
    raw = JSON.parse(trimmed)
  } catch {
    return null
  }
  if (raw === null || typeof raw !== "object") {
    return null
  }
  const record = raw as Record<string, unknown>
  const decision = record["decision"]
  if (decision !== "approved" && decision !== "rejected") {
    return null
  }
  const ts = record["ts"]
  const approvalId = record["approvalId"]
  const sessionId = record["sessionId"]
  const scope = record["scope"]
  const requester = record["requester"]
  const fingerprint = record["fingerprint"]
  const reason = record["reason"]
  if (
    typeof ts !== "number" ||
    typeof approvalId !== "string" ||
    typeof sessionId !== "string" ||
    typeof scope !== "string" ||
    typeof requester !== "string"
  ) {
    return null
  }
  return {
    decision,
    ts,
    approvalId,
    sessionId,
    scope,
    requester,
    fingerprint:
      fingerprint === null || typeof fingerprint === "string" ? fingerprint : null,
    ...(typeof reason === "string" && reason.length > 0 ? { reason } : {}),
  }
}

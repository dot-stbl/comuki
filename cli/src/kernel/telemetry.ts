/**
 * Structured diagnostics log (issue #81).
 *
 * One JSON-Lines sink under the platform state root:
 *
 *   <state>/diagnostics.log
 *
 * Where `<state>` follows the same XDG-aware resolution that
 * `receipts.ts`, `cursors.ts`, `drafts.ts`, and `sessions.ts` already
 * honor — `COMUKI_STATE_DIR` wins, then `XDG_STATE_HOME`, then
 * `%LOCALAPPDATA%` (Windows), then `~/.local/state`.
 *
 * Every event carries `{ ts, level, kind, session, payload }`:
 *
 *   - `ts` — unix milliseconds from the injectable clock.
 *   - `level` — one of `info`, `warn`, `error`.
 *   - `kind` — a stable short tag the operator greps by
 *     (`boot`, `crash`, `render-error`, `dispatch-failed`, ...).
 *   - `session` — optional session id the event is about.
 *   - `payload` — arbitrary JSON-serialisable record; serialised via
 *     `JSON.stringify`, the schema is per-`kind` and lives in code.
 *
 * Fire-and-forget contract:
 *
 *   - Every `log()` call returns synchronously. The actual file write
 *     runs in a chained lane so concurrent writes never interleave.
 *   - A write failure (disk full, permission denied) is reported to
 *     `process.stderr` but never thrown — the dispatch path keeps
 *     going.
 *   - `whenIdle()` resolves once every queued write has settled.
 *
 * The writer is intentionally renderer-agnostic — no Ink, no React,
 * no ANSI helpers, no terminal dimensions. The kernel owns the
 * instance and exposes it as `kernel.telemetry()`; the host calls
 * `log(...)` from its error paths.
 */

import { appendFile, mkdir, readFile, stat } from "node:fs/promises"
import { dirname, join, resolve } from "node:path"
import { defaultStateDirectory } from "./receipts"

// ---------------------------------------------------------------------------
// Event shape
// ---------------------------------------------------------------------------

export type LogLevel = "info" | "warn" | "error"

/**
 * The on-disk schema is intentionally flat — three required fields, two
 * optional. `payload` carries the structured context the operator needs
 * to triage; the `kind` tag keeps the line scannable.
 */
export interface StructuredLogEvent {
  readonly ts: number
  readonly level: LogLevel
  readonly kind: string
  readonly session?: string
  readonly payload?: Record<string, unknown>
}

// ---------------------------------------------------------------------------
// Storage location
// ---------------------------------------------------------------------------

/** File name for the diagnostics sink (lives under the state root). */
export const DIAGNOSTICS_FILE = "diagnostics.log"

/**
 * The full file path. Tests inject the directory; production calls
 * `defaultDiagnosticsFilePath()` which lands under the platform state
 * root (same XDG resolution as `receipts.ts`).
 */
export function diagnosticsFilePath(stateDirectory: string): string {
  return join(stateDirectory, DIAGNOSTICS_FILE)
}

/**
 * Default `stateDirectory/diagnostics.log`. Mirrors the XDG
 * resolution that `receipts.ts` already uses so a user override of
 * `COMUKI_STATE_DIR` propagates uniformly across the kernel's
 * durable stores.
 */
export function defaultDiagnosticsFilePath(): string {
  return diagnosticsFilePath(defaultStateDirectory())
}

// ---------------------------------------------------------------------------
// Encoder / decoder
// ---------------------------------------------------------------------------

/** Stable key order so NDJSON diffs stay meaningful across runs. */
export function encodeEvent(event: StructuredLogEvent): string {
  const ordered: Record<string, unknown> = {
    ts: event.ts,
    level: event.level,
    kind: event.kind,
  }
  if (event.session !== undefined) {
    ordered["session"] = event.session
  }
  if (event.payload !== undefined) {
    ordered["payload"] = event.payload
  }
  return `${JSON.stringify(ordered)}\n`
}

/**
 * Parse one NDJSON line into a `StructuredLogEvent`. Returns `null`
 * on malformed input — a corrupt line degrades to "skip" so one bad
 * write can never blank the rest of the file.
 */
export function decodeEvent(line: string): StructuredLogEvent | null {
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
  if (raw === null || typeof raw !== "object" || Array.isArray(raw)) {
    return null
  }
  const record = raw as Record<string, unknown>
  const ts = record["ts"]
  const level = record["level"]
  const kind = record["kind"]
  if (typeof ts !== "number" || !Number.isFinite(ts)) {
    return null
  }
  if (level !== "info" && level !== "warn" && level !== "error") {
    return null
  }
  if (typeof kind !== "string" || kind.length === 0) {
    return null
  }
  const session = record["session"]
  const payload = record["payload"]
  const event: StructuredLogEvent = { ts, level, kind }
  if (typeof session === "string" && session.length > 0) {
    ;(event as { session?: string }).session = session
  }
  if (
    payload !== undefined &&
    payload !== null &&
    typeof payload === "object" &&
    !Array.isArray(payload)
  ) {
    ;(event as { payload?: Record<string, unknown> }).payload =
      payload as Record<string, unknown>
  }
  return event
}

// ---------------------------------------------------------------------------
// StructuredLog — the writer the kernel owns
// ---------------------------------------------------------------------------

/**
 * The writer surface. `log(...)` is sync from the caller's POV; the
 * underlying file write runs in a chained lane.
 */
export interface StructuredLog {
  /** Queue one event; never throws on a write failure. */
  log(event: StructuredLogEvent): void
  /** Sugar — stamp `ts` for the caller, default `kind = event.kind`. */
  logFields(level: LogLevel, kind: string, payload?: Record<string, unknown>, session?: string): void
  /** Resolves when every queued write has settled (test/stop seam). */
  whenIdle(): Promise<void>
  /** Where the writer is currently pointed. Test seam. */
  readonly filePath: string
}

export interface StructuredLogOptions {
  /** Absolute path to the directory that holds `diagnostics.log`. */
  readonly stateDirectory?: string
  /** Injectable clock — defaults to `Date.now`. */
  readonly now?: () => number
  /**
   * Sink for write failures. Defaults to `process.stderr` so the
   * dispatch path stays informed without crashing; tests inject a
   * buffer.
   */
  readonly onWriteError?: (error: unknown) => void
}

/**
 * Build a `StructuredLog` rooted at `options.stateDirectory`. The
 * file is created lazily on the first write.
 */
export function createStructuredLog(options: StructuredLogOptions = {}): StructuredLog {
  const now = options.now ?? (() => Date.now())
  const stateDirectory = options.stateDirectory ?? defaultStateDirectory()
  const filePath = resolve(stateDirectory, DIAGNOSTICS_FILE)
  const onWriteError =
    options.onWriteError ?? ((error: unknown) => writeStderr(error))

  let chain: Promise<void> = Promise.resolve()

  function log(event: StructuredLogEvent): void {
    const stamped: StructuredLogEvent = { ...event, ts: event.ts ?? now() }
    chain = chain
      .then(() => writeOne(filePath, stamped))
      .catch((error: unknown) => onWriteError(error))
  }

  function logFields(
    level: LogLevel,
    kind: string,
    payload?: Record<string, unknown>,
    session?: string
  ): void {
    const event: StructuredLogEvent = { ts: now(), level, kind }
    if (session !== undefined && session.length > 0) {
      ;(event as { session?: string }).session = session
    }
    if (payload !== undefined) {
      ;(event as { payload?: Record<string, unknown> }).payload = payload
    }
    log(event)
  }

  async function whenIdle(): Promise<void> {
    try {
      await chain
    } catch (error) {
      onWriteError(error)
    }
  }

  return { log, logFields, whenIdle, filePath }
}

async function writeOne(filePath: string, event: StructuredLogEvent): Promise<void> {
  // POSIX `mode` is ignored on Windows; harmless on Linux/macOS. The
  // recursive: true is the only thing that matters across platforms.
  await mkdir(dirname(filePath), { recursive: true })
  await appendFile(filePath, encodeEvent(event), "utf8")
}

function writeStderr(error: unknown): void {
  // Best-effort stderr surface — never throw, never block.
  try {
    const message = error instanceof Error ? error.message : String(error)
    process.stderr.write(`[comuki telemetry] write failed: ${message}\n`)
  } catch {
    // swallow — stderr itself may be unavailable in some test harnesses
  }
}

// ---------------------------------------------------------------------------
// Read helpers — used by tests, the profile script, and export-bundle
// ---------------------------------------------------------------------------

/** Read every event the file currently holds. Lines that fail to parse are dropped. */
export async function readEvents(filePath: string): Promise<StructuredLogEvent[]> {
  let text: string
  try {
    await stat(filePath)
    text = await readFile(filePath, "utf8")
  } catch (error: unknown) {
    if (isMissingFileError(error)) {
      return []
    }
    throw error
  }
  const events: StructuredLogEvent[] = []
  for (const line of text.split("\n")) {
    const event = decodeEvent(line)
    if (event !== null) {
      events.push(event)
    }
  }
  return events
}

function isMissingFileError(error: unknown): boolean {
  return (
    typeof error === "object" &&
    error !== null &&
    "code" in error &&
    (error as { code?: unknown }).code === "ENOENT"
  )
}

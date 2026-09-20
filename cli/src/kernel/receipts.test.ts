/**
 * Decision receipts — NDJSON round-trip and fingerprint stability
 * (issue #76).
 *
 * Two contracts:
 *
 * 1. The writer appends one JSON line per call, in the chained lane,
 *    to `<stateDir>/approvals/<sessionId>.ndjson`. Malformed input
 *    is decoded as `null`; well-formed rows round-trip with the same
 *    `decision`, `ts`, `approvalId`, `scope`, `requester`,
 *    `fingerprint`, and optional `reason`.
 * 2. The fingerprint is a stable sha256 over the typed action
 *    payload — two identical approvals compute the same fingerprint,
 *    a different scope or node count produces a different one.
 */

import { afterEach, beforeEach, describe, expect, test } from "bun:test"
import { readFile, rm, stat } from "node:fs/promises"
import { tmpdir } from "node:os"
import { join } from "node:path"

import { sessionId, turnRequestId, type HarnessSession } from "../harness/state"
import { approvalFingerprint, pendingApprovalEntry } from "../tui/approvals"
import {
  createDecisionReceiptStore,
  decodeRow,
  fingerprintFor,
  receiptsFilePath,
  serializeRow,
  type DecisionReceipt,
} from "./receipts"

const SESSION_ID = sessionId("s-1")

const PLAN = {
  intent: "RECEIPT-INTENT",
  scope: "tests/receipt/*",
  risk: "medium",
  planSteps: ["RECEIPT-STEP-1", "RECEIPT-STEP-2"],
  diff: "+ receipt diff",
  estimateMinutes: 5,
  nodes: [
    {
      id: "n1",
      key: "n1",
      title: "RECEIPT-NODE-ONE",
      profileKey: "impl",
      brief: "RECEIPT-NODE-ONE-BRIEF",
    },
    {
      id: "n2",
      key: "n2",
      title: "RECEIPT-NODE-TWO",
      profileKey: "test",
      brief: "RECEIPT-NODE-TWO-BRIEF",
    },
  ],
}

function awaitingSession(plan: unknown): HarnessSession {
  return {
    identity: { kind: "remote", id: SESSION_ID },
    projectId: null,
    title: "TUI",
    createdAtUnixMs: 0,
    renamed: false,
    unread: false,
    turn: {
      kind: "awaiting-approval",
      requestId: turnRequestId("turn-receipt"),
    },
    transcriptLoad: { kind: "loaded" },
    transcript: [],
    queue: [],
    history: [],
    lastUserMessage: null,
    pendingPlan: plan,
  }
}

async function entryApproval(): Promise<DecisionReceipt> {
  const entry = pendingApprovalEntry({
    session: awaitingSession(PLAN),
    messageId: "m-receipt",
    requester: "subject-receipt",
    createdAtUnixMs: Date.parse("2026-01-01T00:00:00Z"),
    trailingMeta: null,
  })
  if (entry === null) {
    throw new Error("entry must be non-null")
  }
  const fingerprint = approvalFingerprint(entry)
  return {
    decision: "approved",
    approvalId: entry.approvalId,
    sessionId: SESSION_ID,
    scope: entry.applicability.scope,
    requester: entry.applicability.requester,
    fingerprint,
  }
}

describe("receipts — NDJSON round-trip", () => {
  let tempDir: string
  let store: ReturnType<typeof createDecisionReceiptStore>

  beforeEach(async () => {
    tempDir = await mkTempDir("comuki-receipts")
    store = createDecisionReceiptStore({ stateDirectory: tempDir })
  })

  afterEach(async () => {
    await rm(tempDir, { recursive: true, force: true })
  })

  test("a single approved decision writes one well-formed NDJSON row", async () => {
    const receipt = await entryApproval()
    store.append(receipt)
    await store.whenIdle()

    const filePath = receiptsFilePath(tempDir, SESSION_ID)
    const fileStat = await stat(filePath)
    expect(fileStat.isFile()).toBe(true)
    const text = await readFile(filePath, "utf8")
    const lines = text.split("\n").filter((line) => line.length > 0)
    expect(lines).toHaveLength(1)

    const decoded = decodeRow(lines[0]!)
    expect(decoded).not.toBeNull()
    expect(decoded?.decision).toBe("approved")
    expect(decoded?.approvalId).toBe(receipt.approvalId)
    expect(decoded?.sessionId).toBe(SESSION_ID)
    expect(decoded?.scope).toBe("tests/receipt/*")
    expect(decoded?.requester).toBe("subject-receipt")
    expect(decoded?.fingerprint).toBe(receipt.fingerprint)
    expect(typeof decoded?.ts).toBe("number")
  })

  test("a rejected decision with empty reason omits the reason column", async () => {
    const receipt = await entryApproval()
    store.append({ ...receipt, decision: "rejected", reason: "" })
    await store.whenIdle()

    const text = await readFile(receiptsFilePath(tempDir, SESSION_ID), "utf8")
    const lines = text.split("\n").filter((line) => line.length > 0)
    expect(lines).toHaveLength(1)
    expect(lines[0]).not.toContain("reason")

    const decoded = decodeRow(lines[0]!)
    expect(decoded?.decision).toBe("rejected")
    expect(decoded?.reason).toBeUndefined()
  })

  test("a rejected decision with a non-empty reason keeps the reason column", async () => {
    const receipt = await entryApproval()
    store.append({ ...receipt, decision: "rejected", reason: "not safe to run" })
    await store.whenIdle()

    const lines = (await readFile(receiptsFilePath(tempDir, SESSION_ID), "utf8"))
      .split("\n")
      .filter((line) => line.length > 0)
    expect(lines).toHaveLength(1)
    expect(lines[0]).toContain("reason")

    const decoded = decodeRow(lines[0]!)
    expect(decoded?.reason).toBe("not safe to run")
  })

  test("a null fingerprint is preserved through round-trip", async () => {
    const receipt = { ...(await entryApproval()), fingerprint: null }
    store.append(receipt)
    await store.whenIdle()

    const decoded = decodeRow(
      (await readFile(receiptsFilePath(tempDir, SESSION_ID), "utf8"))
        .split("\n")
        .filter((line) => line.length > 0)[0]!
    )
    expect(decoded?.fingerprint).toBeNull()
  })

  test("two decisions on the same session append in order, one row each", async () => {
    const first = await entryApproval()
    const second: DecisionReceipt = {
      ...first,
      decision: "rejected",
      reason: "race condition",
    }
    store.append(first)
    store.append(second)
    await store.whenIdle()

    const lines = (await readFile(receiptsFilePath(tempDir, SESSION_ID), "utf8"))
      .split("\n")
      .filter((line) => line.length > 0)
    expect(lines).toHaveLength(2)
    const firstDecoded = decodeRow(lines[0]!)
    const secondDecoded = decodeRow(lines[1]!)
    expect(firstDecoded?.decision).toBe("approved")
    expect(secondDecoded?.decision).toBe("rejected")
    expect(secondDecoded?.reason).toBe("race condition")
  })

  test("decodeRow returns null on empty / malformed input", () => {
    expect(decodeRow("")).toBeNull()
    expect(decodeRow("not-json")).toBeNull()
    expect(decodeRow(JSON.stringify({ decision: "approved" }))).toBeNull()
    expect(decodeRow(JSON.stringify({}))).toBeNull()
    expect(decodeRow(JSON.stringify(null))).toBeNull()
  })

  test("serializeRow produces exactly one line per call (no embedded newlines)", () => {
    const receipt: DecisionReceipt = {
      decision: "approved",
      ts: 1700000000000,
      approvalId: "a-1",
      sessionId: SESSION_ID,
      scope: "x",
      requester: "y",
      fingerprint: "abc",
      reason: "multi\nline\nreason",
    }
    const line = serializeRow(receipt)
    expect(line.endsWith("\n")).toBe(true)
    // The newline-containing reason is encoded as \n inside the JSON
    // string — the row itself stays a single line.
    expect(line.split("\n")).toHaveLength(2) // payload + trailing newline
  })
})

describe("fingerprint stability — duplicate detection", () => {
  test("two identical ApprovalEntry objects produce the same fingerprint", () => {
    const a = pendingApprovalEntry({
      session: awaitingSession(PLAN),
      messageId: "m-fp-a",
      requester: "subject-fp",
      createdAtUnixMs: 0,
      trailingMeta: null,
    })
    const b = pendingApprovalEntry({
      session: awaitingSession(PLAN),
      messageId: "m-fp-b",
      requester: "subject-fp",
      createdAtUnixMs: 0,
      trailingMeta: null,
    })
    expect(a).not.toBeNull()
    expect(b).not.toBeNull()
    if (a === null || b === null) {
      throw new Error("entries must be non-null")
    }
    expect(approvalFingerprint(a)).toBe(approvalFingerprint(b))
  })

  test("different scopes produce different fingerprints", () => {
    const sessionA: HarnessSession = {
      ...awaitingSession(PLAN),
      pendingPlan: { ...PLAN, scope: "tests/scope-a/*" },
    }
    const sessionB: HarnessSession = {
      ...awaitingSession(PLAN),
      pendingPlan: { ...PLAN, scope: "tests/scope-b/*" },
    }
    const a = pendingApprovalEntry({
      session: sessionA,
      messageId: "m-fp-scope-a",
      requester: "subject-fp",
      createdAtUnixMs: 0,
      trailingMeta: null,
    })
    const b = pendingApprovalEntry({
      session: sessionB,
      messageId: "m-fp-scope-b",
      requester: "subject-fp",
      createdAtUnixMs: 0,
      trailingMeta: null,
    })
    if (a === null || b === null) {
      throw new Error("entries must be non-null")
    }
    expect(approvalFingerprint(a)).not.toBe(approvalFingerprint(b))
  })

  test("the fingerprint is stable across rebuilds (same wire payload, fresh entry)", () => {
    const first = pendingApprovalEntry({
      session: awaitingSession(PLAN),
      messageId: "m-receipt",
      requester: "subject-receipt",
      createdAtUnixMs: Date.parse("2026-01-01T00:00:00Z"),
      trailingMeta: null,
    })
    const rebuilt = pendingApprovalEntry({
      session: awaitingSession(PLAN),
      messageId: "m-receipt",
      requester: "subject-receipt",
      createdAtUnixMs: Date.parse("2026-01-01T00:00:00Z"),
      trailingMeta: null,
    })
    expect(first).not.toBeNull()
    expect(rebuilt).not.toBeNull()
    if (first === null || rebuilt === null) {
      throw new Error("entries must be non-null")
    }
    expect(approvalFingerprint(first)).toBe(approvalFingerprint(rebuilt))
  })

  test("fingerprintFor works on a raw shape (kernel side helper)", () => {
    // The kernel-side `fingerprintFor` accepts an unknown value and
    // canonicalises it; two equal canonical forms must produce the
    // same hash regardless of source-order keys.
    const left = { kind: "compact", scope: "x", effects: "y" }
    const right = { effects: "y", kind: "compact", scope: "x" }
    expect(fingerprintFor(left)).toBe(fingerprintFor(right))
  })
})

async function mkTempDir(label: string): Promise<string> {
  const stamp = `${Date.now()}-${Math.floor(Math.random() * 1e6)}`
  const dir = join(tmpdir(), `${label}-${stamp}`)
  await rm(dir, { recursive: true, force: true })
  return dir
}

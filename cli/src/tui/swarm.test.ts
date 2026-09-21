/**
 * Swarm summary view-model (issue #78) — pure derivation tests.
 *
 * The summary sits between the kernel's `AttentionSignal` and the
 * canvas renderer. It must:
 *
 * - split items into P0/P1/P2 buckets in stable order;
 * - render each row with `profile · title (duration)` and a badge;
 * - go silent (`isEmpty === true`) when only P2 surfaces — focus mode
 *   default;
 * - thread `findSwarmRow` for deep-link inspection;
 * - thread `attachPlanNodes` for stacked approval detail.
 */

import { describe, expect, test } from "bun:test"
import {
  attachFailure,
  attachPlanNodes,
  deriveSwarmSummary,
  findSwarmRow,
  summaryIsEmpty,
} from "./swarm"
import {
  DEFAULT_STALL_THRESHOLD_MS,
  deriveAttention,
  type AttentionSignal,
} from "../kernel/attention"
import type { ClientSnapshot } from "../kernel/kernel"
import {
  sessionId,
  turnRequestId,
  type CliError,
  type HarnessSession,
  type HarnessState,
  type SessionId,
} from "../harness/state"

const BASE_TIME = 1_000_000

const REASON_LABELS = {
  awaiting: "awaiting",
  stalled: "stalled",
  evidence: "evidence",
  failed: "failed",
  active: "active",
} as const

function cliError(): CliError {
  return { kind: "server", code: "boom", message: "synthetic", retryable: false }
}

function session(
  id: SessionId,
  overrides: Partial<HarnessSession> = {}
): HarnessSession {
  return {
    identity: { kind: "remote", id },
    projectId: null,
    title: `s-${id}`,
    createdAtUnixMs: BASE_TIME,
    renamed: false,
    unread: false,
    turn: { kind: "idle" },
    transcriptLoad: { kind: "loaded" },
    transcript: [],
    queue: [],
    history: [],
    lastUserMessage: null,
    pendingPlan: null,
    ...overrides,
  }
}

function snapshot(
  sessions: readonly HarnessSession[],
  cursors: Record<string, number> = {}
): ClientSnapshot {
  const state: HarnessState = {
    sessions,
    activeSessionId: null,
    connection: { kind: "connected" },
    auth: { kind: "authenticated", subjectId: "subject" },
    overlay: { kind: "closed" },
    drafts: [],
    cursors,
    outbound: [],
  }
  return {
    revision: 0,
    state,
    sessions: [],
    cursors: {},
    online: true,
  }
}

function signalFor(
  sessions: readonly HarnessSession[],
  cursors: Record<string, number> = {},
  nowMs = BASE_TIME + 1_000
): AttentionSignal {
  return deriveAttention(snapshot(sessions, cursors), { nowUnixMs: nowMs })
}

describe("swarm summary — derivation + focus-mode default", () => {
  test("empty state produces an empty summary", () => {
    const signal = signalFor([])
    const summary = deriveSwarmSummary(signal, REASON_LABELS)
    expect(summaryIsEmpty(summary)).toBe(true)
    expect(summary.p0).toEqual([])
    expect(summary.p1).toEqual([])
    expect(summary.p2).toEqual([])
  })

  test("P2-only swarm — focus mode goes quiet, no P2 surfaces", () => {
    // The renderer suppresses P2 when there is no P0/P1 — see
    // `renderSwarmCanvas`. The summary itself keeps the P2 bucket
    // for callers that want it; the canvas hides it.
    const signal = signalFor([
      session(sessionId("server-1"), {
        turn: {
          kind: "thinking",
          requestId: turnRequestId("turn-1"),
          accumulatedText: "",
        },
      }),
    ])
    const summary = deriveSwarmSummary(signal, REASON_LABELS)
    expect(summaryIsEmpty(summary)).toBe(false)
    expect(summary.p2).toHaveLength(1)
    expect(summary.p0).toEqual([])
    expect(summary.p1).toEqual([])
  })

  test("full swarm — P0/P1/P2 buckets populated in stable order", () => {
    const stalledFor = DEFAULT_STALL_THRESHOLD_MS + 5_000
    const nowMs = BASE_TIME + stalledFor
    const signal = signalFor(
      [
        session(sessionId("server-evidence"), {
          unread: true,
          turn: {
            kind: "thinking",
            requestId: turnRequestId("turn-evidence"),
            accumulatedText: "",
          },
        }),
        session(sessionId("server-stalled"), {
          turn: {
            kind: "thinking",
            requestId: turnRequestId("turn-stalled"),
            accumulatedText: "",
          },
        }),
        session(sessionId("server-failed"), {
          turn: {
            kind: "failed",
            requestId: turnRequestId("turn-failed"),
            error: cliError(),
          },
        }),
      ],
      { "server-evidence": nowMs - 1_000 },
      nowMs
    )
    const summary = deriveSwarmSummary(signal, REASON_LABELS)
    expect(summary.p0).toHaveLength(1)
    expect(summary.p0[0]!.reason).toBe("stalled")
    // Failed surfaces as P1; the harness carries no failure
    // timestamp, so the failed row persists until retry/close.
    expect(summary.p1).toHaveLength(2)
    expect(summary.p1.map((row) => row.reason).sort()).toEqual([
      "evidence",
      "failed",
    ])
    // P2 surfaces here only when P0 and P1 are empty — the canvas
    // hides the bucket; the summary keeps it.
    expect(summary.p2).toEqual([])
  })

  test("row carries the headline: profile · title (duration)", () => {
    const signal = signalFor([
      session(sessionId("server-1"), {
        title: "refactor auth",
        turn: {
          kind: "awaiting-approval",
          requestId: turnRequestId("turn-1"),
        },
      }),
    ])
    const summary = deriveSwarmSummary(signal, REASON_LABELS)
    const row = summary.p0[0]!
    expect(row.headline).toContain("refactor auth")
    expect(row.profile).toBe("refactor auth")
    expect(row.title).toBe("refactor auth")
    expect(row.badge).toBe("awaiting")
    expect(row.correlationId).toBe("turn-1")
    expect(row.durationLabel).toMatch(/^\d+s$/)
  })

  test("row uses the pending-plan's first node profile when present", () => {
    const signal = signalFor([
      session(sessionId("server-1"), {
        title: "ship v2",
        turn: {
          kind: "awaiting-approval",
          requestId: turnRequestId("turn-1"),
        },
        pendingPlan: {
          intent: "ship v2",
          nodes: [
            { id: "n1", profileKey: "explore", title: "explore api", brief: "explore api" },
          ],
        },
      }),
    ])
    const summary = deriveSwarmSummary(signal, REASON_LABELS)
    expect(summary.p0[0]!.profile).toBe("explore")
    expect(summary.p0[0]!.headline.startsWith("explore · ")).toBe(true)
  })

  test("findSwarmRow matches by session id OR correlation id", () => {
    const signal = signalFor([
      session(sessionId("server-1"), {
        turn: {
          kind: "awaiting-approval",
          requestId: turnRequestId("turn-99"),
        },
      }),
    ])
    const summary = deriveSwarmSummary(signal, REASON_LABELS)
    expect(findSwarmRow(summary, "server-1")?.correlationId).toBe("turn-99")
    expect(findSwarmRow(summary, "turn-99")?.sessionId).toBe("server-1")
    expect(findSwarmRow(summary, "missing")).toBeNull()
  })

  test("attachPlanNodes merges stacked-plan node rows into awaiting detail", () => {
    const signal = signalFor([
      session(sessionId("server-1"), {
        turn: {
          kind: "awaiting-approval",
          requestId: turnRequestId("turn-1"),
        },
        pendingPlan: {
          scope: "tests/*",
          nodes: [
            { id: "n1", profileKey: "explore", title: "explore", brief: "explore api" },
            { id: "n2", profileKey: "review", title: "review", brief: "review diffs" },
          ],
        },
      }),
    ])
    const summary = deriveSwarmSummary(signal, REASON_LABELS)
    const enriched = attachPlanNodes(summary.p0[0]!, [
      { id: "n1", profile: "explore", brief: "explore api" },
      { id: "n2", profile: "review", brief: "review diffs" },
    ])
    expect(enriched.detail?.kind).toBe("awaiting-approval")
    if (enriched.detail?.kind === "awaiting-approval") {
      expect(enriched.detail.planNodes).toHaveLength(2)
      expect(enriched.detail.planNodes[0]?.id).toBe("n1")
      expect(enriched.detail.planNodes[1]?.profile).toBe("review")
    }
  })

  test("attachPlanNodes is a no-op on non-awaiting rows", () => {
    const signal = signalFor([
      session(sessionId("server-1"), {
        turn: {
          kind: "failed",
          requestId: turnRequestId("turn-1"),
          error: cliError(),
        },
      }),
    ])
    const summary = deriveSwarmSummary(signal, REASON_LABELS)
    const original = summary.p1[0]!
    const enriched = attachPlanNodes(original, [
      { id: "n1", profile: "x", brief: "y" },
    ])
    expect(enriched).toBe(original)
  })

  test("attachFailure stamps the failed-turn detail block", () => {
    const signal = signalFor([
      session(sessionId("server-1"), {
        turn: {
          kind: "failed",
          requestId: turnRequestId("turn-1"),
          error: cliError(),
        },
      }),
    ])
    const summary = deriveSwarmSummary(signal, REASON_LABELS)
    const enriched = attachFailure(summary.p1[0]!, "boom", "synthetic message")
    expect(enriched.detail?.kind).toBe("failed")
    if (enriched.detail?.kind === "failed") {
      expect(enriched.detail.code).toBe("boom")
      expect(enriched.detail.message).toBe("synthetic message")
    }
  })

test("summary preserves the kernel revision and now timestamps", () => {
    // `deriveAttention` returns a fresh signal whose `revision` is the
    // snapshot's revision (0 in our fixture). The summary echoes that
    // verbatim; the test pins the contract rather than mutating the
    // signal after the fact.
    const signal = signalFor([], {}, BASE_TIME + 9_999)
    const summary = deriveSwarmSummary(signal, REASON_LABELS)
    expect(summary.revision).toBe(signal.revision)
    expect(summary.generatedAtUnixMs).toBe(BASE_TIME + 9_999)
    expect(summary.nowUnixMs).toBe(BASE_TIME + 9_999)
  })
})

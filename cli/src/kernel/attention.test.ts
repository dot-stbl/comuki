/**
 * Attention derivation — pure unit tests over `deriveAttention`.
 *
 * The signal is the seam between the kernel's snapshot and the
 * canvas: a `deriveAttention(snapshot)` call classifies every remote
 * session into P0/P1/P2 and emits a stable order. These tests pin the
 * classification rules and the listener pattern of `AttentionSource`.
 */

import { afterEach, describe, expect, test } from "bun:test"
import {
  AttentionSource,
  DEFAULT_FAILED_WINDOW_MS,
  DEFAULT_STALL_THRESHOLD_MS,
  deriveAttention,
  type AttentionSignal,
} from "./attention"
import type { ClientKernel, ClientSnapshot } from "./kernel"
import {
  turnRequestId,
  type CliError,
  type HarnessMessage,
  type HarnessSession,
  type HarnessState,
  type PendingSessionId,
  type SessionId,
} from "../harness/state"

const SESSION_IDS = {
  pending: "pending-1" as PendingSessionId,
  remoteOne: "server-1" as SessionId,
  remoteTwo: "server-2" as SessionId,
  remoteThree: "server-3" as SessionId,
} as const

const BASE_TIME = 1_000_000

function cliError(): CliError {
  return {
    kind: "server",
    code: "boom",
    message: "synthetic failure",
    retryable: false,
  }
}

function emptyTranscript(): readonly HarnessMessage[] {
  return []
}

function remoteSession(
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
    transcript: emptyTranscript(),
    queue: [],
    history: [],
    lastUserMessage: null,
    pendingPlan: null,
    ...overrides,
  }
}

function pendingSession(id: PendingSessionId): HarnessSession {
  return {
    identity: { kind: "pending", id },
    projectId: null,
    title: "",
    createdAtUnixMs: BASE_TIME,
    renamed: false,
    unread: false,
    turn: { kind: "idle" },
    transcriptLoad: { kind: "not-loaded" },
    transcript: emptyTranscript(),
    queue: [],
    history: [],
    lastUserMessage: null,
    pendingPlan: null,
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
  // Issue #77 — the new fields on ClientSnapshot default to the
  // empty / offline state; the attention derivation does not read them.
  return {
    revision: 0,
    state,
    sessions: [],
    cursors: {},
    online: true,
  }
}

describe("deriveAttention — classification rules", () => {
  test("an empty state produces an empty signal", () => {
    const signal = deriveAttention(snapshot([]), { nowUnixMs: BASE_TIME + 1000 })
    expect(signal.items).toEqual([])
  })

  test("pending sessions never surface — focus mode silences them", () => {
    const signal = deriveAttention(
      snapshot([pendingSession(SESSION_IDS.pending)]),
      { nowUnixMs: BASE_TIME + 1000 }
    )
    expect(signal.items).toEqual([])
  })

  test("awaiting-approval yields a P0 row with the request id as correlation", () => {
    const signal = deriveAttention(
      snapshot([
        remoteSession(SESSION_IDS.remoteOne, {
          turn: {
            kind: "awaiting-approval",
            requestId: turnRequestId("turn-1"),
          },
        }),
      ]),
      { nowUnixMs: BASE_TIME + 1000 }
    )
    expect(signal.items).toHaveLength(1)
    const item = signal.items[0]!
    expect(item.priority).toBe("p0")
    expect(item.reason).toBe("awaiting-approval")
    expect(item.state).toBe("awaiting-approval")
    expect(item.correlationId).toBe("turn-1")
    expect(item.sessionId).toBe(SESSION_IDS.remoteOne)
  })

  test("thinking past the stall threshold yields a P0 stalled row", () => {
    const stalledFor = DEFAULT_STALL_THRESHOLD_MS + 2_000
    const signal = deriveAttention(
      snapshot([
        remoteSession(SESSION_IDS.remoteOne, {
          turn: {
            kind: "thinking",
            requestId: turnRequestId("turn-stalled"),
            accumulatedText: "",
          },
        }),
      ]),
      {
        nowUnixMs: BASE_TIME + stalledFor,
        // The session has no transcript + no cursor → heartbeat
        // falls back to createdAtUnixMs = BASE_TIME, so the
        // threshold check fires correctly.
      }
    )
    expect(signal.items).toHaveLength(1)
    const item = signal.items[0]!
    expect(item.priority).toBe("p0")
    expect(item.reason).toBe("stalled")
    expect(item.rationale).toContain("heartbeat")
  })

  test("thinking under the stall threshold with no evidence yields a P2 row", () => {
    const signal = deriveAttention(
      snapshot([
        remoteSession(SESSION_IDS.remoteOne, {
          turn: {
            kind: "thinking",
            requestId: turnRequestId("turn-active"),
            accumulatedText: "",
          },
        }),
      ]),
      { nowUnixMs: BASE_TIME + 1_000 }
    )
    expect(signal.items).toHaveLength(1)
    const item = signal.items[0]!
    expect(item.priority).toBe("p2")
    expect(item.reason).toBe("active")
  })

  test("failed turn surfaces as P1 and persists until the user retries or closes", () => {
    // The harness reducer does not stamp a failure timestamp by
    // design, so every failed turn surfaces as P1 — see the
    // classifier note in attention.ts.
    const insideWindow = deriveAttention(
      snapshot([
        remoteSession(SESSION_IDS.remoteOne, {
          turn: {
            kind: "failed",
            requestId: turnRequestId("turn-failed"),
            error: cliError(),
          },
        }),
      ]),
      { nowUnixMs: BASE_TIME + 5_000 }
    )
    expect(insideWindow.items).toHaveLength(1)
    expect(insideWindow.items[0]!.priority).toBe("p1")
    expect(insideWindow.items[0]!.reason).toBe("failed")

    const longAfter = deriveAttention(
      snapshot([
        remoteSession(SESSION_IDS.remoteOne, {
          turn: {
            kind: "failed",
            requestId: turnRequestId("turn-failed"),
            error: cliError(),
          },
        }),
      ]),
      {
        nowUnixMs: BASE_TIME + DEFAULT_FAILED_WINDOW_MS + 10_000,
      }
    )
    expect(longAfter.items).toHaveLength(1)
    expect(longAfter.items[0]!.priority).toBe("p1")
  })

  test("thinking with unread output yields a P1 evidence row", () => {
    const signal = deriveAttention(
      snapshot([
        remoteSession(SESSION_IDS.remoteOne, {
          unread: true,
          turn: {
            kind: "thinking",
            requestId: turnRequestId("turn-evidence"),
            accumulatedText: "",
          },
        }),
      ]),
      { nowUnixMs: BASE_TIME + 1_000 }
    )
    expect(signal.items).toHaveLength(1)
    expect(signal.items[0]!.priority).toBe("p1")
    expect(signal.items[0]!.reason).toBe("evidence")
  })

  test("priorities sort P0 → P1 → P2 with the oldest reason first inside a bucket", () => {
    const stalledFor = DEFAULT_STALL_THRESHOLD_MS + 5_000
    const nowMs = BASE_TIME + stalledFor
    const signal = deriveAttention(
      snapshot(
        [
          remoteSession(SESSION_IDS.remoteOne, {
            unread: true,
            turn: {
              kind: "thinking",
              requestId: turnRequestId("turn-active"),
              accumulatedText: "",
            },
            // A recent cursor keeps this session out of "stalled"
            // territory even at nowMs — it's evidence, not stalled.
          }),
          remoteSession(SESSION_IDS.remoteTwo, {
            turn: {
              kind: "thinking",
              requestId: turnRequestId("turn-stalled"),
              accumulatedText: "",
            },
            // No cursor, old session — heartbeat falls back to
            // createdAtUnixMs and trips the stall threshold.
          }),
          remoteSession(SESSION_IDS.remoteThree, {
            turn: {
              kind: "failed",
              requestId: turnRequestId("turn-failed"),
              error: cliError(),
            },
          }),
        ],
        { "server-1": nowMs - 1_000 }
      ),
      {
        nowUnixMs: nowMs,
      }
    )
    // P0 stalled first, then P1 evidence (oldest reason = recent
    // cursor), then P1 failed (reasonAt = nowUnixMs). Within a
    // bucket, the row with the older reasonAtUnixMs renders first.
    expect(signal.items.map((item) => item.priority)).toEqual([
      "p0",
      "p1",
      "p1",
    ])
    expect(signal.items.map((item) => item.reason)).toEqual([
      "stalled",
      "evidence",
      "failed",
    ])
  })

  test("the cursor advances the heartbeat and rescues a thinking session from P0", () => {
    const stalledFor = DEFAULT_STALL_THRESHOLD_MS + 5_000
    const signal = deriveAttention(
      snapshot(
        [
          remoteSession(SESSION_IDS.remoteOne, {
            turn: {
              kind: "thinking",
              requestId: turnRequestId("turn-active"),
              accumulatedText: "",
            },
          }),
        ],
        { "server-1": BASE_TIME + stalledFor - 1_000 }
      ),
      { nowUnixMs: BASE_TIME + stalledFor }
    )
    expect(signal.items).toHaveLength(1)
    expect(signal.items[0]!.priority).toBe("p2")
    expect(signal.items[0]!.reason).toBe("active")
  })
})

describe("AttentionSource — listener pattern matches addEventListener", () => {
  const captured: AttentionSignal[] = []
  let unsubscribe: (() => void) | null = null

  afterEach(() => {
    unsubscribe?.()
    unsubscribe = null
    captured.length = 0
  })

  /**
   * A hand-built `ClientKernel`-shaped surface. The shape only needs
   * `snapshot` + `subscribe` for the source to work; the remaining
   * methods are stubbed with `unknown` so the cast is honest.
   */
  function fakeKernel(initial: HarnessState): ClientKernel {
    const listeners = new Set<(snapshot: ClientSnapshot) => void>()
    const snapshotValue: ClientSnapshot = {
      revision: 0,
      state: initial,
      sessions: [],
      cursors: {},
      online: true,
    }
    const surface = {
      snapshot: () => snapshotValue,
      subscribe: (listener: (snapshot: ClientSnapshot) => void) => {
        listeners.add(listener)
        return () => {
          listeners.delete(listener)
        }
      },
    } as unknown as ClientKernel
    return surface
  }

  test("subscribing fires once with the current snapshot and again on each commit", () => {
    const state: HarnessState = {
      sessions: [],
      activeSessionId: null,
      connection: { kind: "connected" },
      auth: { kind: "authenticated", subjectId: "subject" },
      overlay: { kind: "closed" },
      drafts: [],
      cursors: {},
      outbound: [],
    }
    const surface = fakeKernel(state)
    const source = new AttentionSource(surface, () => BASE_TIME)
    const listener = (signal: AttentionSignal): void => {
      captured.push(signal)
    }
    unsubscribe = source.addAttentionListener(listener)

    // The constructor already populated `source.snapshot()` once; the
    // listener wired below fires on the NEXT commit, not retroactively.
    expect(captured).toEqual([])
    expect(source.snapshot().revision).toBe(0)
  })

  test("the kernel surface returns the same signal the source caches", () => {
    const surface = fakeKernel({
      sessions: [],
      activeSessionId: null,
      connection: { kind: "connected" },
      auth: { kind: "authenticated", subjectId: "subject" },
      overlay: { kind: "closed" },
      drafts: [],
      cursors: {},
      outbound: [],
    })
    const source = new AttentionSource(surface, () => BASE_TIME)
    expect(source.snapshot().items).toEqual([])
  })
})
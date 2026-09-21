/**
 * Swarm canvas renderer (issue #78) — pure line-builder tests.
 *
 * Three coverage targets, mirroring the issue's edge cases:
 *
 * 1. Empty swarm — every worker P2; the canvas renders nothing.
 *    The host shows nothing in the right pane; focus mode goes
 *    quiet — this is the success case.
 * 2. Full swarm — P0 (awaiting) + P1 (failed, evidence) + a
 *    stacked-approval P0 row.
 * 3. One stalled worker — the P0 detail block explains the lease.
 *
 * The renderer reads the host-provided `sessions` map only when an
 * inspected row needs plan-node data; the empty test doesn't pass
 * one in.
 */

import { describe, expect, test } from "bun:test"
import { renderSwarmCanvas, type SwarmCanvasContext } from "./swarmcanvas"
import { lineText } from "./styled"
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
import { createI18nFor, type I18nInstance } from "../locales"

const BASE_TIME = 1_000_000

let i18nPromise: Promise<I18nInstance> | null = null
async function getI18n(): Promise<I18nInstance> {
  if (i18nPromise === null) {
    i18nPromise = createI18nFor("en")
  }
  return i18nPromise
}

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
  return { revision: 0, state }
}

function signalFor(
  sessions: readonly HarnessSession[],
  cursors: Record<string, number> = {},
  nowMs = BASE_TIME + 1_000
): AttentionSignal {
  return deriveAttention(snapshot(sessions, cursors), { nowUnixMs: nowMs })
}

async function renderAt(
  signal: AttentionSignal,
  context: Omit<SwarmCanvasContext, "i18n" | "width">,
  width: number = 80
): Promise<{ readonly text: string; readonly lines: readonly string[] }> {
  const i18n = await getI18n()
  const lines = renderSwarmCanvas(signal, { ...context, i18n, width })
  return {
    text: lines.map(lineText).join("\n"),
    lines: lines.map(lineText),
  }
}

describe("swarm canvas — empty / full / stacked / stalled", () => {
  test("empty swarm renders nothing — focus mode goes quiet", async () => {
    const signal = signalFor([])
    const { text, lines } = await renderAt(signal, { inspectedId: null, sessions: new Map() })
    expect(text).toBe("")
    expect(lines).toEqual([])
  })

  test("P2-only swarm hides the P2 section — focus mode is silent", async () => {
    const signal = signalFor([
      session(sessionId("server-1"), {
        turn: {
          kind: "thinking",
          requestId: turnRequestId("turn-active"),
          accumulatedText: "",
        },
      }),
    ])
    const { text, lines } = await renderAt(signal, { inspectedId: null, sessions: new Map() })
    expect(text).toBe("")
    expect(lines).toEqual([])
  })

  test("full swarm surfaces one row per priority with a per-section header", async () => {
    const stalledFor = DEFAULT_STALL_THRESHOLD_MS + 5_000
    const nowMs = BASE_TIME + stalledFor
    const signal = signalFor(
      [
        session(sessionId("server-approval"), {
          turn: {
            kind: "awaiting-approval",
            requestId: turnRequestId("turn-approval"),
          },
          pendingPlan: {
            scope: "tests/*",
            nodes: [
              { id: "n1", profileKey: "explore", title: "explore api", brief: "explore api" },
              { id: "n2", profileKey: "review", title: "review", brief: "review diffs" },
              { id: "n3", profileKey: "ship", title: "ship", brief: "ship release" },
            ],
          },
        }),
        session(sessionId("server-stalled"), {
          turn: {
            kind: "thinking",
            requestId: turnRequestId("turn-stalled"),
            accumulatedText: "",
          },
        }),
        session(sessionId("server-evidence"), {
          unread: true,
          turn: {
            kind: "thinking",
            requestId: turnRequestId("turn-evidence"),
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
    const sessions = new Map<string, HarnessSession>([
      ["server-approval", signal.items[0]?.sessionId
        ? session(sessionId("server-approval"), {
            turn: {
              kind: "awaiting-approval",
              requestId: turnRequestId("turn-approval"),
            },
            pendingPlan: {
              scope: "tests/*",
              nodes: [
                { id: "n1", profileKey: "explore", title: "explore api", brief: "explore api" },
                { id: "n2", profileKey: "review", title: "review", brief: "review diffs" },
                { id: "n3", profileKey: "ship", title: "ship", brief: "ship release" },
              ],
            },
          })
        : session(sessionId("server-approval"))],
    ])
    const { text, lines } = await renderAt(signal, { inspectedId: null,
      sessions,
    })
    // The canvas header appears once.
    expect(text).toContain("swarm · attention")
    // Section headers render top-to-bottom: P0, P1.
    const headerIndices = lines
      .map((line, index) => ({ line, index }))
      .filter(({ line }) => line.includes("p0 · needs you") || line.includes("p1 · review"))
      .map(({ index }) => index)
    expect(headerIndices.length).toBeGreaterThanOrEqual(2)
    // P0 rows lead; stalled and awaiting both surface.
    expect(text).toContain("stalled")
    expect(text).toContain("awaiting")
    // P1 rows trail: evidence + failed.
    expect(text).toContain("evidence unread")
    expect(text).toContain("failed")
  })

  test("stacked approval surfaces one P0 row with plan-node detail when inspected", async () => {
    const signal = signalFor([
      session(sessionId("server-approval"), {
        turn: {
          kind: "awaiting-approval",
          requestId: turnRequestId("turn-approval"),
        },
        pendingPlan: {
          scope: "tests/*",
          nodes: [
            { id: "n1", profileKey: "explore", title: "explore api", brief: "explore api" },
            { id: "n2", profileKey: "review", title: "review", brief: "review diffs" },
            { id: "n3", profileKey: "ship", title: "ship", brief: "ship release" },
          ],
        },
      }),
    ])
    const sessions = new Map<string, HarnessSession>([
      [
        "server-approval",
        session(sessionId("server-approval"), {
          turn: {
            kind: "awaiting-approval",
            requestId: turnRequestId("turn-approval"),
          },
          pendingPlan: {
            scope: "tests/*",
            nodes: [
              { id: "n1", profileKey: "explore", title: "explore api", brief: "explore api" },
              { id: "n2", profileKey: "review", title: "review", brief: "review diffs" },
              { id: "n3", profileKey: "ship", title: "ship", brief: "ship release" },
            ],
          },
        }),
      ],
    ])
const { text } = await renderAt(
      signal,
      {
        inspectedId: "server-approval",
        sessions,
      },
      80
    )
    expect(text).toContain("tests/*")
    expect(text).toContain("explore")
    expect(text).toContain("review")
    expect(text).toContain("ship")
  })

  test("stalled worker detail explains the lease in the P0 row", async () => {
    const stalledFor = DEFAULT_STALL_THRESHOLD_MS + 2_000
    const signal = signalFor(
      [
        session(sessionId("server-stalled"), {
          turn: {
            kind: "thinking",
            requestId: turnRequestId("turn-stalled"),
            accumulatedText: "",
          },
        }),
      ],
      {},
      BASE_TIME + stalledFor
    )
const { text } = await renderAt(
      signal,
      {
        inspectedId: "server-stalled",
        sessions: new Map(),
      },
      80
    )
    expect(text).toContain("p0 · needs you")
    expect(text).toContain("stalled")
    expect(text).toContain("no heartbeat for")
  })

  test("every row carries the correlation id (deep-link target)", async () => {
    const signal = signalFor([
      session(sessionId("server-1"), {
        turn: {
          kind: "awaiting-approval",
          requestId: turnRequestId("turn-correlation"),
        },
      }),
    ])
    const { text } = await renderAt(signal, { inspectedId: null, sessions: new Map() })
    expect(text).toContain("turn-correlation")
  })

  test("the canvas never leaks lines past the right edge — narrow viewports clip", async () => {
    const longTitle = "x".repeat(120)
    const signal = signalFor([
      session(sessionId("server-1"), {
        title: longTitle,
        turn: {
          kind: "awaiting-approval",
          requestId: turnRequestId("turn-long"),
        },
      }),
    ])
    const { lines } = await renderAt(
      signal,
      {
        inspectedId: null,
        sessions: new Map(),
      },
      30
    )
    expect(lines.length).toBeGreaterThan(0)
    for (const line of lines) {
      expect(line.length).toBeLessThanOrEqual(30)
    }
  })
})



import { describe, expect, it } from "bun:test"
import type { Session } from "../lib/session-state"
import { harnessSessionFromLegacy } from "./compat"
import { pendingSessionId, sessionId, turnRequestId } from "./state"

function legacySession(overrides: Partial<Session> = {}): Session {
  return {
    id: "server-1",
    name: "Legacy",
    status: "thinking",
    createdAt: 10,
    unread: true,
    awaitingApproval: false,
    pendingPlan: null,
    blocks: [
      {
        kind: "message",
        key: "block-1",
        message: {
          id: "message-1",
          role: "assistant",
          content: "Answer",
          toolName: null,
          parts: null,
          meta: null,
          createdAt: "2026-09-18T00:00:00.000Z",
        },
      },
      { kind: "lines", key: "block-2", lines: ["presentation only"] },
    ],
    liveText: "Working",
    hydrated: true,
    blocksExpanded: false,
    lastUserMessage: "Question",
    renamed: true,
    history: ["Question"],
    queued: ["Follow-up"],
    ...overrides,
  }
}

describe("harnessSessionFromLegacy", () => {
  it("maps a remote legacy session without carrying presentation blocks", () => {
    const result = harnessSessionFromLegacy(legacySession())

    expect(result.identity).toEqual({
      kind: "remote",
      id: sessionId("server-1"),
    })
    expect(result.turn).toEqual({
      kind: "thinking",
      requestId: turnRequestId("legacy:server-1"),
      accumulatedText: "Working",
    })
    expect(result.transcript).toEqual([
      {
        id: "message-1",
        role: "assistant",
        content: "Answer",
        createdAtUnixMs: 1_789_689_600_000,
      },
    ])
    expect(result.queue[0]?.message).toBe("Follow-up")
  })

  it("preserves pending identity and approval as orthogonal state", () => {
    const result = harnessSessionFromLegacy(
      legacySession({
        id: "local-10-0",
        status: "idle",
        awaitingApproval: true,
        hydrated: false,
      })
    )

    expect(result.identity).toEqual({
      kind: "pending",
      id: pendingSessionId("local-10-0"),
    })
    expect(result.turn.kind).toBe("awaiting-approval")
    expect(result.transcriptLoad).toEqual({ kind: "not-loaded" })
  })
})

import { describe, expect, it } from "bun:test"
import { activeSession, attentionCount } from "./selectors"
import { reduceHarness } from "./reducer"
import {
  initialHarnessState,
  pendingSessionId,
  projectId,
  sessionId,
  turnRequestId,
  type CliError,
  type HarnessState,
} from "./state"

const pendingId = pendingSessionId("local-1")
const remoteId = sessionId("session-1")
const requestId = turnRequestId("turn-1")
const retryableError: CliError = {
  kind: "network",
  code: "network.unreachable",
  message: "unreachable",
  retryable: true,
}

function pendingState(): HarnessState {
  return reduceHarness(initialHarnessState(), {
    type: "pending-session-opened",
    pendingSessionId: pendingId,
    projectId: projectId("project-1"),
    createdAtUnixMs: 1,
  }).state
}

function remoteState(): HarnessState {
  return reduceHarness(pendingState(), {
    type: "remote-session-adopted",
    pendingSessionId: pendingId,
    sessionId: remoteId,
    projectId: projectId("project-1"),
    title: "First task",
  }).state
}

describe("reduceHarness session lifecycle", () => {
  it("opens and focuses a pending session", () => {
    const transition = reduceHarness(initialHarnessState(), {
      type: "pending-session-opened",
      pendingSessionId: pendingId,
      projectId: null,
      createdAtUnixMs: 42,
    })

    expect(transition.state.activeSessionId).toBe(pendingId)
    expect(transition.state.sessions[0]?.identity).toEqual({
      kind: "pending",
      id: pendingId,
    })
    expect(transition.effects.map((effect) => effect.type)).toEqual([
      "persist-sessions",
    ])
  })

  it("creates a remote session before submitting the first pending turn", () => {
    const transition = reduceHarness(pendingState(), {
      type: "turn-queued",
      sessionId: pendingId,
      requestId,
      message: "Investigate",
    })

    expect(transition.state.sessions[0]?.queue).toHaveLength(1)
    expect(transition.effects).toEqual([
      {
        type: "create-remote-session",
        pendingSessionId: pendingId,
        projectId: projectId("project-1"),
        title: "",
        requestId,
      },
    ])
  })

  it("adopts the remote id and submits the queued turn", () => {
    const queued = reduceHarness(pendingState(), {
      type: "turn-queued",
      sessionId: pendingId,
      requestId,
      message: "Investigate",
    }).state

    const transition = reduceHarness(queued, {
      type: "remote-session-adopted",
      pendingSessionId: pendingId,
      sessionId: remoteId,
      projectId: projectId("project-1"),
      title: "Investigation",
    })

    expect(transition.state.activeSessionId).toBe(remoteId)
    expect(transition.state.sessions[0]?.identity).toEqual({
      kind: "remote",
      id: remoteId,
    })
    expect(transition.effects.map((effect) => effect.type)).toEqual([
      "persist-sessions",
      "set-subscriptions",
      "submit-turn",
    ])
  })

  it("streams a turn and submits the next queued turn after completion", () => {
    const firstQueued = reduceHarness(remoteState(), {
      type: "turn-queued",
      sessionId: remoteId,
      requestId,
      message: "First",
    })
    const thinking = reduceHarness(firstQueued.state, {
      type: "thinking-started",
      sessionId: remoteId,
      requestId,
    })
    const chunked = reduceHarness(thinking.state, {
      type: "thinking-chunk-received",
      sessionId: remoteId,
      requestId,
      text: "partial",
    })
    const secondRequestId = turnRequestId("turn-2")
    const secondQueued = reduceHarness(chunked.state, {
      type: "turn-queued",
      sessionId: remoteId,
      requestId: secondRequestId,
      message: "Second",
    })

    const completed = reduceHarness(secondQueued.state, {
      type: "turn-completed",
      sessionId: remoteId,
      requestId,
      messages: [
        {
          id: "message-1",
          role: "assistant",
          content: "Done",
          createdAtUnixMs: 2,
        },
      ],
      awaitingApproval: false,
    })

    expect(chunked.state.sessions[0]?.turn).toEqual({
      kind: "thinking",
      requestId,
      accumulatedText: "partial",
    })
    expect(completed.state.sessions[0]?.turn).toEqual({ kind: "idle" })
    expect(completed.effects).toEqual([
      {
        type: "submit-turn",
        sessionId: remoteId,
        requestId: secondRequestId,
        message: "Second",
      },
    ])
  })

  it("holds the queue while approval is required", () => {
    const transition = reduceHarness(remoteState(), {
      type: "turn-completed",
      sessionId: remoteId,
      requestId,
      messages: [],
      awaitingApproval: true,
    })

    expect(transition.state.sessions[0]?.turn).toEqual({
      kind: "awaiting-approval",
      requestId,
    })
    expect(transition.effects).toEqual([])
  })

  it("marks background output unread and clears it on focus", () => {
    const secondId = sessionId("session-2")
    const withSecond = reduceHarness(remoteState(), {
      type: "pending-session-opened",
      pendingSessionId: pendingSessionId("local-2"),
      projectId: null,
      createdAtUnixMs: 2,
    }).state
    const adopted = reduceHarness(withSecond, {
      type: "remote-session-adopted",
      pendingSessionId: pendingSessionId("local-2"),
      sessionId: secondId,
      projectId: null,
      title: "Second",
    }).state

    const unread = reduceHarness(adopted, {
      type: "session-output-received",
      sessionId: remoteId,
    }).state
    const focused = reduceHarness(unread, {
      type: "session-focused",
      sessionId: remoteId,
    }).state

    expect(unread.sessions[0]?.unread).toBe(true)
    expect(focused.sessions[0]?.unread).toBe(false)
  })

  it("selectors read the post-focus state (HarnessEngine step 1)", () => {
    const focused = reduceHarness(remoteState(), {
      type: "session-focused",
      sessionId: remoteId,
    }).state

    expect(activeSession(focused)?.identity.id).toBe(remoteId)
    expect(attentionCount(focused)).toBe(0)
  })

  it("records a failed turn without discarding its transcript", () => {
    const state = remoteState()
    const transition = reduceHarness(state, {
      type: "turn-failed",
      sessionId: remoteId,
      requestId,
      error: retryableError,
    })

    expect(transition.state.sessions[0]?.turn).toEqual({
      kind: "failed",
      requestId,
      error: retryableError,
    })
    expect(transition.state.sessions[0]?.transcript).toBe(
      state.sessions[0]?.transcript
    )
  })

  it("closes the active session and focuses its neighbour", () => {
    const secondPendingId = pendingSessionId("local-2")
    const state = reduceHarness(remoteState(), {
      type: "pending-session-opened",
      pendingSessionId: secondPendingId,
      projectId: null,
      createdAtUnixMs: 2,
    }).state

    const transition = reduceHarness(state, {
      type: "session-closed",
      sessionId: secondPendingId,
    })

    expect(transition.state.activeSessionId).toBe(remoteId)
    expect(transition.state.sessions).toHaveLength(1)
    expect(transition.effects.map((effect) => effect.type)).toEqual([
      "persist-sessions",
      "set-subscriptions",
    ])
  })
})

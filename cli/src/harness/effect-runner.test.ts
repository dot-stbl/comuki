import { describe, expect, it } from "bun:test"
import { ComukiApiError } from "../lib/client"
import {
  normalizeCliError,
  runEffect,
  type HarnessEffectPorts,
} from "./effect-runner"
import type { HarnessEvent } from "./events"
import {
  pendingSessionId,
  projectId,
  sessionId,
  turnRequestId,
} from "./state"
import { WORKSPACE_DOCUMENT_VERSION } from "./workspace"

const remoteId = sessionId("session-1")
const pendingId = pendingSessionId("local-1")
const requestId = turnRequestId("turn-1")

function fakePorts(
  overrides: Partial<HarnessEffectPorts> = {}
): HarnessEffectPorts {
  return {
    conversation: {
      async openConversation() {
        return {
          sessionId: remoteId,
          projectId: projectId("project-1"),
          title: "Task",
        }
      },
      async submitTurn() {
        return { messages: [], awaitingApproval: false }
      },
      async cancelTurn() {},
      async loadConversation() {
        return []
      },
    },
    approval: {
      async decide() {
        return { messages: [], awaitingApproval: false }
      },
    },
    realtime: {
      async setSubscriptions() {},
    },
    workspace: {
      async read() {
        return null
      },
      async write() {},
    },
    ...overrides,
  }
}

function eventCollector(): {
  readonly events: HarnessEvent[]
  readonly dispatch: (event: HarnessEvent) => void
} {
  const events: HarnessEvent[] = []
  return { events, dispatch: (event) => events.push(event) }
}

describe("runEffect", () => {
  it("persists sessions and dispatches completion", async () => {
    const writes: string[] = []
    const ports = fakePorts({
      workspace: {
        async read() {
          return null
        },
        async write(value, signal) {
          expect(signal.aborted).toBe(false)
          writes.push(String(value.activeSessionId))
        },
      },
    })
    const collected = eventCollector()

    await runEffect(
      {
        type: "persist-sessions",
        value: {
          version: WORKSPACE_DOCUMENT_VERSION,
          activeSessionId: remoteId,
          sessions: [],
          drafts: [],
          cursors: {},
          outbound: [],
        },
      },
      ports,
      collected.dispatch,
      new AbortController().signal
    )

    expect(writes).toEqual(["session-1"])
    expect(collected.events).toEqual([{ type: "sessions-persisted" }])
  })

  it("adopts the session returned by the API", async () => {
    const collected = eventCollector()

    await runEffect(
      {
        type: "create-remote-session",
        pendingSessionId: pendingId,
        projectId: projectId("project-1"),
        title: "Task",
        requestId,
      },
      fakePorts(),
      collected.dispatch,
      new AbortController().signal
    )

    expect(collected.events).toEqual([
      {
        type: "remote-session-adopted",
        pendingSessionId: pendingId,
        sessionId: remoteId,
        projectId: projectId("project-1"),
        title: "Task",
      },
    ])
  })

  it("dispatches thinking before the completed turn", async () => {
    const collected = eventCollector()

    await runEffect(
      {
        type: "submit-turn",
        sessionId: remoteId,
        requestId,
        message: "Hello",
      },
      fakePorts({
        conversation: {
          ...fakePorts().conversation,
          async submitTurn(session, message, commandId, signal) {
            expect(session).toBe(remoteId)
            expect(message).toBe("Hello")
            expect(commandId).toBeUndefined()
            expect(signal.aborted).toBe(false)
            return {
              messages: [
                {
                  id: "message-1",
                  role: "assistant",
                  content: "Hi",
                  createdAtUnixMs: 1,
                },
              ],
              awaitingApproval: false,
            }
          },
        },
      }),
      collected.dispatch,
      new AbortController().signal
    )

    expect(collected.events.map((event) => event.type)).toEqual([
      "thinking-started",
      "turn-completed",
    ])
  })

  it("normalizes API failures into typed turn failures", async () => {
    const collected = eventCollector()

    await runEffect(
      {
        type: "submit-turn",
        sessionId: remoteId,
        requestId,
        message: "Hello",
      },
      fakePorts({
        conversation: {
          ...fakePorts().conversation,
          async submitTurn() {
            throw new ComukiApiError(503, "provider.unavailable", "Unavailable")
          },
        },
      }),
      collected.dispatch,
      new AbortController().signal
    )

    expect(collected.events[1]).toEqual({
      type: "turn-failed",
      sessionId: remoteId,
      requestId,
      error: {
        kind: "server",
        code: "provider.unavailable",
        message: "Unavailable",
        retryable: true,
      },
    })
  })

  it("sets realtime subscriptions with the caller signal", async () => {
    const subscriptions: readonly string[][] = []
    const mutableSubscriptions = subscriptions as string[][]
    const collected = eventCollector()

    await runEffect(
      { type: "set-subscriptions", sessionIds: [remoteId] },
      fakePorts({
        realtime: {
          async setSubscriptions(sessionIds, signal) {
            expect(signal.aborted).toBe(false)
            mutableSubscriptions.push([...sessionIds])
          },
        },
      }),
      collected.dispatch,
      new AbortController().signal
    )

    expect(subscriptions).toEqual([[remoteId]])
    expect(collected.events).toEqual([
      { type: "subscriptions-set", sessionIds: [remoteId] },
    ])
  })

  it("completes an approval decision through the approval port", async () => {
    const collected = eventCollector()

    await runEffect(
      {
        type: "decide-approval",
        sessionId: remoteId,
        requestId,
        approved: false,
        reason: "too risky",
      },
      fakePorts({
        approval: {
          async decide(session, approved, reason, commandId, signal) {
            expect(session).toBe(remoteId)
            expect(approved).toBe(false)
            expect(reason).toBe("too risky")
            expect(commandId).toBeUndefined()
            expect(signal.aborted).toBe(false)
            return { messages: [], awaitingApproval: false }
          },
        },
      }),
      collected.dispatch,
      new AbortController().signal
    )

    expect(collected.events).toEqual([
      {
        type: "turn-completed",
        sessionId: remoteId,
        requestId,
        messages: [],
        awaitingApproval: false,
        pendingPlan: undefined,
      },
    ])
  })
})

describe("normalizeCliError", () => {
  it("does not expose non-error values", () => {
    expect(normalizeCliError({ secret: "hidden" })).toEqual({
      kind: "unknown",
      code: "cli.unexpected",
      message: "Unexpected failure",
      retryable: false,
    })
  })
})

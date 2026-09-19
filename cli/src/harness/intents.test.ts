import { describe, expect, it } from "bun:test"
import { translateIntent } from "./intents"
import { reduceHarness } from "./reducer"
import {
  initialHarnessState,
  pendingSessionId,
  sessionId,
  turnRequestId,
} from "./state"

const pendingId = pendingSessionId("local-1")
const remoteId = sessionId("session-1")

function remoteState() {
  const opened = reduceHarness(initialHarnessState(), {
    type: "pending-session-opened",
    pendingSessionId: pendingId,
    projectId: null,
    createdAtUnixMs: 1,
  }).state
  return reduceHarness(opened, {
    type: "remote-session-adopted",
    pendingSessionId: pendingId,
    sessionId: remoteId,
    projectId: null,
    title: "Task",
  }).state
}

describe("translateIntent", () => {
  it("opens a pending session", () => {
    const events = translateIntent(
      initialHarnessState(),
      { kind: "open-session" },
      500
    )
    expect(events).toEqual([
      {
        type: "pending-session-opened",
        pendingSessionId: pendingSessionId("local-500-0"),
        projectId: null,
        createdAtUnixMs: 500,
      },
    ])
  })

  it("derives the request id from the stable command id", () => {
    const events = translateIntent(remoteState(), {
      kind: "submit-turn",
      sessionId: remoteId,
      message: "Hello",
      commandId: "cmd-42",
      echoText: "Hello",
    }, 0)

    expect(events).toEqual([
      {
        type: "turn-queued",
        sessionId: remoteId,
        requestId: turnRequestId("cmd-42"),
        message: "Hello",
        commandId: "cmd-42",
        echoText: "Hello",
      },
    ])
  })

  it("drops a duplicate command id — repeated sends never duplicate", () => {
    const state = {
      ...remoteState(),
      outbound: [
        {
          commandId: "cmd-42",
          requestId: turnRequestId("cmd-42"),
          sessionId: remoteId,
          kind: "turn" as const,
          message: "Hello",
          state: "in-flight" as const,
        },
      ],
    }

    const events = translateIntent(state, {
      kind: "submit-turn",
      sessionId: remoteId,
      message: "Hello",
      commandId: "cmd-42",
    }, 0)

    expect(events).toEqual([])
  })

  it("drops a submit for an unknown session", () => {
    const events = translateIntent(initialHarnessState(), {
      kind: "submit-turn",
      sessionId: sessionId("ghost"),
      message: "Hello",
      commandId: "cmd-1",
    }, 0)
    expect(events).toEqual([])
  })

  it("decides an approval only while one is actually awaited", () => {
    const awaiting = reduceHarness(remoteState(), {
      type: "turn-completed",
      sessionId: remoteId,
      requestId: turnRequestId("cmd-1"),
      messages: [],
      awaitingApproval: true,
    }).state

    expect(
      translateIntent(awaiting, {
        kind: "decide-approval",
        sessionId: remoteId,
        approved: false,
        reason: "no",
        commandId: "cmd-approve",
      }, 0)
    ).toEqual([
      {
        type: "approval-decision-sent",
        sessionId: remoteId,
        requestId: turnRequestId("approval:cmd-approve"),
        approved: false,
        reason: "no",
        commandId: "cmd-approve",
      },
    ])

    expect(
      translateIntent(remoteState(), {
        kind: "decide-approval",
        sessionId: remoteId,
        approved: true,
        commandId: "cmd-approve",
      }, 0)
    ).toEqual([])
  })

  it("cancels only a thinking remote turn", () => {
    const thinking = reduceHarness(remoteState(), {
      type: "thinking-started",
      sessionId: remoteId,
      requestId: turnRequestId("cmd-1"),
    }).state

    expect(
      translateIntent(thinking, {
        kind: "cancel-turn",
        sessionId: remoteId,
      }, 0)
    ).toEqual([
      {
        type: "turn-cancel-requested",
        sessionId: remoteId,
        requestId: turnRequestId("cmd-1"),
        commandId: undefined,
      },
    ])

    expect(
      translateIntent(remoteState(), { kind: "cancel-turn", sessionId: remoteId }, 0)
    ).toEqual([])
  })

  it("ignores a rename to a blank title", () => {
    expect(
      translateIntent(remoteState(), {
        kind: "rename-session",
        sessionId: remoteId,
        title: "   ",
      }, 0)
    ).toEqual([])
  })

  it("saves and clears drafts", () => {
    expect(
      translateIntent(remoteState(), {
        kind: "save-draft",
        sessionId: remoteId,
        text: "wip",
      }, 33)
    ).toEqual([
      {
        type: "draft-saved",
        sessionId: remoteId,
        text: "wip",
        savedAtUnixMs: 33,
      },
    ])

    expect(
      translateIntent(remoteState(), {
        kind: "clear-draft",
        sessionId: remoteId,
      }, 33)
    ).toEqual([{ type: "draft-cleared", sessionId: remoteId }])
  })
})

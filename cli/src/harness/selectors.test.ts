import { describe, expect, it } from "bun:test"
import {
  activeSession,
  attentionCount,
  compactHeaderModel,
  contextualWorkbenchVisible,
  isBusy,
  sessionSwitcherItems,
} from "./selectors"
import {
  initialHarnessState,
  pendingSessionId,
  sessionId,
  turnRequestId,
  type HarnessSession,
  type HarnessState,
} from "./state"

const firstId = sessionId("session-1")
const secondId = pendingSessionId("local-2")

function session(
  identity: HarnessSession["identity"],
  overrides: Partial<HarnessSession> = {}
): HarnessSession {
  return {
    identity,
    projectId: null,
    title: identity.id,
    createdAtUnixMs: 1,
    renamed: false,
    unread: false,
    turn: { kind: "idle" },
    transcriptLoad: { kind: "not-loaded" },
    transcript: [],
    queue: [],
    ...overrides,
  }
}

function populatedState(): HarnessState {
  return {
    ...initialHarnessState(),
    sessions: [
      session({ kind: "remote", id: firstId }, { unread: true }),
      session({ kind: "pending", id: secondId }, {
        turn: {
          kind: "thinking",
          requestId: turnRequestId("turn-2"),
          accumulatedText: "",
        },
      }),
    ],
    activeSessionId: secondId,
    connection: { kind: "connected" },
    auth: { kind: "authenticated", subjectId: "user-1" },
    overlay: { kind: "workbench", sessionId: secondId },
  }
}

describe("harness selectors", () => {
  it("selects the active session", () => {
    expect(activeSession(populatedState())?.identity.id).toBe(secondId)
  })

  it("counts sessions requiring attention", () => {
    expect(attentionCount(populatedState())).toBe(1)
  })

  it("reports busy state across sessions", () => {
    expect(isBusy(populatedState())).toBe(true)
  })

  it("projects ordered switcher items", () => {
    expect(sessionSwitcherItems(populatedState())).toEqual([
      {
        id: firstId,
        title: firstId,
        active: false,
        unread: true,
        busy: false,
        pending: false,
      },
      {
        id: secondId,
        title: secondId,
        active: true,
        unread: false,
        busy: true,
        pending: true,
      },
    ])
  })

  it("shows the workbench only for the active session", () => {
    const state = populatedState()

    expect(contextualWorkbenchVisible(state)).toBe(true)
    expect(
      contextualWorkbenchVisible({
        ...state,
        overlay: { kind: "workbench", sessionId: firstId },
      })
    ).toBe(false)
  })

  it("builds a compact presentation-neutral header model", () => {
    expect(compactHeaderModel(populatedState())).toEqual({
      sessionId: secondId,
      title: secondId,
      connection: "connected",
      auth: "authenticated",
      busy: true,
      attentionCount: 1,
    })
  })
})

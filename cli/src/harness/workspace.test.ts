import { describe, expect, it } from "bun:test"
import { reduceHarness } from "./reducer"
import {
  pendingSessionId,
  sessionId,
  initialHarnessState,
  turnRequestId,
} from "./state"
import {
  applyWorkspaceToState,
  migrateWorkspaceDocument,
  WORKSPACE_DOCUMENT_VERSION,
  workspaceDocumentFromState,
  workspaceFromState,
} from "./workspace"

const pendingId = pendingSessionId("local-100-0")
const remoteId = sessionId("server-1")

function stateWithRemoteSession() {
  return reduceHarness(initialHarnessState(), {
    type: "pending-session-opened",
    pendingSessionId: pendingId,
    projectId: null,
    createdAtUnixMs: 100,
  }).state
}

function adoptedState() {
  return reduceHarness(stateWithRemoteSession(), {
    type: "remote-session-adopted",
    pendingSessionId: pendingId,
    sessionId: remoteId,
    projectId: null,
    title: "First task",
  }).state
}

describe("workspace extraction", () => {
  it("extracts session refs, drafts and cursors from state", () => {
    const state = {
      ...adoptedState(),
      drafts: [{ sessionId: remoteId, text: "half-written", updatedAtUnixMs: 5 }],
      cursors: { [remoteId]: 42 },
      outbound: [
        {
          commandId: "cmd-1",
          requestId: turnRequestId("cmd-1"),
          sessionId: remoteId,
          kind: "turn" as const,
          message: "Hello",
          state: "settled" as const,
        },
      ],
    }

    const workspace = workspaceFromState(state)

    expect(workspace.activeSessionId).toBe(remoteId)
    expect(workspace.sessions).toEqual([
      {
        id: remoteId,
        title: "First task",
        renamed: false,
        createdAtUnixMs: 100,
        lastStatus: "idle",
        history: [],
      },
    ])
    expect(workspace.drafts).toEqual([
      { sessionId: remoteId, text: "half-written", updatedAtUnixMs: 5 },
    ])
    expect(workspace.cursors).toEqual({ [remoteId]: 42 })
    expect(workspace.outbound[0]?.commandId).toBe("cmd-1")
  })

  it("stamps the version on the durable document", () => {
    const document = workspaceDocumentFromState(adoptedState())
    expect(document.version).toBe(WORKSPACE_DOCUMENT_VERSION)
  })
})

describe("workspace restore", () => {
  it("restores sessions as idle with not-loaded transcripts", () => {
    const workspace = workspaceFromState(adoptedState())
    const restored = applyWorkspaceToState(
      initialHarnessState(),
      workspace
    )

    expect(restored.sessions).toHaveLength(1)
    expect(restored.sessions[0]?.identity).toEqual({
      kind: "remote",
      id: remoteId,
    })
    expect(restored.sessions[0]?.turn).toEqual({ kind: "idle" })
    expect(restored.sessions[0]?.transcriptLoad).toEqual({ kind: "not-loaded" })
    expect(restored.activeSessionId).toBe(remoteId)
  })

  it("restores a pending session reference as pending", () => {
    const workspace = workspaceFromState(stateWithRemoteSession())
    const restored = applyWorkspaceToState(initialHarnessState(), workspace)

    expect(restored.sessions[0]?.identity).toEqual({
      kind: "pending",
      id: pendingId,
    })
  })

  it("round-trips through the document without losing refs", () => {
    const document = workspaceDocumentFromState(adoptedState())
    const migrated = migrateWorkspaceDocument(document)
    expect(migrated.sessions).toEqual(workspaceFromState(adoptedState()).sessions)
    expect(migrated.activeSessionId).toBe(remoteId)
  })
})

describe("migration from the legacy v1 sessions document", () => {
  it("migrates the legacy sessions.json shape losslessly", () => {
    const legacy = {
      activeSessionId: "server-9",
      sessions: [
        {
          id: "server-9",
          name: "Refactor Identity",
          status: "thinking",
          createdAt: 1_760_000_000_000,
          renamed: true,
          history: ["make a plan", "continue"],
        },
        {
          id: "server-10",
          name: "",
          status: "done",
          createdAt: 1_760_000_100_000,
        },
      ],
    }

    const workspace = migrateWorkspaceDocument(legacy)

    expect(workspace.activeSessionId).toBe(sessionId("server-9"))
    expect(workspace.sessions).toEqual([
      {
        id: "server-9",
        title: "Refactor Identity",
        renamed: true,
        createdAtUnixMs: 1_760_000_000_000,
        lastStatus: "thinking",
        history: ["make a plan", "continue"],
      },
      {
        id: "server-10",
        title: "session",
        renamed: false,
        createdAtUnixMs: 1_760_000_100_000,
        lastStatus: "done",
        history: [],
      },
    ])
    expect(workspace.drafts).toEqual([])
    expect(workspace.cursors).toEqual({})
    expect(workspace.outbound).toEqual([])
  })

  it("decodes a v2 document with drafts, cursors and outbound", () => {
    const v2 = {
      version: 2,
      activeSessionId: "server-1",
      sessions: [
        {
          id: "server-1",
          title: "Task",
          renamed: false,
          createdAtUnixMs: 1,
          lastStatus: "done",
          history: [],
        },
      ],
      drafts: [{ sessionId: "server-1", text: "draft", updatedAtUnixMs: 9 }],
      cursors: { "server-1": 77 },
      outbound: [
        {
          commandId: "cmd-1",
          requestId: "cmd-1",
          sessionId: "server-1",
          kind: "turn",
          message: "Hello",
          state: "settled",
        },
      ],
    }

    const workspace = migrateWorkspaceDocument(v2)

    expect(workspace.sessions[0]?.title).toBe("Task")
    expect(workspace.drafts[0]?.text).toBe("draft")
    expect(workspace.cursors["server-1"]).toBe(77)
    expect(workspace.outbound[0]?.commandId).toBe("cmd-1")
  })

  it("maps legacy running status onto thinking", () => {
    const workspace = migrateWorkspaceDocument({
      sessions: [{ id: "s", name: "n", status: "running", createdAt: 1 }],
    })
    expect(workspace.sessions[0]?.lastStatus).toBe("thinking")
  })

  it("yields the empty workspace for corrupt documents, never throws", () => {
    for (const corrupt of [null, 42, "nope", {}, { sessions: 5 }, { sessions: [7, null] }]) {
      const workspace = migrateWorkspaceDocument(corrupt)
      expect(workspace.sessions).toEqual([])
      expect(workspace.activeSessionId).toBeNull()
    }
  })
})

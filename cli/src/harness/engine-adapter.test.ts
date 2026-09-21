import { describe, expect, it } from "bun:test"
import {
  HarnessEngineAdapter,
  emptyHarnessEngine,
  type HarnessEnginePorts,
} from "./engine-adapter"
import type {
  ConversationPort,
  ApprovalPort,
  RealtimePort,
  WorkspaceStore,
} from "./effect-runner"
import type {
  HarnessMessage,
  SessionId,
} from "./state"
import {
  initialHarnessState,
  pendingSessionId,
  sessionId as harnessSessionId,
  type HarnessSession,
  type HarnessState,
} from "./state"
import type { OpenedConversation, TurnOutcome } from "./effect-runner"
import type { WorkspaceDocument } from "./workspace"

class RecordingPorts implements HarnessEnginePorts {
  readonly setSubscriptionsCalls: SessionId[][] = []
  readonly writes: WorkspaceDocument[] = []
  readonly conversation: ConversationPort
  readonly approval: ApprovalPort

  readonly realtime: RealtimePort = {
    setSubscriptions: async (sessionIds, _signal) => {
      this.setSubscriptionsCalls.push([...sessionIds])
    },
  }

  readonly workspace: WorkspaceStore = {
    read: async () => null,
    write: async (value: WorkspaceDocument, _signal) => {
      this.writes.push(value)
    },
  }

  constructor() {
    const noopConversation: ConversationPort = {
      openConversation: async (): Promise<OpenedConversation> => {
        throw new Error("not used in step 1")
      },
      submitTurn: async (
        _sessionId: SessionId,
        _message: string,
        _commandId: string | undefined,
        _signal: AbortSignal,
      ): Promise<TurnOutcome> => {
        throw new Error("not used in step 1")
      },
      cancelTurn: async (
        _sessionId: SessionId,
        _commandId: string | undefined,
        _signal: AbortSignal,
      ): Promise<void> => {
        throw new Error("not used in step 1")
      },
      loadConversation: async (
        _sessionId: SessionId,
        _signal: AbortSignal,
      ): Promise<readonly HarnessMessage[]> => {
        throw new Error("not used in step 1")
      },
    }
    const noopApproval: ApprovalPort = {
      decide: async (
        _sessionId: SessionId,
        _approved: boolean,
        _reason: string | undefined,
        _commandId: string | undefined,
        _signal: AbortSignal,
      ): Promise<TurnOutcome> => {
        throw new Error("not used in step 1")
      },
    }
    this.conversation = noopConversation
    this.approval = noopApproval
  }
}

function harnessSession(overrides: Partial<HarnessSession> = {}): HarnessSession {
  return {
    identity: { kind: "remote", id: harnessSessionId("session-1") },
    projectId: null,
    title: "Session 1",
    createdAtUnixMs: 1,
    renamed: false,
    unread: true,
    turn: { kind: "idle" },
    transcriptLoad: { kind: "not-loaded" },
    transcript: [],
    queue: [],
    history: [],
    lastUserMessage: null,
    pendingPlan: null,
    ...overrides,
  }
}

function harnessStateWith(
  sessions: readonly HarnessSession[],
  active: HarnessSession["identity"]["id"] | null = null,
): HarnessState {
  return {
    ...initialHarnessState(),
    sessions,
    activeSessionId: active,
  }
}

describe("HarnessEngineAdapter", () => {
  it("dispatch(focus) advances activeSessionId, clears unread, and emits the persist effect", async () => {
    const ports = new RecordingPorts()
    const session = harnessSession({ unread: true })
    const engine = new HarnessEngineAdapter({
      initial: harnessStateWith([session], session.identity.id),
      ports,
    })

    const seen: HarnessState[] = []
    engine.subscribe((state) => seen.push(state))

    await engine.dispatch({
      type: "session-focused",
      sessionId: session.identity.id,
    })

    expect(engine.snapshot.sessions[0]?.unread).toBe(false)
    expect(engine.snapshot.activeSessionId).toBe(session.identity.id)
    // One dispatch call notifies the subscriber once with the post-reduce
    // state; the persist confirmation re-enters and notifies again with
    // the unchanged state — that is the `unchanged(state)` branch in the
    // reducer for `sessions-persisted`.
    expect(seen.length).toBeGreaterThanOrEqual(1)
    expect(ports.writes.length).toBe(1)
    // Focus emits no `set-subscriptions`.
    expect(ports.setSubscriptionsCalls).toEqual([])
  })

  it("dispatch(close) removes the session and emits persist + set-subscriptions in order", async () => {
    const ports = new RecordingPorts()
    const a = harnessSession({ identity: { kind: "remote", id: harnessSessionId("a") } })
    const b = harnessSession({ identity: { kind: "remote", id: harnessSessionId("b") } })
    const engine = new HarnessEngineAdapter({
      initial: harnessStateWith([a, b], a.identity.id),
      ports,
    })

    await engine.dispatch({
      type: "session-closed",
      sessionId: b.identity.id,
    })

    expect(engine.snapshot.sessions.map((session) => session.identity.id)).toEqual([
      a.identity.id,
    ])
    expect(ports.writes.length).toBe(1)
    expect(ports.setSubscriptionsCalls).toEqual([[a.identity.id as SessionId]])
  })

  it("subscriber receives the same state again when the no-op confirmation event re-enters dispatch", async () => {
    const ports = new RecordingPorts()
    const session = harnessSession()
    const engine = new HarnessEngineAdapter({
      initial: harnessStateWith([session], session.identity.id),
      ports,
    })

    const seen: HarnessState[] = []
    engine.subscribe((state) => seen.push(state))

    await engine.dispatch({
      type: "session-focused",
      sessionId: session.identity.id,
    })

    // At minimum: the post-reduce snapshot, plus the post-`sessions-persisted`
    // snapshot (which is unchanged).
    expect(seen.length).toBeGreaterThanOrEqual(2)
    expect(seen[0]).toBe(seen[seen.length - 1])
  })

  it("subscribe returns an unsubscribe function", async () => {
    const ports = new RecordingPorts()
    const session = harnessSession()
    const engine = new HarnessEngineAdapter({
      initial: harnessStateWith([session]),
      ports,
    })

    const seen: HarnessState[] = []
    const unsubscribe = engine.subscribe((state) => seen.push(state))

    await engine.dispatch({
      type: "session-focused",
      sessionId: session.identity.id,
    })
    const afterFirst = seen.length

    unsubscribe()

    await engine.dispatch({
      type: "session-closed",
      sessionId: session.identity.id,
    })
    expect(seen.length).toBe(afterFirst)
  })

  it("snapshot exposes the same state the reducer holds", () => {
    const ports = new RecordingPorts()
    const session = harnessSession()
    const initial = harnessStateWith([session], session.identity.id)
    const engine = new HarnessEngineAdapter({ initial, ports })

    expect(engine.snapshot).toBe(initial)
  })

  it("emptyHarnessEngine() starts with the empty harness state", async () => {
    const ports = new RecordingPorts()
    const engine = emptyHarnessEngine(ports)

    expect(engine.snapshot).toEqual(initialHarnessState())

    // Dispatching a close for a non-existent session is a no-op reducer
    // transition (unchanged) — no effects, no writes, no subs.
    await engine.dispatch({
      type: "session-closed",
      sessionId: harnessSessionId("missing"),
    })
    expect(ports.writes).toEqual([])
    expect(ports.setSubscriptionsCalls).toEqual([])
  })

  it("pending tabs flow through close without set-subscriptions picking them up", async () => {
    const ports = new RecordingPorts()
    const pending = harnessSession({
      identity: { kind: "pending", id: pendingSessionId("local-1") },
    })
    const remote = harnessSession({
      identity: { kind: "remote", id: harnessSessionId("remote-1") },
    })
    const engine = new HarnessEngineAdapter({
      initial: harnessStateWith([pending, remote]),
      ports,
    })

    await engine.dispatch({
      type: "session-closed",
      sessionId: pending.identity.id,
    })

    // Only remote ids are sent to the realtime port.
    expect(ports.setSubscriptionsCalls).toEqual([[remote.identity.id as SessionId]])
  })
})

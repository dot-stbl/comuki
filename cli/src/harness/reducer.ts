import type { HarnessEffect, PersistedHarnessSessions } from "./effects"
import type { HarnessEvent } from "./events"
import type {
  HarnessSession,
  HarnessState,
  SessionId,
  SessionKey,
  TurnRequestId,
} from "./state"

export interface HarnessTransition {
  readonly state: HarnessState
  readonly effects: readonly HarnessEffect[]
}

export function reduceHarness(
  state: HarnessState,
  event: HarnessEvent
): HarnessTransition {
  switch (event.type) {
    case "pending-session-opened": {
      const session: HarnessSession = {
        identity: { kind: "pending", id: event.pendingSessionId },
        projectId: event.projectId,
        title: "",
        createdAtUnixMs: event.createdAtUnixMs,
        renamed: false,
        unread: false,
        turn: { kind: "idle" },
        transcriptLoad: { kind: "not-loaded" },
        transcript: [],
        queue: [],
      }
      return withPersistence({
        ...state,
        sessions: [...state.sessions, session],
        activeSessionId: event.pendingSessionId,
      })
    }
    case "remote-session-adopted": {
      const adopted = state.sessions.find(
        (session) => sessionKey(session) === event.pendingSessionId
      )
      const nextState: HarnessState = {
        ...state,
        sessions: state.sessions.map((session) =>
          sessionKey(session) === event.pendingSessionId
            ? {
                ...session,
                identity: { kind: "remote", id: event.sessionId },
                projectId: event.projectId,
                title: event.title,
              }
            : session
        ),
        activeSessionId:
          state.activeSessionId === event.pendingSessionId
            ? event.sessionId
            : state.activeSessionId,
      }
      const queued = adopted?.queue[0]
      return {
        state: nextState,
        effects: [
          persistEffect(nextState),
          subscriptionsEffect(nextState),
          ...(queued
            ? [submitEffect(event.sessionId, queued.requestId, queued.message)]
            : []),
        ],
      }
    }
    case "remote-session-create-failed":
      return updateSession(state, event.pendingSessionId, (session) => ({
        ...session,
        turn: event.requestId
          ? { kind: "failed", requestId: event.requestId, error: event.error }
          : session.turn,
      }))
    case "session-focused":
      return withPersistence({
        ...state,
        sessions: state.sessions.map((session) =>
          sessionKey(session) === event.sessionId
            ? { ...session, unread: false }
            : session
        ),
        activeSessionId: event.sessionId,
      })
    case "session-closed": {
      const closedIndex = state.sessions.findIndex(
        (session) => sessionKey(session) === event.sessionId
      )
      if (closedIndex < 0) {
        return unchanged(state)
      }
      const sessions = state.sessions.filter(
        (session) => sessionKey(session) !== event.sessionId
      )
      const activeSessionId =
        state.activeSessionId === event.sessionId
          ? sessionKey(sessions[Math.min(closedIndex, sessions.length - 1)]) ?? null
          : state.activeSessionId
      const nextState = { ...state, sessions, activeSessionId }
      return {
        state: nextState,
        effects: [persistEffect(nextState), subscriptionsEffect(nextState)],
      }
    }
    case "session-renamed":
      return withPersistence(
        mapSession(state, event.sessionId, (session) => ({
          ...session,
          title: event.title,
          renamed: true,
        }))
      )
    case "session-output-received":
      return updateSession(state, event.sessionId, (session) => ({
        ...session,
        unread: state.activeSessionId !== event.sessionId,
      }))
    case "turn-queued": {
      const session = findSession(state, event.sessionId)
      if (!session) {
        return unchanged(state)
      }
      const nextState = mapSession(state, event.sessionId, (current) => ({
        ...current,
        queue: [
          ...current.queue,
          { requestId: event.requestId, message: event.message },
        ],
      }))
      if (session.queue.length > 0 || session.turn.kind !== "idle") {
        return { state: nextState, effects: [] }
      }
      if (session.identity.kind === "pending") {
        return {
          state: nextState,
          effects: [
            {
              type: "create-remote-session",
              pendingSessionId: session.identity.id,
              projectId: session.projectId,
              title: session.title,
              requestId: event.requestId,
            },
          ],
        }
      }
      return {
        state: nextState,
        effects: [
          submitEffect(session.identity.id, event.requestId, event.message),
        ],
      }
    }
    case "turn-dequeued":
      return updateSession(state, event.sessionId, (session) => ({
        ...session,
        queue: session.queue.filter(
          (turn) => turn.requestId !== event.requestId
        ),
      }))
    case "thinking-started":
      return updateSession(state, event.sessionId, (session) => ({
        ...session,
        turn: {
          kind: "thinking",
          requestId: event.requestId,
          accumulatedText: "",
        },
        queue: session.queue.filter(
          (turn) => turn.requestId !== event.requestId
        ),
      }))
    case "thinking-chunk-received":
      return updateSession(state, event.sessionId, (session) => ({
        ...session,
        turn:
          session.turn.kind === "thinking" &&
          session.turn.requestId === event.requestId
            ? {
                ...session.turn,
                accumulatedText: session.turn.accumulatedText + event.text,
              }
            : session.turn,
      }))
    case "turn-completed":
      return completeTurn(
        state,
        event.sessionId,
        event.requestId,
        event.messages,
        event.awaitingApproval
      )
    case "turn-failed":
      return updateSession(state, event.sessionId, (session) => ({
        ...session,
        turn: {
          kind: "failed",
          requestId: event.requestId,
          error: event.error,
        },
        unread: state.activeSessionId !== event.sessionId,
      }))
    case "approval-resolved":
      return continueQueue(
        mapSession(state, event.sessionId, (session) => ({
          ...session,
          turn:
            session.turn.kind === "awaiting-approval" &&
            session.turn.requestId === event.requestId
              ? { kind: "idle" }
              : session.turn,
        })),
        event.sessionId
      )
    case "transcript-load-started":
      return updateSession(state, event.sessionId, (session) => ({
        ...session,
        transcriptLoad: { kind: "loading" },
      }))
    case "transcript-loaded":
      return updateSession(state, event.sessionId, (session) => ({
        ...session,
        transcriptLoad: { kind: "loaded" },
        transcript: event.messages,
      }))
    case "transcript-load-failed":
      return updateSession(state, event.sessionId, (session) => ({
        ...session,
        transcriptLoad: { kind: "failed", error: event.error },
      }))
    case "sessions-persisted":
    case "sessions-persist-failed":
    case "subscriptions-set":
    case "subscriptions-set-failed":
      return unchanged(state)
    case "connection-started":
      return stateOnly({ ...state, connection: { kind: "connecting" } })
    case "connection-established":
      return stateOnly({ ...state, connection: { kind: "connected" } })
    case "connection-lost":
      return stateOnly({
        ...state,
        connection: { kind: "reconnecting", attempt: event.attempt },
      })
    case "connection-stopped":
      return stateOnly({ ...state, connection: { kind: "disconnected" } })
    case "auth-check-started":
      return stateOnly({ ...state, auth: { kind: "checking" } })
    case "auth-succeeded":
      return stateOnly({
        ...state,
        auth: { kind: "authenticated", subjectId: event.subjectId },
      })
    case "auth-required":
      return stateOnly({ ...state, auth: { kind: "unauthenticated" } })
    case "auth-failed":
      return stateOnly({
        ...state,
        auth: { kind: "failed", error: event.error },
      })
    case "overlay-closed":
      return stateOnly({ ...state, overlay: { kind: "closed" } })
    case "session-switcher-opened":
      return stateOnly({
        ...state,
        overlay: { kind: "session-switcher", query: event.query },
      })
    case "command-palette-opened":
      return stateOnly({
        ...state,
        overlay: { kind: "command-palette", query: event.query },
      })
    case "workbench-opened":
      return stateOnly({
        ...state,
        overlay: { kind: "workbench", sessionId: event.sessionId },
      })
    default:
      return assertNever(event)
  }
}

export function assertNever(value: never): never {
  throw new Error(`Unhandled harness event: ${JSON.stringify(value)}`)
}

function completeTurn(
  state: HarnessState,
  sessionId: SessionId,
  requestId: TurnRequestId,
  messages: HarnessSession["transcript"],
  awaitingApproval: boolean
): HarnessTransition {
  const nextState = mapSession(state, sessionId, (session) => ({
    ...session,
    turn: awaitingApproval
      ? { kind: "awaiting-approval", requestId }
      : { kind: "idle" },
    transcript: [...session.transcript, ...messages],
    unread: state.activeSessionId !== sessionId,
  }))
  return awaitingApproval
    ? { state: nextState, effects: [] }
    : continueQueue(nextState, sessionId)
}

function continueQueue(
  state: HarnessState,
  sessionId: SessionId
): HarnessTransition {
  const queued = findSession(state, sessionId)?.queue[0]
  return {
    state,
    effects: queued
      ? [submitEffect(sessionId, queued.requestId, queued.message)]
      : [],
  }
}

function updateSession(
  state: HarnessState,
  id: SessionKey,
  update: (session: HarnessSession) => HarnessSession
): HarnessTransition {
  return stateOnly(mapSession(state, id, update))
}

function mapSession(
  state: HarnessState,
  id: SessionKey,
  update: (session: HarnessSession) => HarnessSession
): HarnessState {
  return {
    ...state,
    sessions: state.sessions.map((session) =>
      sessionKey(session) === id ? update(session) : session
    ),
  }
}

function findSession(
  state: HarnessState,
  id: SessionKey
): HarnessSession | undefined {
  return state.sessions.find((session) => sessionKey(session) === id)
}

function sessionKey(session: HarnessSession | undefined): SessionKey | undefined {
  return session?.identity.id
}

function withPersistence(state: HarnessState): HarnessTransition {
  return { state, effects: [persistEffect(state)] }
}

function persistEffect(state: HarnessState): HarnessEffect {
  const value: PersistedHarnessSessions = {
    activeSessionId: state.activeSessionId,
    sessions: state.sessions,
  }
  return { type: "persist-sessions", value }
}

function subscriptionsEffect(state: HarnessState): HarnessEffect {
  return {
    type: "set-subscriptions",
    sessionIds: state.sessions.flatMap((session) =>
      session.identity.kind === "remote" ? [session.identity.id] : []
    ),
  }
}

function submitEffect(
  sessionId: SessionId,
  requestId: TurnRequestId,
  message: string
): HarnessEffect {
  return { type: "submit-turn", sessionId, requestId, message }
}

function stateOnly(state: HarnessState): HarnessTransition {
  return { state, effects: [] }
}

function unchanged(state: HarnessState): HarnessTransition {
  return { state, effects: [] }
}

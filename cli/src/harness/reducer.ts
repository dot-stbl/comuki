import type { HarnessEffect } from "./effects"
import type { HarnessEvent } from "./events"
import type {
  HarnessSession,
  HarnessState,
  SessionId,
  SessionKey,
  TrackedCommand,
  TurnRequestId,
} from "./state"
import { workspaceDocumentFromState } from "./workspace"

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
        history: [],
        lastUserMessage: null,
        pendingPlan: null,
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
        // The pending id dies at adoption — tracked commands and
        // drafts follow the session to its remote id (one-directional).
        drafts: state.drafts.map((draft) =>
          draft.sessionId === event.pendingSessionId
            ? { ...draft, sessionId: event.sessionId }
            : draft
        ),
        outbound: state.outbound.map((command) =>
          command.sessionId === event.pendingSessionId
            ? { ...command, sessionId: event.sessionId }
            : command
        ),
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
      const nextState = {
        ...state,
        sessions,
        activeSessionId,
        drafts: state.drafts.filter((draft) => draft.sessionId !== event.sessionId),
      }
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
      const echoText = event.echoText
      const historyText = event.historyText ?? event.echoText
      // Auto-title from the first message — a manual rename wins.
      const title =
        session.identity.kind === "pending" &&
        !session.renamed &&
        session.title === "" &&
        event.titleHint
          ? event.titleHint
          : session.title
      const queuedMessage: HarnessSession["queue"][number] = {
        requestId: event.requestId,
        message: event.message,
      }
      const updatedSessions = state.sessions.map((candidate) =>
        sessionKey(candidate) === event.sessionId
          ? {
              ...candidate,
              title,
              history:
                historyText !== undefined && historyText.length > 0
                  ? [...(candidate.history ?? []), historyText]
                  : candidate.history,
              lastUserMessage:
                echoText !== undefined && echoText.length > 0
                  ? echoText
                  : candidate.lastUserMessage,
              transcript:
                echoText !== undefined && echoText.length > 0
                  ? [
                      ...candidate.transcript,
                      {
                        id: `echo-${event.requestId}`,
                        role: "user" as const,
                        content: echoText,
                        createdAtUnixMs: 0,
                      },
                    ]
                  : candidate.transcript,
              queue: [...candidate.queue, queuedMessage],
            }
          : candidate
      )
      const nextState: HarnessState = {
        ...state,
        sessions: updatedSessions,
        outbound: trackCommand(state.outbound, {
          commandId: event.commandId,
          requestId: event.requestId,
          sessionId: event.sessionId,
          kind: "turn",
          message: event.message,
          state: "queued",
        }),
      }
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
              title,
              requestId: event.requestId,
            },
          ],
        }
      }
      return {
        state: nextState,
        effects: [
          submitEffect(session.identity.id, event.requestId, event.message, event.commandId),
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
    case "turn-completed": {
      const session = findSession(state, event.sessionId)
      // Stale-event rejection: a completion for a request that is not
      // this session's in-flight turn must not clobber state or
      // duplicate transcript entries.
      if (
        session?.turn.kind === "thinking" &&
        session.turn.requestId !== event.requestId
      ) {
        return unchanged(state)
      }
      return completeTurn(
        markCommandSettled(state, event.sessionId, event.requestId),
        event.sessionId,
        event.requestId,
        event.messages,
        event.awaitingApproval,
        event.pendingPlan
      )
    }
    case "turn-failed": {
      const session = findSession(state, event.sessionId)
      if (
        session?.turn.kind === "thinking" &&
        session.turn.requestId !== event.requestId
      ) {
        return unchanged(state)
      }
      return updateSession(
        markCommandSettled(state, event.sessionId, event.requestId),
        event.sessionId,
        (current) => ({
          ...current,
          turn: {
            kind: "failed",
            requestId: event.requestId,
            error: event.error,
          },
          unread: state.activeSessionId !== event.sessionId,
        })
      )
    }
    case "turn-cancel-requested":
      return {
        state: mapSession(state, event.sessionId, (session) => ({
          ...session,
          // The client stops listening; the server keeps thinking.
          turn:
            session.turn.kind === "thinking" &&
            session.turn.requestId === event.requestId
              ? { kind: "idle" as const }
              : session.turn,
        })),
        effects: [
          {
            type: "cancel-turn",
            sessionId: event.sessionId,
            requestId: event.requestId,
            commandId: event.commandId,
          },
        ],
      }
    case "approval-decision-sent": {
      const session = findSession(state, event.sessionId)
      // Guarded by awaiting-approval, not by request id: the decision
      // is a new wire operation with its own request id, deciding the
      // turn that is currently awaiting.
      if (!session || session.turn.kind !== "awaiting-approval") {
        return unchanged(state)
      }
      return {
        state: mapSession(
          {
            ...state,
            outbound: trackCommand(state.outbound, {
              commandId: event.commandId,
              requestId: event.requestId,
              sessionId: event.sessionId,
              kind: "approval",
              message: event.approved ? "approve" : "reject",
              state: "in-flight",
            }),
          },
          event.sessionId,
          (current) => ({
            ...current,
            turn: {
              kind: "thinking",
              requestId: event.requestId,
              accumulatedText: "",
            },
          })
        ),
        effects: [
          {
            type: "decide-approval",
            sessionId: event.sessionId,
            requestId: event.requestId,
            approved: event.approved,
            reason: event.reason,
            commandId: event.commandId,
          },
        ],
      }
    }
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
    case "draft-saved": {
      const rest = state.drafts.filter(
        (draft) => draft.sessionId !== event.sessionId
      )
      return stateOnly({
        ...state,
        drafts: [
          ...rest,
          {
            sessionId: event.sessionId,
            text: event.text,
            updatedAtUnixMs: event.savedAtUnixMs,
          },
        ],
      })
    }
    case "draft-cleared":
      return stateOnly({
        ...state,
        drafts: state.drafts.filter(
          (draft) => draft.sessionId !== event.sessionId
        ),
      })
    case "cursor-advanced": {
      const current = state.cursors[event.sessionId] ?? 0
      if (event.lastSeenAtUnixMs <= current) {
        return unchanged(state)
      }
      return stateOnly({
        ...state,
        cursors: { ...state.cursors, [event.sessionId]: event.lastSeenAtUnixMs },
      })
    }
    case "sessions-persisted":
    case "sessions-persist-failed":
    case "subscriptions-set":
    case "subscriptions-set-failed":
      return unchanged(state)
    case "connection-started":
      return stateOnly({ ...state, connection: { kind: "connecting" } })
    case "connection-established":
      // Reconnect reconciliation: a reconnect gets a fresh connection
      // id and loses every chat group — re-subscribe to the open set.
      return {
        state: { ...state, connection: { kind: "connected" } },
        effects: [subscriptionsEffect(state)],
      }
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
  awaitingApproval: boolean,
  pendingPlan?: unknown
): HarnessTransition {
  const nextState = mapSession(state, sessionId, (session) => ({
    ...session,
    turn: awaitingApproval
      ? { kind: "awaiting-approval", requestId }
      : { kind: "idle" },
    transcript: [...session.transcript, ...messages],
    unread: state.activeSessionId !== sessionId,
    pendingPlan: awaitingApproval ? (pendingPlan ?? null) : null,
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

function trackCommand(
  outbound: readonly TrackedCommand[],
  command: {
    commandId?: string
    requestId: TurnRequestId
    sessionId: SessionKey
    kind: "turn" | "approval"
    message: string
    state: "queued" | "in-flight" | "settled"
  }
): readonly TrackedCommand[] {
  if (!command.commandId) {
    return outbound
  }
  if (outbound.some((existing) => existing.commandId === command.commandId)) {
    return outbound
  }
  return [
    ...outbound,
    {
      commandId: command.commandId,
      requestId: command.requestId,
      sessionId: command.sessionId,
      kind: command.kind,
      message: command.message,
      state: command.state,
    },
  ]
}

function markCommandSettled(
  state: HarnessState,
  sessionId: SessionKey,
  requestId: TurnRequestId
): HarnessState {
  const tracked = state.outbound.some(
    (command) =>
      command.sessionId === sessionId && command.requestId === requestId
  )
  if (!tracked) {
    return state
  }
  return {
    ...state,
    outbound: state.outbound.map((command) =>
      command.sessionId === sessionId && command.requestId === requestId
        ? { ...command, state: "settled" as const }
        : command
    ),
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
  return { type: "persist-sessions", value: workspaceDocumentFromState(state) }
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
  message: string,
  commandId?: string
): HarnessEffect {
  return { type: "submit-turn", sessionId, requestId, message, commandId }
}

function stateOnly(state: HarnessState): HarnessTransition {
  return { state, effects: [] }
}

function unchanged(state: HarnessState): HarnessTransition {
  return { state, effects: [] }
}

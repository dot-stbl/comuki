import { ComukiApiError, isAbortError } from "../lib/client"
import type { HarnessEffect, PersistedHarnessSessions } from "./effects"
import type { HarnessEvent } from "./events"
import type {
  CliError,
  HarnessMessage,
  PendingSessionId,
  ProjectId,
  SessionId,
  TurnRequestId,
} from "./state"

export interface CreatedSession {
  readonly sessionId: SessionId
  readonly projectId: ProjectId | null
  readonly title: string
}

export interface SubmittedTurn {
  readonly messages: readonly HarnessMessage[]
  readonly awaitingApproval: boolean
}

export interface ComukiApi {
  createSession(
    request: {
      readonly projectId: ProjectId | null
      readonly title: string
    },
    signal: AbortSignal
  ): Promise<CreatedSession>
  submitTurn(
    sessionId: SessionId,
    message: string,
    signal: AbortSignal
  ): Promise<SubmittedTurn>
  cancelTurn(
    sessionId: SessionId,
    requestId: TurnRequestId,
    signal: AbortSignal
  ): Promise<void>
  loadTranscript(
    sessionId: SessionId,
    signal: AbortSignal
  ): Promise<readonly HarnessMessage[]>
}

export interface AuthSession {
  status(signal: AbortSignal): Promise<"authenticated" | "unauthenticated">
}

export interface RealtimeConnection {
  setSubscriptions(
    sessionIds: readonly SessionId[],
    signal: AbortSignal
  ): Promise<void>
  reconnect(signal: AbortSignal): Promise<void>
}

export interface SessionStore {
  write(value: PersistedHarnessSessions, signal: AbortSignal): Promise<void>
}

export interface TerminalAdapter {
  setTitle(title: string, signal: AbortSignal): Promise<void>
}

export interface HarnessEffectPorts {
  readonly api: ComukiApi
  readonly auth: AuthSession
  readonly realtime: RealtimeConnection
  readonly sessions: SessionStore
  readonly terminal: TerminalAdapter
}

export type RunnableHarnessEffect = Extract<
  HarnessEffect,
  {
    readonly type:
      | "persist-sessions"
      | "create-remote-session"
      | "submit-turn"
      | "set-subscriptions"
  }
>

export type HarnessDispatch = (event: HarnessEvent) => void

export async function runEffect(
  effect: RunnableHarnessEffect,
  ports: HarnessEffectPorts,
  dispatch: HarnessDispatch,
  signal: AbortSignal
): Promise<void> {
  switch (effect.type) {
    case "persist-sessions":
      await runPersistSessions(effect.value, ports, dispatch, signal)
      return
    case "create-remote-session":
      await runCreateRemoteSession(effect, ports, dispatch, signal)
      return
    case "submit-turn":
      await runSubmitTurn(effect, ports, dispatch, signal)
      return
    case "set-subscriptions":
      await runSetSubscriptions(effect.sessionIds, ports, dispatch, signal)
      return
    default:
      return assertNever(effect)
  }
}

export function normalizeCliError(error: unknown): CliError {
  if (isAbortError(error)) {
    return {
      kind: "aborted",
      code: "operation.aborted",
      message: "Operation was cancelled",
      retryable: false,
    }
  }
  if (error instanceof ComukiApiError) {
    return {
      kind:
        error.status === 401 || error.status === 403 ? "auth" : "server",
      code: error.code ?? `http.${error.status}`,
      message: error.detail ?? "Request failed",
      retryable: error.status === 408 || error.status === 429 || error.status >= 500,
    }
  }
  if (error instanceof Error) {
    return {
      kind: "unknown",
      code: "cli.unexpected",
      message: error.message,
      retryable: false,
    }
  }
  return {
    kind: "unknown",
    code: "cli.unexpected",
    message: "Unexpected failure",
    retryable: false,
  }
}

async function runPersistSessions(
  value: PersistedHarnessSessions,
  ports: HarnessEffectPorts,
  dispatch: HarnessDispatch,
  signal: AbortSignal
): Promise<void> {
  try {
    await ports.sessions.write(value, signal)
    dispatch({ type: "sessions-persisted" })
  } catch (error: unknown) {
    dispatch({
      type: "sessions-persist-failed",
      error: storageError(error),
    })
  }
}

async function runCreateRemoteSession(
  effect: Extract<HarnessEffect, { readonly type: "create-remote-session" }>,
  ports: HarnessEffectPorts,
  dispatch: HarnessDispatch,
  signal: AbortSignal
): Promise<void> {
  try {
    const created = await ports.api.createSession(
      { projectId: effect.projectId, title: effect.title },
      signal
    )
    dispatch({
      type: "remote-session-adopted",
      pendingSessionId: effect.pendingSessionId,
      sessionId: created.sessionId,
      projectId: created.projectId,
      title: created.title,
    })
  } catch (error: unknown) {
    dispatch(createSessionFailed(effect.pendingSessionId, effect.requestId, error))
  }
}

async function runSubmitTurn(
  effect: Extract<HarnessEffect, { readonly type: "submit-turn" }>,
  ports: HarnessEffectPorts,
  dispatch: HarnessDispatch,
  signal: AbortSignal
): Promise<void> {
  dispatch({
    type: "thinking-started",
    sessionId: effect.sessionId,
    requestId: effect.requestId,
  })
  try {
    const result = await ports.api.submitTurn(
      effect.sessionId,
      effect.message,
      signal
    )
    dispatch({
      type: "turn-completed",
      sessionId: effect.sessionId,
      requestId: effect.requestId,
      messages: result.messages,
      awaitingApproval: result.awaitingApproval,
    })
  } catch (error: unknown) {
    dispatch({
      type: "turn-failed",
      sessionId: effect.sessionId,
      requestId: effect.requestId,
      error: normalizeCliError(error),
    })
  }
}

async function runSetSubscriptions(
  sessionIds: readonly SessionId[],
  ports: HarnessEffectPorts,
  dispatch: HarnessDispatch,
  signal: AbortSignal
): Promise<void> {
  try {
    await ports.realtime.setSubscriptions(sessionIds, signal)
    dispatch({ type: "subscriptions-set", sessionIds })
  } catch (error: unknown) {
    dispatch({
      type: "subscriptions-set-failed",
      error: normalizeCliError(error),
    })
  }
}

function createSessionFailed(
  pendingSessionId: PendingSessionId,
  requestId: TurnRequestId | null,
  error: unknown
): HarnessEvent {
  return {
    type: "remote-session-create-failed",
    pendingSessionId,
    requestId,
    error: normalizeCliError(error),
  }
}

function storageError(error: unknown): CliError {
  const normalized = normalizeCliError(error)
  return {
    ...normalized,
    kind: normalized.kind === "aborted" ? "aborted" : "storage",
    code:
      normalized.kind === "aborted" ? normalized.code : "storage.write_failed",
  }
}

function assertNever(value: never): never {
  throw new Error(`Unhandled runnable harness effect: ${JSON.stringify(value)}`)
}

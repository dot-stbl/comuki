/**
 * Effect interpreter: turns harness effects into port calls and
 * dispatches the resulting events. The ports are the kernel's only
 * view of the outside world (issue #83):
 *
 * - `ConversationPort` — open/submit/cancel/load a server conversation.
 * - `ApprovalPort` — decide a pending approval (online-only, never queued).
 * - `RealtimePort` — (re)subscribe the live feed to the open sessions.
 * - `WorkspaceStore` — durable workspace document (versioned, migrated).
 *
 * Adapters implement the ports over HTTP/SignalR; tests use fakes. No
 * renderer concerns live here.
 */
import { ComukiApiError, isAbortError } from "../lib/client"
import type { HarnessEffect } from "./effects"
import type { HarnessEvent } from "./events"
import type {
  CliError,
  HarnessMessage,
  PendingSessionId,
  ProjectId,
  SessionId,
  TurnRequestId,
} from "./state"
import type { WorkspaceDocument } from "./workspace"

export interface OpenedConversation {
  readonly sessionId: SessionId
  readonly projectId: ProjectId | null
  readonly title: string
}

export interface TurnOutcome {
  readonly messages: readonly HarnessMessage[]
  readonly awaitingApproval: boolean
  readonly pendingPlan?: unknown
}

export interface ConversationPort {
  /** Creates the server session a pending tab adopts on its first message. */
  openConversation(
    request: {
      readonly projectId: ProjectId | null
      readonly title: string
    },
    signal: AbortSignal
  ): Promise<OpenedConversation>
  /**
   * Submits one turn. `commandId` is the stable client command id for
   * idempotency — adapters may surface it to the server (header) or
   * key their own in-flight bookkeeping on it.
   */
  submitTurn(
    sessionId: SessionId,
    message: string,
    commandId: string | undefined,
    signal: AbortSignal
  ): Promise<TurnOutcome>
  /** Client-side abort of an in-flight turn; the server keeps thinking. */
  cancelTurn(
    sessionId: SessionId,
    commandId: string | undefined,
    signal: AbortSignal
  ): Promise<void>
  /** Loads a session's transcript (adapters hide the paging). */
  loadConversation(
    sessionId: SessionId,
    signal: AbortSignal
  ): Promise<readonly HarnessMessage[]>
}

export interface ApprovalPort {
  /** Decides the pending approval — requires a live connection by contract. */
  decide(
    sessionId: SessionId,
    approved: boolean,
    reason: string | undefined,
    commandId: string | undefined,
    signal: AbortSignal
  ): Promise<TurnOutcome>
}

export interface RealtimePort {
  /** (Re)points the live feed's chat groups at exactly these sessions. */
  setSubscriptions(
    sessionIds: readonly SessionId[],
    signal: AbortSignal
  ): Promise<void>
}

export interface WorkspaceStore {
  read(): Promise<unknown>
  write(document: WorkspaceDocument, signal: AbortSignal): Promise<void>
}

export interface HarnessEffectPorts {
  readonly conversation: ConversationPort
  readonly approval: ApprovalPort
  readonly realtime: RealtimePort
  readonly workspace: WorkspaceStore
}

export type HarnessDispatch = (event: HarnessEvent) => void

export async function runEffect(
  effect: HarnessEffect,
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
    case "cancel-turn":
      await runCancelTurn(effect, ports, signal)
      return
    case "decide-approval":
      await runDecideApproval(effect, ports, dispatch, signal)
      return
    case "load-transcript":
      await runLoadTranscript(effect, ports, dispatch, signal)
      return
    case "set-subscriptions":
      await runSetSubscriptions(effect.sessionIds, ports, dispatch, signal)
      return
    case "reconnect":
      // Reconnection is owned by the feed adapter's own retry ramp.
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
  value: WorkspaceDocument,
  ports: HarnessEffectPorts,
  dispatch: HarnessDispatch,
  signal: AbortSignal
): Promise<void> {
  try {
    await ports.workspace.write(value, signal)
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
    const created = await ports.conversation.openConversation(
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
    const result = await ports.conversation.submitTurn(
      effect.sessionId,
      effect.message,
      effect.commandId,
      signal
    )
    dispatch({
      type: "turn-completed",
      sessionId: effect.sessionId,
      requestId: effect.requestId,
      messages: result.messages,
      awaitingApproval: result.awaitingApproval,
      pendingPlan: result.pendingPlan,
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

async function runCancelTurn(
  effect: Extract<HarnessEffect, { readonly type: "cancel-turn" }>,
  ports: HarnessEffectPorts,
  signal: AbortSignal
): Promise<void> {
  // The reducer already flipped the turn off thinking; the abort (and
  // the resulting turn-failed(aborted)) is best-effort — nothing to
  // dispatch here either way.
  try {
    await ports.conversation.cancelTurn(
      effect.sessionId,
      effect.commandId,
      signal
    )
  } catch {
    // The in-flight submit may already be gone — that is the goal.
  }
}

async function runDecideApproval(
  effect: Extract<HarnessEffect, { readonly type: "decide-approval" }>,
  ports: HarnessEffectPorts,
  dispatch: HarnessDispatch,
  signal: AbortSignal
): Promise<void> {
  try {
    const result = await ports.approval.decide(
      effect.sessionId,
      effect.approved,
      effect.reason,
      effect.commandId,
      signal
    )
    dispatch({
      type: "turn-completed",
      sessionId: effect.sessionId,
      requestId: effect.requestId,
      messages: result.messages,
      awaitingApproval: result.awaitingApproval,
      pendingPlan: result.pendingPlan,
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

async function runLoadTranscript(
  effect: Extract<HarnessEffect, { readonly type: "load-transcript" }>,
  ports: HarnessEffectPorts,
  dispatch: HarnessDispatch,
  signal: AbortSignal
): Promise<void> {
  dispatch({ type: "transcript-load-started", sessionId: effect.sessionId })
  try {
    const messages = await ports.conversation.loadConversation(
      effect.sessionId,
      signal
    )
    dispatch({
      type: "transcript-loaded",
      sessionId: effect.sessionId,
      messages,
    })
  } catch (error: unknown) {
    dispatch({
      type: "transcript-load-failed",
      sessionId: effect.sessionId,
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
  throw new Error(`Unhandled harness effect: ${JSON.stringify(value)}`)
}

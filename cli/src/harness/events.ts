import type {
  CliError,
  HarnessMessage,
  PendingSessionId,
  ProjectId,
  SessionId,
  SessionKey,
  TurnRequestId,
} from "./state"

export type HarnessEvent =
  | {
      readonly type: "pending-session-opened"
      readonly pendingSessionId: PendingSessionId
      readonly projectId: ProjectId | null
      readonly createdAtUnixMs: number
    }
  | {
      readonly type: "remote-session-adopted"
      readonly pendingSessionId: PendingSessionId
      readonly sessionId: SessionId
      readonly projectId: ProjectId | null
      readonly title: string
    }
  | {
      readonly type: "remote-session-create-failed"
      readonly pendingSessionId: PendingSessionId
      readonly requestId: TurnRequestId | null
      readonly error: CliError
    }
  | { readonly type: "session-focused"; readonly sessionId: SessionKey }
  | { readonly type: "session-closed"; readonly sessionId: SessionKey }
  | {
      readonly type: "session-renamed"
      readonly sessionId: SessionKey
      readonly title: string
    }
  | { readonly type: "session-output-received"; readonly sessionId: SessionId }
  | {
      readonly type: "turn-queued"
      readonly sessionId: SessionKey
      readonly requestId: TurnRequestId
      readonly message: string
    }
  | {
      readonly type: "turn-dequeued"
      readonly sessionId: SessionKey
      readonly requestId: TurnRequestId
    }
  | {
      readonly type: "thinking-started"
      readonly sessionId: SessionId
      readonly requestId: TurnRequestId
    }
  | {
      readonly type: "thinking-chunk-received"
      readonly sessionId: SessionId
      readonly requestId: TurnRequestId
      readonly text: string
    }
  | {
      readonly type: "turn-completed"
      readonly sessionId: SessionId
      readonly requestId: TurnRequestId
      readonly messages: readonly HarnessMessage[]
      readonly awaitingApproval: boolean
    }
  | {
      readonly type: "turn-failed"
      readonly sessionId: SessionId
      readonly requestId: TurnRequestId
      readonly error: CliError
    }
  | {
      readonly type: "approval-resolved"
      readonly sessionId: SessionId
      readonly requestId: TurnRequestId
    }
  | { readonly type: "transcript-load-started"; readonly sessionId: SessionId }
  | {
      readonly type: "transcript-loaded"
      readonly sessionId: SessionId
      readonly messages: readonly HarnessMessage[]
    }
  | {
      readonly type: "transcript-load-failed"
      readonly sessionId: SessionId
      readonly error: CliError
    }
  | { readonly type: "sessions-persisted" }
  | { readonly type: "sessions-persist-failed"; readonly error: CliError }
  | { readonly type: "subscriptions-set"; readonly sessionIds: readonly SessionId[] }
  | { readonly type: "subscriptions-set-failed"; readonly error: CliError }
  | { readonly type: "connection-started" }
  | { readonly type: "connection-established" }
  | { readonly type: "connection-lost"; readonly attempt: number }
  | { readonly type: "connection-stopped" }
  | { readonly type: "auth-check-started" }
  | { readonly type: "auth-succeeded"; readonly subjectId: string }
  | { readonly type: "auth-required" }
  | { readonly type: "auth-failed"; readonly error: CliError }
  | { readonly type: "overlay-closed" }
  | { readonly type: "session-switcher-opened"; readonly query: string }
  | { readonly type: "command-palette-opened"; readonly query: string }
  | { readonly type: "workbench-opened"; readonly sessionId: SessionKey }

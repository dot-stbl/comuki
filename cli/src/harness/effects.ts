import type {
  HarnessSession,
  PendingSessionId,
  ProjectId,
  SessionId,
  TurnRequestId,
} from "./state"
import type { WorkspaceDocument } from "./workspace"

/**
 * Legacy alias for the pre-workspace persistence payload. The
 * `persist-sessions` effect now carries the versioned
 * {@link WorkspaceDocument}; the alias stays for old call sites.
 */
export interface PersistedHarnessSessions {
  readonly activeSessionId: HarnessSession["identity"]["id"] | null
  readonly sessions: readonly HarnessSession[]
}

export type HarnessEffect =
  | {
      readonly type: "persist-sessions"
      readonly value: WorkspaceDocument
    }
  | {
      readonly type: "create-remote-session"
      readonly pendingSessionId: PendingSessionId
      readonly projectId: ProjectId | null
      readonly title: string
      readonly requestId: TurnRequestId | null
    }
  | {
      readonly type: "submit-turn"
      readonly sessionId: SessionId
      readonly requestId: TurnRequestId
      readonly message: string
      readonly commandId?: string
    }
  | {
      readonly type: "cancel-turn"
      readonly sessionId: SessionId
      readonly requestId: TurnRequestId
      readonly commandId?: string
    }
  | {
      readonly type: "decide-approval"
      readonly sessionId: SessionId
      readonly requestId: TurnRequestId
      readonly approved: boolean
      readonly reason?: string
      readonly commandId?: string
    }
  | { readonly type: "reconnect" }
  | {
      readonly type: "set-subscriptions"
      readonly sessionIds: readonly SessionId[]
    }
  | { readonly type: "load-transcript"; readonly sessionId: SessionId }

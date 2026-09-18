import type {
  HarnessSession,
  PendingSessionId,
  ProjectId,
  SessionId,
  SessionKey,
  TurnRequestId,
} from "./state"

export interface PersistedHarnessSessions {
  readonly activeSessionId: SessionKey | null
  readonly sessions: readonly HarnessSession[]
}

export type HarnessEffect =
  | {
      readonly type: "persist-sessions"
      readonly value: PersistedHarnessSessions
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
    }
  | {
      readonly type: "cancel-turn"
      readonly sessionId: SessionId
      readonly requestId: TurnRequestId
    }
  | { readonly type: "reconnect" }
  | {
      readonly type: "set-subscriptions"
      readonly sessionIds: readonly SessionId[]
    }
  | { readonly type: "load-transcript"; readonly sessionId: SessionId }

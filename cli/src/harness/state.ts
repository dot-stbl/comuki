declare const sessionIdBrand: unique symbol
declare const pendingSessionIdBrand: unique symbol
declare const turnRequestIdBrand: unique symbol
declare const projectIdBrand: unique symbol

export type SessionId = string & { readonly [sessionIdBrand]: true }
export type PendingSessionId = string & { readonly [pendingSessionIdBrand]: true }
export type TurnRequestId = string & { readonly [turnRequestIdBrand]: true }
export type ProjectId = string & { readonly [projectIdBrand]: true }

export function sessionId(value: string): SessionId {
  return value as SessionId
}

export function pendingSessionId(value: string): PendingSessionId {
  return value as PendingSessionId
}

export function turnRequestId(value: string): TurnRequestId {
  return value as TurnRequestId
}

export function projectId(value: string): ProjectId {
  return value as ProjectId
}

export type SessionKey = SessionId | PendingSessionId

export type SessionIdentity =
  | { readonly kind: "pending"; readonly id: PendingSessionId }
  | { readonly kind: "remote"; readonly id: SessionId }

export interface CliError {
  readonly kind: "aborted" | "auth" | "network" | "server" | "storage" | "unknown"
  readonly code: string
  readonly message: string
  readonly retryable: boolean
}

export type TurnState =
  | { readonly kind: "idle" }
  | {
      readonly kind: "thinking"
      readonly requestId: TurnRequestId
      readonly accumulatedText: string
    }
  | {
      readonly kind: "awaiting-approval"
      readonly requestId: TurnRequestId
    }
  | {
      readonly kind: "failed"
      readonly requestId: TurnRequestId
      readonly error: CliError
    }

export type TranscriptLoadState =
  | { readonly kind: "not-loaded" }
  | { readonly kind: "loading" }
  | { readonly kind: "loaded" }
  | { readonly kind: "failed"; readonly error: CliError }

export type ConnectionState =
  | { readonly kind: "disconnected" }
  | { readonly kind: "connecting" }
  | { readonly kind: "connected" }
  | { readonly kind: "reconnecting"; readonly attempt: number }

export type AuthState =
  | { readonly kind: "checking" }
  | { readonly kind: "authenticated"; readonly subjectId: string }
  | { readonly kind: "unauthenticated" }
  | { readonly kind: "failed"; readonly error: CliError }

export type OverlayState =
  | { readonly kind: "closed" }
  | { readonly kind: "session-switcher"; readonly query: string }
  | { readonly kind: "command-palette"; readonly query: string }
  | { readonly kind: "workbench"; readonly sessionId: SessionKey }

export interface HarnessMessage {
  readonly id: string
  readonly role: "user" | "assistant" | "system" | "tool"
  readonly content: string
  readonly createdAtUnixMs: number
}

export interface QueuedTurn {
  readonly requestId: TurnRequestId
  readonly message: string
}

export interface HarnessSession {
  readonly identity: SessionIdentity
  readonly projectId: ProjectId | null
  readonly title: string
  readonly createdAtUnixMs: number
  readonly renamed: boolean
  readonly unread: boolean
  readonly turn: TurnState
  readonly transcriptLoad: TranscriptLoadState
  readonly transcript: readonly HarnessMessage[]
  readonly queue: readonly QueuedTurn[]
}

export interface HarnessState {
  readonly sessions: readonly HarnessSession[]
  readonly activeSessionId: SessionKey | null
  readonly connection: ConnectionState
  readonly auth: AuthState
  readonly overlay: OverlayState
}

export function initialHarnessState(): HarnessState {
  return {
    sessions: [],
    activeSessionId: null,
    connection: { kind: "disconnected" },
    auth: { kind: "checking" },
    overlay: { kind: "closed" },
  }
}

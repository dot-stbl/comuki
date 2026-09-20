declare const sessionIdBrand: unique symbol
declare const pendingSessionIdBrand: unique symbol
declare const turnRequestIdBrand: unique symbol
declare const projectIdBrand: unique symbol

import type { ChatMessageView } from "../contracts/_generated/http/types/ChatMessageView"

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
  /**
   * The wire view the message was decoded from, when it came from the
   * server. Pure logic reads `content`; renderers that need parts /
   * meta read `view`. Absent on synthesized messages (user echoes).
   */
  readonly view?: ChatMessageView
}

export interface QueuedTurn {
  readonly requestId: TurnRequestId
  readonly message: string
}

/**
 * A recoverable per-session composer draft. Client-owned workspace
 * state: survives restarts via the versioned workspace document.
 */
export interface SessionDraft {
  readonly sessionId: SessionKey
  readonly text: string
  readonly updatedAtUnixMs: number
}

/** Durable stream cursor per remote session — `lastSeenAt` unix ms. */
export type WorkspaceCursors = Readonly<Record<string, number>>

/**
 * One explicitly-safe outbound command the workspace tracks for
 * idempotency. Only ever covers turns — approval decisions are
 * online-only and are never recorded for (offline) replay.
 */
export interface TrackedCommand {
  readonly commandId: string
  readonly requestId: TurnRequestId
  readonly sessionId: SessionKey
  readonly kind: "turn" | "approval"
  readonly message: string
  readonly state: "queued" | "in-flight" | "settled"
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
  /** Recall history for ↑/↓ prompt navigation, oldest first. */
  readonly history?: readonly string[]
  /** Last user-sent text — what `/retry` resends. */
  readonly lastUserMessage?: string | null
  /** Server plan awaiting approval, carried from the last turn result. */
  readonly pendingPlan?: unknown
}

export interface HarnessState {
  readonly sessions: readonly HarnessSession[]
  readonly activeSessionId: SessionKey | null
  readonly connection: ConnectionState
  readonly auth: AuthState
  readonly overlay: OverlayState
  /** Recoverable per-session composer drafts (workspace). */
  readonly drafts: readonly SessionDraft[]
  /** Durable stream cursors, per remote session id (workspace). */
  readonly cursors: WorkspaceCursors
  /** Explicitly-safe outbound commands tracked for idempotency. */
  readonly outbound: readonly TrackedCommand[]
}

export function initialHarnessState(): HarnessState {
  return {
    sessions: [],
    activeSessionId: null,
    connection: { kind: "disconnected" },
    auth: { kind: "checking" },
    overlay: { kind: "closed" },
    drafts: [],
    cursors: {},
    outbound: [],
  }
}

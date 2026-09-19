/**
 * UserIntent — the renderer's entire vocabulary for driving the
 * kernel. `translateIntent` is pure: state + intent + clock → events.
 * The kernel runs it on every dispatch and feeds the events through
 * the reducer, so intent handling is as replayable and testable as
 * the reducer itself.
 *
 * Idempotency: a `submit-turn` whose command id already exists in the
 * workspace outbound log translates to nothing — a repeated send
 * after an uncertain response resolves to the existing entry, never a
 * duplicate. Approval decisions are online-only by construction: they
 * translate only while a turn is actually awaiting approval and are
 * never queued for replay.
 */
import type { HarnessEvent } from "./events"
import type { HarnessState, ProjectId, SessionKey } from "./state"
import { pendingSessionId, turnRequestId } from "./state"

export type UserIntent =
  | { readonly kind: "open-session"; readonly projectId?: ProjectId | null }
  | { readonly kind: "focus-session"; readonly sessionId: SessionKey }
  | { readonly kind: "close-session"; readonly sessionId: SessionKey }
  | {
      readonly kind: "rename-session"
      readonly sessionId: SessionKey
      readonly title: string
    }
  | {
      readonly kind: "submit-turn"
      readonly sessionId: SessionKey
      readonly message: string
      readonly commandId: string
      readonly echoText?: string
      readonly historyText?: string
      readonly titleHint?: string
    }
  | { readonly kind: "cancel-turn"; readonly sessionId: SessionKey }
  | {
      readonly kind: "decide-approval"
      readonly sessionId: SessionKey
      readonly approved: boolean
      readonly reason?: string
      readonly commandId: string
    }
  | {
      readonly kind: "save-draft"
      readonly sessionId: SessionKey
      readonly text: string
    }
  | { readonly kind: "clear-draft"; readonly sessionId: SessionKey }

export function translateIntent(
  state: HarnessState,
  intent: UserIntent,
  nowUnixMs: number
): readonly HarnessEvent[] {
  switch (intent.kind) {
    case "open-session":
      return [
        {
          type: "pending-session-opened",
          pendingSessionId: pendingSessionId(
            `local-${nowUnixMs}-${state.sessions.length}`
          ),
          projectId: intent.projectId ?? null,
          createdAtUnixMs: nowUnixMs,
        },
      ]
    case "focus-session":
      return [{ type: "session-focused", sessionId: intent.sessionId }]
    case "close-session":
      return [{ type: "session-closed", sessionId: intent.sessionId }]
    case "rename-session": {
      const collapsed = intent.title.replace(/\s+/g, " ").trim()
      if (collapsed.length === 0) {
        return []
      }
      return [
        { type: "session-renamed", sessionId: intent.sessionId, title: collapsed },
      ]
    }
    case "submit-turn": {
      if (
        state.outbound.some(
          (command) => command.commandId === intent.commandId
        )
      ) {
        return []
      }
      if (
        !state.sessions.some(
          (session) => session.identity.id === intent.sessionId
        )
      ) {
        return []
      }
      return [
        {
          type: "turn-queued",
          sessionId: intent.sessionId,
          requestId: turnRequestId(intent.commandId),
          message: intent.message,
          commandId: intent.commandId,
          ...(intent.echoText !== undefined ? { echoText: intent.echoText } : {}),
          ...(intent.historyText !== undefined
            ? { historyText: intent.historyText }
            : {}),
          ...(intent.titleHint !== undefined ? { titleHint: intent.titleHint } : {}),
        },
      ]
    }
    case "cancel-turn": {
      const session = state.sessions.find(
        (candidate) => candidate.identity.id === intent.sessionId
      )
      // Only a remote session can have a turn in flight on the wire.
      if (!session || session.identity.kind !== "remote") {
        return []
      }
      if (session.turn.kind !== "thinking") {
        return []
      }
      const thinkingTurn = session.turn
      const command = state.outbound.find(
        (candidate) =>
          candidate.sessionId === intent.sessionId &&
          candidate.requestId === thinkingTurn.requestId
      )
      return [
        {
          type: "turn-cancel-requested",
          sessionId: session.identity.id,
          requestId: thinkingTurn.requestId,
          commandId: command?.commandId,
        },
      ]
    }
    case "decide-approval": {
      const session = state.sessions.find(
        (candidate) => candidate.identity.id === intent.sessionId
      )
      if (!session || session.identity.kind !== "remote") {
        return []
      }
      // Approvals are decided online or not at all — never queued. The
      // decision is its own wire operation with its own request id.
      if (session.turn.kind !== "awaiting-approval") {
        return []
      }
      return [
        {
          type: "approval-decision-sent",
          sessionId: session.identity.id,
          requestId: turnRequestId(`approval:${intent.commandId}`),
          approved: intent.approved,
          reason: intent.reason,
          commandId: intent.commandId,
        },
      ]
    }
    case "save-draft":
      return [
        {
          type: "draft-saved",
          sessionId: intent.sessionId,
          text: intent.text,
          savedAtUnixMs: nowUnixMs,
        },
      ]
    case "clear-draft":
      return [{ type: "draft-cleared", sessionId: intent.sessionId }]
    default:
      return []
  }
}

import type { ChatMessageView } from "../lib/client"
import { PENDING_PREFIX, type Session } from "../lib/session-state"
import {
  pendingSessionId,
  sessionId,
  turnRequestId,
  type HarnessMessage,
  type HarnessSession,
  type TurnRequestId,
} from "./state"

export function harnessSessionFromLegacy(session: Session): HarnessSession {
  const requestId = turnRequestId(`legacy:${session.id}`)
  return {
    identity: session.id.startsWith(PENDING_PREFIX)
      ? { kind: "pending", id: pendingSessionId(session.id) }
      : { kind: "remote", id: sessionId(session.id) },
    projectId: null,
    title: session.name,
    createdAtUnixMs: session.createdAt,
    renamed: session.renamed,
    unread: session.unread,
    turn: legacyTurnState(session, requestId),
    transcriptLoad: session.hydrated
      ? { kind: "loaded" }
      : { kind: "not-loaded" },
    transcript: session.blocks.flatMap((block) =>
      block.kind === "message" ? [harnessMessageFromLegacy(block.message)] : []
    ),
    queue: (session.queued ?? []).map((message, index) => ({
      requestId: turnRequestId(`legacy:${session.id}:queued:${index}`),
      message,
    })),
  }
}

function legacyTurnState(
  session: Session,
  requestId: TurnRequestId
): HarnessSession["turn"] {
  if (session.awaitingApproval) {
    return { kind: "awaiting-approval", requestId }
  }
  if (session.status === "thinking" || session.status === "running") {
    return {
      kind: "thinking",
      requestId,
      accumulatedText: session.liveText,
    }
  }
  return { kind: "idle" }
}

function harnessMessageFromLegacy(message: ChatMessageView): HarnessMessage {
  return {
    id: message.id,
    role: legacyRole(message.role),
    content: message.content,
    createdAtUnixMs: Date.parse(message.createdAt),
  }
}

function legacyRole(role: string): HarnessMessage["role"] {
  switch (role) {
    case "user":
    case "assistant":
    case "system":
    case "tool":
      return role
    default:
      return "system"
  }
}

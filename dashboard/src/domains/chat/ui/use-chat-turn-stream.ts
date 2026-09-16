import { useEffect, useSyncExternalStore } from "react"
import type { HubConnection } from "@microsoft/signalr"

import { chatMessagesQueryKey } from "@/domains/chat/api/queries"
import {
  applyChatChunk,
  chatTurnStreamStore,
  completeChatTurn,
  pendingUserMessageOf,
  streamingReplyMessageOf,
  type ChatTurnStream,
} from "@/domains/chat/model/streaming"
import type { ChatMessage } from "@/domains/chat/model/types"
import {
  bindChatHubEvents,
  getRunsHubConnection,
  joinChatGroup,
  leaveChatGroup,
  useRunsHubStatus,
} from "@/shared/realtime/runs-hub"

/**
 * The live overlay rows of one running chat turn, or nulls when nothing runs.
 */
export interface ChatTurnOverlay {
  /** The just-sent message, before the wire echoes it. */
  readonly user: ChatMessage | null
  /** The streaming reply once the brain's first fragment arrived. */
  readonly reply: ChatMessage | null
}

const emptyOverlay: ChatTurnOverlay = { user: null, reply: null }

/** Group joins this process still holds, per session — two consoles, one connection. */
const joinedSessions = new Map<string, number>()

/**
 * Joins the session's live-turn group on the shared connection, reference-
 * counted: the route and the dock can both hold the same conversation open,
 * and SignalR group membership is not counted server-side — the first
 * unmount must not mute the other container. Returns the release function.
 */
function retainChatGroup(
  connection: HubConnection,
  sessionId: string
): () => void {
  const held = (joinedSessions.get(sessionId) ?? 0) + 1
  joinedSessions.set(sessionId, held)

  if (held === 1) {
    void joinChatGroup(connection, sessionId)
  }

  return () => {
    const remaining = (joinedSessions.get(sessionId) ?? 1) - 1
    if (remaining <= 0) {
      joinedSessions.delete(sessionId)
      void leaveChatGroup(connection, sessionId)
      return
    }
    joinedSessions.set(sessionId, remaining)
  }
}

/**
 * Binds the chat server→client callbacks on a connection: chunks flow into
 * the live-turn store, and the terminal signal closes the overlay **and**
 * invalidates the transcript query — the tab that did not send learns the
 * turn ended through the same event. Called once per connection by the
 * realtime provider, next to the runs wiring; pure plumbing so a test can
 * bind it to any connection-shaped object.
 */
export function bindChatTurnEvents(
  connection: Pick<HubConnection, "on">,
  invalidate: (queryKey: readonly unknown[]) => void
): void {
  bindChatHubEvents(
    connection,
    (event) => {
      applyChatChunk(event)
    },
    (event) => {
      completeChatTurn(event.sessionId)
      invalidate(chatMessagesQueryKey(event.sessionId))
    }
  )
}

/**
 * The console's live turn: subscribes to the streaming store and holds the
 * session's hub group for as long as a conversation is open.
 *
 * **The join** mirrors `useJoinRunGroup`: it rides the provider's connection
 * and re-runs when the socket turns live, so a console opened before the
 * handshake still joins. **The events** are bound by the provider at
 * connection creation (see `bindChatTurnEvents`) — a console that mounts
 * late still sees fragments because the store, not the hook, holds them.
 *
 * The sender does not depend on the terminal event to clear: the send
 * mutation's own settlement clears first most of the time, and the event is
 * the safety net for every other connection in the group.
 */
export function useChatTurnStream(sessionId: string | null): ChatTurnOverlay {
  const status = useRunsHubStatus()

  useEffect(() => {
    if (!sessionId || status !== "live") {
      return
    }
    const connection = getRunsHubConnection()
    if (!connection) {
      return
    }

    return retainChatGroup(connection, sessionId)
  }, [sessionId, status])

  const { stream } = useSyncExternalStore(
    chatTurnStreamStore.subscribe,
    chatTurnStreamStore.getSnapshot,
    chatTurnStreamStore.getSnapshot
  )

  return overlayOf(stream, sessionId)
}

/** The store's stream → the rows this session's thread should draw. */
function overlayOf(
  stream: ChatTurnStream | null,
  sessionId: string | null
): ChatTurnOverlay {
  if (!stream || stream.sessionId !== sessionId) {
    return emptyOverlay
  }

  return {
    user: pendingUserMessageOf(stream),
    reply: streamingReplyMessageOf(stream),
  }
}

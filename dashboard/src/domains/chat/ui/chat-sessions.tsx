import { Plus } from "lucide-react"

import type { ChatSession } from "@/domains/chat/model/types"
import { cn } from "@/shared/lib/utils"
import { Button, ScreenState, Skeleton, Tooltip } from "@/shared/ui"

import styles from "./chat-sessions.module.css"

/* Rail rows, not paragraphs: four short bars at the width a title and its age
   take, so the list does not change shape when the conversations land. */
const SKELETON_WIDTHS = ["72%", "54%", "66%", "45%"]

export interface ChatSessionsProps {
  sessions: ChatSession[]
  currentId: string | null
  onSelect: (sessionId: string) => void
  onStart: () => void
  busy?: boolean
  /**
   * The list has not arrived yet.
   *
   * Without this the rail says "no conversations yet" for as long as the
   * request takes, which is the empty state answering a question that has not
   * been asked — and the operator who has forty conversations reads it as
   * having lost them.
   */
  loading?: boolean
  /**
   * The read failed, and the console is already saying so with the one retry.
   * The rail then says nothing at all rather than offering a second, wrong
   * reading of the same dead wire.
   */
  failed?: boolean
}

/**
 * The conversations, resumable.
 *
 * A list of readings, not a list of chats: each row says what the conversation
 * turned out to be about and how long ago it was last spoken to, because those
 * are the two things somebody uses to find the one they were in yesterday.
 * The count of messages is not one of them and is deliberately absent.
 *
 * The rows are buttons rather than links. A conversation is not a destination
 * in this product — the route is `/chat`, and which session is open is state
 * the screen holds — so a link would be a link to the page you are already on.
 */
export function ChatSessions({
  sessions,
  currentId,
  onSelect,
  onStart,
  busy,
  loading,
  failed,
}: ChatSessionsProps) {
  return (
    <nav className={styles.sessions} aria-label="Conversations">
      <div className={styles.head}>
        <p className={styles.headLabel}>conversations</p>
        <Tooltip content="New conversation">
          <Button
            size="icon-sm"
            variant="ghost"
            data-test="chat-new"
            aria-label="New conversation"
            disabled={busy}
            onClick={onStart}
          >
            <Plus aria-hidden="true" />
          </Button>
        </Tooltip>
      </div>

      {loading ? (
        <Skeleton
          lines={SKELETON_WIDTHS}
          label="Loading conversations"
          data-test="chat-sessions-loading"
        />
      ) : failed ? null : sessions.length === 0 ? (
        /* Flush, not gutter: the rail already pays for its own room, and a
           state indented past the rows it stands in place of would not line up
           with the column it belongs to. */
        <ScreenState
          kind="empty"
          title="No conversations yet"
          description="Start one with the plus above. Every conversation stays here to come back to."
          inset="flush"
          data-test="chat-sessions-empty"
        />
      ) : (
        <ul className={styles.list}>
          {sessions.map((session) => {
            const current = session.id === currentId
            return (
              <li key={session.id}>
                <button
                  type="button"
                  className={cn(styles.row, current && styles.rowCurrent)}
                  data-test="chat-session"
                  data-session={session.id}
                  aria-current={current ? "true" : undefined}
                  onClick={() => onSelect(session.id)}
                >
                  <span className={styles.rowTitle}>{session.title}</span>
                  <span className={styles.rowAge}>{session.age}</span>
                </button>
              </li>
            )
          })}
        </ul>
      )}
    </nav>
  )
}

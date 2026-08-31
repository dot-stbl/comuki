import { Plus } from "lucide-react"

import type { ChatSession } from "@/domains/chat/model/types"
import { cn } from "@/shared/lib/utils"
import { Button, Tooltip } from "@/shared/ui"

import styles from "./chat-sessions.module.css"

export interface ChatSessionsProps {
  sessions: ChatSession[]
  currentId: string | null
  onSelect: (sessionId: string) => void
  onStart: () => void
  busy?: boolean
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
    </nav>
  )
}

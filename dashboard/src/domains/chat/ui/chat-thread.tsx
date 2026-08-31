import { useEffect, useMemo, useRef } from "react"

import type {
  ChatMessage as Message,
  ProposalDecision,
} from "@/domains/chat/model/types"

import { ChatMessage } from "./chat-message"
import { MessageText } from "./message-text"

import styles from "./chat-thread.module.css"

export interface ChatThreadProps {
  messages: Message[]
  onDecide: (proposalId: string, decision: ProposalDecision) => void
  busy?: boolean
}

/**
 * The messages, and the accessibility decision a chat gets wrong by default.
 *
 * ## The politeness, decided rather than defaulted
 *
 * A streaming reply that pushes every token into a live region is unusable: a
 * screen reader reads the same growing sentence from the start, over and over,
 * and there is no gesture that stops it. The usual fix — `aria-live="off"` on
 * the thread — is worse, because then nothing is announced at all and the
 * operator has to go looking for the answer they asked for.
 *
 * So the thread is split in two, and the split is the whole design:
 *
 * - **The log** is `role="log"` with `aria-live="polite"` and
 *   `aria-relevant="additions"`. It holds every message that has *finished*.
 *   A finished message is announced once, when it is added, and never again —
 *   which is exactly the reading a log wants. `polite` and not `assertive`:
 *   nothing the console says is worth interrupting a sentence the operator is
 *   already listening to.
 * - **The reply in flight** is rendered *outside* the log, and is
 *   `aria-hidden`. Its text is changing several times a second and there is no
 *   politeness setting that makes that bearable. Instead a single
 *   `role="status"` says, once, that a reply is arriving — and when it lands it
 *   enters the log like every other message and is read out in full, once.
 *
 * The net effect for somebody listening: "the assistant is replying", a pause,
 * then the answer. For somebody looking: the text arrives as it is written.
 * Neither reading is a degraded version of the other.
 *
 * ## The height chain
 *
 * `.thread` is a flex column with `min-block-size: 0` inside the page's grid
 * cell, and `.scroll` is `flex: 1 1 0` with `overflow-y: auto`. The composer
 * beneath is `flex: 0 0 auto`. jsdom computes none of this, so it is
 * hand-traced in `chat-page.module.css` from `.shell` down.
 */
export function ChatThread({ messages, onDecide, busy }: ChatThreadProps) {
  const scroll = useRef<HTMLDivElement | null>(null)

  // A reply in flight is by definition the newest thing in the thread, so the
  // split is a partition rather than a search through the middle of the list.
  const { settled, pending } = useMemo(() => {
    const last = messages[messages.length - 1]
    return last?.streaming
      ? { settled: messages.slice(0, -1), pending: last }
      : { settled: messages, pending: null }
  }, [messages])

  useEffect(() => {
    const port = scroll.current
    if (port) {
      port.scrollTop = port.scrollHeight
    }
  }, [messages])

  return (
    <div className={styles.thread} data-test="chat-thread">
      <div className={styles.scroll} ref={scroll}>
        {settled.length === 0 && !pending ? (
          <div className={styles.empty} data-test="chat-empty">
            <h2 className={styles.emptyTitle}>Nothing said yet</h2>
            <p className={styles.emptyBody}>
              This is the same control plane the screens drive, reached by
              typing. Ask it what the swarm is doing, or start with a slash to
              see everything it can do. Anything that would change something
              comes back as a proposal you press — it never acts on its own.
            </p>
          </div>
        ) : null}

        <ol
          className={styles.log}
          data-test="chat-log"
          role="log"
          aria-live="polite"
          aria-relevant="additions"
          aria-label="Conversation"
        >
          {settled.map((message) => (
            <ChatMessage
              key={message.id}
              message={message}
              onDecide={onDecide}
              busy={busy}
            />
          ))}
        </ol>

        {pending ? (
          /* Outside the log, and hidden from assistive technology while the
             tokens arrive. It joins the log — and is read once, in full — the
             moment it settles. */
          <div
            className={styles.pending}
            data-test="chat-streaming"
            aria-hidden="true"
          >
            <div className={styles.pendingByline}>
              <span className={styles.pendingAuthor}>comuki</span>
              <span className={styles.pendingClock}>{pending.at}</span>
            </div>
            {pending.text ? <MessageText text={pending.text} /> : null}
            <span className={styles.cursor} />
          </div>
        ) : null}
      </div>

      {/* One announcement per reply, at the start of it. Empty the rest of the
          time, so nothing is repeated when the thread re-renders. */}
      <p className={styles.announce} role="status" data-test="chat-announce">
        {pending ? "the assistant is replying" : ""}
      </p>
    </div>
  )
}

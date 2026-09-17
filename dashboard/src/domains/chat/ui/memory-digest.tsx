import { digestBody, digestFactCount } from "@/domains/chat/model/dynamics"
import type { ChatMessage as Message } from "@/domains/chat/model/types"

import styles from "./chat-message.module.css"

export interface MemoryDigestProps {
  message: Message
}

/**
 * The memory the turn was fed, as a chip rather than a paragraph.
 *
 * The digest is journaled for audit as its own row — context the console
 * *gave* the brain, not words it said to the operator — and the honest
 * weight for that in a transcript is one line: `memory: 2 facts`, in the
 * waiting hue, with the facts themselves folded behind it for the operator
 * who needs to check what yesterday's shift knew. Drawing it at full prose
 * weight made the loudest thing in the thread something nobody said.
 *
 * The count is a reading of the digest's own lines — one recollection per
 * line is the shape the memory service writes — and an empty digest says
 * `0 facts` rather than nothing: "the brain was told nothing" is itself the
 * fact an operator would want beside an answer that ignored yesterday.
 */
export function MemoryDigest({ message }: MemoryDigestProps) {
  const facts = digestFactCount(message)
  const body = digestBody(message)

  return (
    <details className={styles.memory} data-test="chat-memory">
      <summary className={styles.memorySummary}>
        <span className={styles.memoryLabel}>memory</span>
        <span className={styles.memoryCount}>
          {facts} {facts === 1 ? "fact" : "facts"}
        </span>
      </summary>
      <p className={styles.memoryBody}>{body}</p>
    </details>
  )
}

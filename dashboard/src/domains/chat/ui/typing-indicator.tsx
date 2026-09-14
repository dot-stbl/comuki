import { cn } from "@/shared/lib/utils"

import styles from "./chat-thread.module.css"

export interface TypingIndicatorProps {
  className?: string
}

/**
 * The pause between the send and the first word — three dots and a sentence.
 *
 * The turn's real phases live in the transcript once they exist; this is the
 * one moment that has no row yet. It renders *outside* the log (a transient
 * state, not an addition to the journal) and `aria-hidden`, because the
 * thread's single `role="status"` announcement is what says it once, in
 * words, to somebody listening — a live region on the dots themselves would
 * say "bullet bullet bullet".
 *
 * The dots are the cursor's own grammar: the same accent, the same
 * reduced-motion contract (still, and therefore quieter, for anyone who
 * asked), and motion that is a *state* rather than decoration — a block that
 * breathes says the turn has started and not finished.
 */
export function TypingIndicator({ className }: TypingIndicatorProps) {
  return (
    <div
      className={cn(styles.typing, className)}
      data-test="chat-typing"
      aria-hidden="true"
    >
      <span className={styles.typingDots}>
        <span className={styles.typingDot} />
        <span className={styles.typingDot} />
        <span className={styles.typingDot} />
      </span>
      <span className={styles.typingWords}>Comuki думает</span>
    </div>
  )
}

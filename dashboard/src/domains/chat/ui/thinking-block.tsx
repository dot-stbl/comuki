import styles from "./chat-message.module.css"

export interface ThinkingBlockProps {
  text: string
  /** What it cost, when the turn said. A figure, so mono and tabular. */
  tokens?: number
}

/**
 * The model's working-out, folded away.
 *
 * Collapsed by default, muted, and with **no animation at all** — the three
 * decisions are one decision. A console that performs its thinking on screen
 * is asking the operator to watch it work; this product's console is a pult,
 * and the reasoning is *evidence*, reached when an answer surprises somebody
 * and invisible the rest of the time. Motion here would be ambient
 * decoration, which the design system does not allow, rather than a moment.
 *
 * A native `<details>`, so it is keyboard-operable, announced as a disclosure,
 * and open before the page finishes loading if the operator asked their
 * browser to find text inside it. There is nothing here React does better.
 */
export function ThinkingBlock({ text, tokens }: ThinkingBlockProps) {
  return (
    <details className={styles.thinking} data-test="chat-thinking">
      <summary className={styles.thinkingSummary}>
        <span className={styles.thinkingWords}>thinking</span>
        {tokens === undefined ? null : (
          <span className={styles.thinkingCount}>
            {tokens.toLocaleString("en-US")} tokens
          </span>
        )}
      </summary>
      <p className={styles.thinkingText}>{text}</p>
    </details>
  )
}

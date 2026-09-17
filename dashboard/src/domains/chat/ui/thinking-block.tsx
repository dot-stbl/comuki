import { Check, LoaderCircle } from "lucide-react"

import {
  parseThinkingSteps,
  type ThinkingStep,
} from "@/domains/chat/model/dynamics"
import { cn } from "@/shared/lib/utils"

import styles from "./chat-message.module.css"

export interface ThinkingBlockProps {
  text: string
  /** What it cost, when the turn said. A figure, so mono and tabular. */
  tokens?: number
  /**
   * The turn this working-out belongs to is still arriving.
   *
   * The one state that moves: the last line spins, the summary runs, and the
   * block is drawn open in place — this is the "watching it think" moment
   * the console owes the operator. The moment the turn settles the same
   * lines become *evidence* and fold away (see below), which is why this is
   * a prop and not something the block guesses from a clock.
   */
  active?: boolean
}

/**
 * The model's working-out — moving while the turn moves, folded when it is
 * history.
 *
 * Two readings of one list of lines, and the difference is a decision the
 * product already made: **in flight, the working-out is the show** — each
 * line a step, the finished ones checked in the success hue, the newest one
 * spinning in the running hue, nothing hidden while the operator waits.
 * **Settled, it is evidence** — collapsed behind a summary, muted, opened
 * when an answer surprises somebody and invisible the rest of the time. A
 * console that performs its thinking on screen *after* the answer exists is
 * asking the operator to watch a recording.
 *
 * The steps keep the words the brain sent: a line shaped like a call
 * (`memory.search("x")`) gets the mono emphasis and its tail kept beside it,
 * and a sentence stays a sentence. Nothing is invented, reordered or
 * summarised — the block shapes, it does not rewrite.
 *
 * The settled reading is a native `<details>`, so it is keyboard-operable,
 * announced as a disclosure, and open before the page finishes loading if
 * the operator asked their browser to find text inside it. There is nothing
 * here React does better.
 */
export function ThinkingBlock({ text, tokens, active }: ThinkingBlockProps) {
  const steps = parseThinkingSteps(text)

  if (active) {
    return (
      <div
        className={cn(styles.thinking, styles.thinkingActive)}
        data-test="chat-thinking"
        data-active="true"
      >
        <p className={cn(styles.thinkingWords, styles.thinkingWordsActive)}>
          thinking
        </p>
        <ol className={styles.thinkingSteps}>
          {steps.map((step, index) => (
            <ThinkingStepRow
              key={index}
              step={step}
              running={index === steps.length - 1}
            />
          ))}
        </ol>
      </div>
    )
  }

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
      <ol className={styles.thinkingSteps}>
        {steps.map((step, index) => (
          <ThinkingStepRow key={index} step={step} running={false} />
        ))}
      </ol>
    </details>
  )
}

/**
 * One line of the working-out, as a step.
 *
 * `running` is the honest reading of a state, not decoration: a check says
 * the brain moved past that line, the spinner says it is on that line now.
 * History keeps every check — a spinner that outlived its turn would be the
 * block claiming work it finished.
 */
function ThinkingStepRow({
  step,
  running,
}: {
  step: ThinkingStep
  running: boolean
}) {
  return (
    <li className={styles.stepRow} data-test="chat-thinking-step">
      {running ? (
        <LoaderCircle className={styles.stepRunningIcon} aria-hidden="true" />
      ) : (
        <Check className={styles.stepDoneIcon} aria-hidden="true" />
      )}
      {step.call ? (
        <>
          <span className={styles.stepCall}>{step.call}</span>
          {step.tail ? (
            <span
              className={cn(styles.stepTail, running && styles.stepTailRunning)}
            >
              {step.tail}
            </span>
          ) : null}
        </>
      ) : (
        <span className={styles.stepNote}>{step.text}</span>
      )}
    </li>
  )
}

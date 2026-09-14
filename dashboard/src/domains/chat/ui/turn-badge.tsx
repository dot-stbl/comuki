import type { TurnPhase } from "@/domains/chat/model/dynamics"

import styles from "./chat-message.module.css"

export interface TurnBadgeProps {
  phase: TurnPhase
}

/**
 * Where the turn is, said beside the name that is working.
 *
 * Three words over one closed vocabulary — `thinking` while the working-out
 * is still moving, `plan` when the turn produced one, `done` for everything
 * else an assistant said — and a hue each, from the status set the whole
 * product already reads: running for the one that moves, success for the one
 * that landed, escalated for the one waiting on a person to look at a plan.
 *
 * The word rides the DOM lowercase and takes its capitals in CSS, because
 * the phase is a value out of `turnPhase`'s vocabulary — the same
 * verbatim-spelling rule the status badge keeps — while the mockup's
 * small-caps reading is presentation and stays in the module.
 */
export function TurnBadge({ phase }: TurnBadgeProps) {
  return (
    <span className={styles.phase} data-test="chat-phase" data-phase={phase}>
      {phase}
    </span>
  )
}

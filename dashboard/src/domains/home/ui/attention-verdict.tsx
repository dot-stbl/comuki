import { Check } from "lucide-react"

import type { AttentionStatus } from "@/domains/home/model/attention"

import styles from "./attention-verdict.module.css"

export interface AttentionVerdictProps {
  /** How many runs are owed a decision. The number the screen exists to give. */
  count: number
  /** That total split into the statuses behind it, worst-first. */
  mix: Array<{ status: AttentionStatus; count: number }>
  /** The worst status present — the band's hue and weave. */
  worst: AttentionStatus | null
  /** The rest of the shift, for the line the clear state reads instead. */
  running: number
  queued: number
}

/**
 * The verdict: whether a person is needed, said before anything else on screen.
 *
 * Two readings out of one shape. Both are a band with a woven status rail, a
 * lead slot and two lines, so the screen does not visibly restructure itself
 * between a quiet shift and a loud one — the engineer learns one place to look.
 * What changes is what fills the lead: a figure when a decision is owed, a mark
 * when none is.
 *
 * The clear reading is the one this screen is in most of the day, so it is
 * written as a **statement** rather than as an absence: a mark, a full sentence
 * in the display voice, and a line of live figures underneath proving the swarm
 * is running and the data arrived. An empty list here would say the same thing
 * as a list that failed to load, and the two must never look alike.
 *
 * Hue never carries the reading alone: the rail is hue *and* weave, the words
 * name the statuses outright, and the figure is the figure.
 */
export function AttentionVerdict({
  count,
  mix,
  worst,
  running,
  queued,
}: AttentionVerdictProps) {
  const clear = count === 0

  return (
    <div
      className={styles.band}
      data-status={clear ? "clear" : worst}
      data-test="attention-verdict"
      data-clear={clear || undefined}
      role="status"
    >
      <span className={styles.rail} aria-hidden="true" />

      <div className={styles.body}>
        <p className={styles.head}>
          {clear ? (
            <>
              <Check className={styles.mark} aria-hidden="true" />
              <span className={styles.verdict}>Nothing needs you</span>
            </>
          ) : (
            <>
              <span className={styles.count}>{count}</span>
              <span className={styles.label}>
                {count === 1 ? "run needs a decision" : "runs need a decision"}
              </span>
            </>
          )}
        </p>

        <p className={styles.line}>
          {clear ? (
            running + queued === 0 ? (
              "The swarm is empty — nothing queued, nothing in flight."
            ) : (
              <>
                <span className={styles.figure}>{running}</span> running ·{" "}
                <span className={styles.figure}>{queued}</span> queued — the
                swarm is moving on its own.
              </>
            )
          ) : (
            mix.map((entry, index) => (
              <span key={entry.status}>
                {index > 0 ? " · " : null}
                <span className={styles.figure}>{entry.count}</span>{" "}
                {entry.status}
              </span>
            ))
          )}
        </p>
      </div>
    </div>
  )
}

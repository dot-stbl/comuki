import { failurePercent } from "@/domains/cost/model/cost"
import type { CostFailure } from "@/domains/cost/model/types"
import { cn } from "@/shared/lib/utils"

import styles from "./failure-analytics.module.css"

export interface FailureAnalyticsProps {
  rows: CostFailure[]
  className?: string
}

/**
 * Where the swarm breaks, by profile.
 *
 * The other half of the cost question: the breakdown beside it says what a day
 * costs, and this says what is buying nothing. A rate with no note is a number
 * nobody can act on, so every row carries the sentence as well — one line,
 * truncating, because the list is scanned before it is read.
 */
export function FailureAnalytics({ rows, className }: FailureAnalyticsProps) {
  if (rows.length === 0) {
    return (
      <p className={cn(styles.empty, className)} data-test="failures-empty">
        nothing failed today
      </p>
    )
  }

  return (
    <ul className={cn(styles.rows, className)} data-test="failure-analytics">
      {rows.map((row) => (
        <li key={row.profile} className={styles.row}>
          <span className={styles.head}>
            <span className={styles.profile} title={row.profile}>
              {row.profile}
            </span>
            <span className={styles.rate}>{failurePercent(row)}%</span>
          </span>
          <span className={styles.note} title={row.note}>
            {row.note}
          </span>
        </li>
      ))}
    </ul>
  )
}

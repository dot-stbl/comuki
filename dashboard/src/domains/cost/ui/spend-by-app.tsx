import { spendAxis, spendShare } from "@/domains/cost/model/cost"
import type { CostByApp } from "@/domains/cost/model/types"
import { cn } from "@/shared/lib/utils"

import styles from "./spend-by-app.module.css"

export interface SpendByAppProps {
  rows: CostByApp[]
  className?: string
}

/**
 * Where the day's money went, ranked.
 *
 * A list rather than a chart, and no charting library under it: the reading is
 * "which app is expensive and by how much against the one above it", and that
 * is a shared axis and five lengths. Every row states its own figure, so the
 * bars are drawn on top of a reading rather than being one — the list is
 * complete in words with every channel removed.
 *
 * The axis is the largest spend in the breakdown, shared by every row. Bars on
 * their own scales cannot be compared, and comparing them is the whole task.
 */
export function SpendByApp({ rows, className }: SpendByAppProps) {
  const axis = spendAxis(rows)

  if (rows.length === 0) {
    return (
      <p className={cn(styles.empty, className)} data-test="spend-empty">
        nothing spent today
      </p>
    )
  }

  return (
    <ul className={cn(styles.rows, className)} data-test="spend-by-app">
      {rows.map((row) => (
        <li key={row.app} className={styles.row}>
          <span className={styles.app} title={row.app}>
            {row.app}
          </span>
          <span className={styles.channel} aria-hidden="true">
            <span
              className={styles.fill}
              style={{ inlineSize: `${Math.round(spendShare(row, axis) * 100)}%` }}
            />
          </span>
          {/* One decimal, which is this screen's third precision and each one
              is a decision: cents for a per-success price, whole dollars for a
              day's total, a dime for a per-app share. */}
          <span className={styles.spend}>${row.spend.toFixed(1)}</span>
        </li>
      ))}
    </ul>
  )
}

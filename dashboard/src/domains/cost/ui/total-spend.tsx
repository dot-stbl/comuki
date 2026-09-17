import type { ReactNode } from "react"

import { periodDelta } from "@/domains/cost/model/cost"
import { CostStat } from "@/domains/cost/ui/cost-stat"

import styles from "./total-spend.module.css"

export interface TotalSpendProps {
  /** Total spend for the active period. */
  total: number
  /** Spend in the period immediately before this one. */
  previousTotal: number
  /** Period-aware copy ("this week", "this month"). */
  periodLabel: string
  /** Compact sentence under the figure ("today's burn", "month-to-date"). */
  burnNote: ReactNode
  className?: string
}

/**
 * The headline reading — the period's spend, a delta vs the period before it,
 * and a one-line burn-rate note.
 *
 * A `CostStat` like the two tiles beside it. The three are equal readings of
 * one period standing in one row, and a figure that took its own step and its
 * own hairline recipe was the row disagreeing with itself — this is the label,
 * the figure and the line; the delta is the one thing the tile has that the
 * others do not, and it rides in the slot between them.
 *
 * No heat. The delta is a fact about a period that has already closed, and a
 * fact does not get a hue — the tile that *does* light its edge is the budget,
 * because its figure has a consequence written beside it.
 *
 * The delta is honest about its absence: a brand-new period returns `null` and
 * the tile says so, rather than printing the misleading "0%" a zero previous
 * would otherwise produce.
 */
export function TotalSpend({
  total,
  previousTotal,
  periodLabel,
  burnNote,
  className,
}: TotalSpendProps) {
  const delta = periodDelta(total, previousTotal)

  return (
    <CostStat
      name="total"
      label={`${periodLabel} spend`}
      prefix="$"
      value={total.toFixed(2)}
      sub={burnNote}
      className={className}
    >
      <p className={styles.delta}>
        {delta === null ? (
          <span className={styles.deltaDash}>no prior period yet</span>
        ) : delta > 0 ? (
          <>
            <span className={styles.deltaUp} data-test="total-spend-delta">
              ▲ {(delta * 100).toFixed(0)}%
            </span>
            <span className={styles.deltaLabel}>vs previous {periodLabel}</span>
          </>
        ) : delta < 0 ? (
          <>
            <span className={styles.deltaDown} data-test="total-spend-delta">
              ▼ {Math.abs(delta * 100).toFixed(0)}%
            </span>
            <span className={styles.deltaLabel}>vs previous {periodLabel}</span>
          </>
        ) : (
          <>
            <span className={styles.deltaFlat} data-test="total-spend-delta">
              ◆ flat
            </span>
            <span className={styles.deltaLabel}>vs previous {periodLabel}</span>
          </>
        )}
      </p>
    </CostStat>
  )
}

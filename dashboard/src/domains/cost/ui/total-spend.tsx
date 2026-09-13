import type { ReactNode } from "react"

import type { CostHeat } from "@/domains/cost/model/cost"
import { costHeat, periodDelta } from "@/domains/cost/model/cost"
import { cn } from "@/shared/lib/utils"

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
 * The headline reading — a large currency figure, the period it covers,
 * a delta vs the previous period, and a one-line burn-rate note.
 *
 * The delta is honest about its absence: a brand-new period returns
 * `null`, the screen renders a dash rather than the misleading "0%" a
 * zero previous would otherwise print. Three colours carry the same
 * threshold as the budget tile, so the operator learns one mapping.
 */
export function TotalSpend({
  total,
  previousTotal,
  periodLabel,
  burnNote,
  className,
}: TotalSpendProps) {
  const delta = periodDelta(total, previousTotal)
  const deltaShare = delta ?? 0
  const heat: CostHeat =
    delta === null ? "ok" : costHeat(Math.max(0, deltaShare) * 0.5 + 0.5)

  return (
    <article
      className={cn(styles.spend, className)}
      data-test="total-spend"
      data-heat={heat}
    >
      <span className={styles.label}>{periodLabel} spend</span>
      <span className={styles.figure}>
        <span className={styles.unit}>$</span>
        <span className={styles.value}>{total.toFixed(2)}</span>
      </span>
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
      <p className={styles.sub}>{burnNote}</p>
    </article>
  )
}

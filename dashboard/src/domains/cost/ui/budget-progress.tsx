import type { CostBudget, CostHeat } from "@/domains/cost/model/cost"
import { budgetHeat } from "@/domains/cost/model/cost"
import { ProxyBudgetMeter } from "@/domains/cost/ui/proxy-budget-meter"
import { cn } from "@/shared/lib/utils"

import styles from "./budget-progress.module.css"

export interface BudgetProgressProps {
  /** Today's burn — the figure the meter sits beside. */
  todayBurn: number
  /** Today's budget cap. */
  todayCap: number
  /** Month-to-date spend — second line, always shown. */
  monthToDate: number
  /** Month's budget cap. */
  monthCap: number
  className?: string
}

/**
 * The screen's two-reading budget widget.
 *
 * Today's burn is the figure the operator has to decide about right now;
 * month-to-date is the line that says how the month is going overall. The
 * two share a colour but never a meter: today gets a bar (it has a single
 * reading), month gets the same percent word-and-figure treatment it gets
 * everywhere else. Heat is `budgetHeat(today)`, the same three words the
 * forecast and proxy-budget tiles use, so a today at 90% reads exactly
 * like a forecast at 90%.
 */
export function BudgetProgress({
  todayBurn,
  todayCap,
  monthToDate,
  monthCap,
  className,
}: BudgetProgressProps) {
  const todayBudget: CostBudget = { used: todayBurn, cap: todayCap }
  const heat: CostHeat = budgetHeat(todayBudget)
  const todayShare = todayCap > 0 ? todayBurn / todayCap : 1
  const monthShare = monthCap > 0 ? monthToDate / monthCap : 1

  return (
    <article
      className={cn(styles.budget, className)}
      data-test="budget-progress"
      data-heat={heat}
    >
      <span className={styles.label}>Budget progress</span>
      <span className={styles.figure}>
        <span className={styles.unit}>$</span>
        <span className={styles.value}>{todayBurn.toFixed(0)}</span>
        <span className={styles.figureSep}>/</span>
        <span className={styles.unit}>$</span>
        <span className={styles.value}>{todayCap.toFixed(0)}</span>
      </span>
      <ProxyBudgetMeter budget={todayBudget} />
      <p className={styles.sub}>
        <span className={styles.subKey}>today</span>{" "}
        <span className={styles.subValue}>
          ${todayBurn.toFixed(0)} of ${todayCap.toFixed(0)} cap
          {todayCap > 0 ? ` · ${Math.round(todayShare * 100)}%` : ""}
        </span>
      </p>
      <p className={styles.sub}>
        <span className={styles.subKey}>month-to-date</span>{" "}
        <span className={styles.subValue}>
          ${monthToDate.toFixed(0)} of ${monthCap.toFixed(0)} cap
          {monthCap > 0 ? ` · ${Math.round(monthShare * 100)}%` : ""}
        </span>
      </p>
    </article>
  )
}

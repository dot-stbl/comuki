import type { CostBudget, CostHeat } from "@/domains/cost/model/cost"
import { budgetHeat } from "@/domains/cost/model/cost"
import { CostStat } from "@/domains/cost/ui/cost-stat"
import { ProxyBudgetMeter } from "@/domains/cost/ui/proxy-budget-meter"

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
 * A `CostStat` — the same tile as the two beside it — carrying the meter in
 * its slot and two named readings in its line. Today's burn is the figure the
 * operator has to decide about right now; month-to-date is the line that says
 * how the month is going overall. The two share a colour but never a meter:
 * today gets a bar (it has a single reading), month gets the same percent
 * word-and-figure treatment it gets everywhere else.
 *
 * This is the one tile of the three that lights its edge, because it is the
 * one whose figure has a consequence written beside it. Heat is
 * `budgetHeat(today)`, the same three words the forecast tile uses, so a today
 * at 90% reads exactly like a forecast at 90%.
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
    <CostStat
      name="budget"
      label="Budget progress"
      prefix="$"
      value={todayBurn.toFixed(0)}
      /* The cap is the context the figure is read against, not a second
         reading — so it rides in the unit slot beside the number rather than
         standing at the number's own weight. */
      suffix={`/ $${todayCap.toFixed(0)}`}
      heat={heat}
      sub={
        <>
          <span className={styles.line}>
            <span className={styles.key}>today</span>{" "}
            <span className={styles.value}>
              ${todayBurn.toFixed(0)} of ${todayCap.toFixed(0)} cap
              {todayCap > 0 ? ` · ${Math.round(todayShare * 100)}%` : ""}
            </span>
          </span>
          <span className={styles.line}>
            <span className={styles.key}>month-to-date</span>{" "}
            <span className={styles.value}>
              ${monthToDate.toFixed(0)} of ${monthCap.toFixed(0)} cap
              {monthCap > 0 ? ` · ${Math.round(monthShare * 100)}%` : ""}
            </span>
          </span>
        </>
      }
      className={className}
    >
      <ProxyBudgetMeter budget={todayBudget} />
    </CostStat>
  )
}

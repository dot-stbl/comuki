import { budgetHeat, budgetShare } from "@/domains/cost/model/cost"
import type { CostBudget } from "@/domains/cost/model/types"
import { cn } from "@/shared/lib/utils"

import styles from "./proxy-budget-meter.module.css"

export interface ProxyBudgetMeterProps {
  budget: CostBudget
  className?: string
}

/**
 * How much of the proxy cap the day has spent, as a length.
 *
 * It carries no figures, and that is the point: it is drawn on a tile that
 * already states the share as a percentage and the two dollar amounts under
 * it, so this is decoration on top of a reading rather than a reading of its
 * own. Nothing on the screen is announced only as a length — which is why the
 * element is hidden from the accessibility tree entirely instead of being
 * given a label that would repeat the tile.
 *
 * Heat is three readings and no gradient between them. At the seeded two
 * thirds it draws in the neutral faint, exactly as the bar it replaced did;
 * the hue only arrives at 85%, which is where the tile's own line — "kill
 * switch at cap" — stops being a note and starts being a forecast.
 */
export function ProxyBudgetMeter({ budget, className }: ProxyBudgetMeterProps) {
  const share = budgetShare(budget)

  return (
    <span
      className={cn(styles.meter, className)}
      data-test="proxy-budget-meter"
      data-heat={budgetHeat(budget)}
      aria-hidden="true"
    >
      <span className={styles.channel}>
        <span
          className={styles.fill}
          style={{ inlineSize: `${Math.min(100, Math.round(share * 100))}%` }}
        />
      </span>
    </span>
  )
}

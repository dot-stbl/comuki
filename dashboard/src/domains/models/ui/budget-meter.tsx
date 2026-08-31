import {
  budgetHeat,
  budgetLeftUsd,
  budgetShare,
  isLive,
} from "@/domains/models/model/keys"
import type { VirtualKey } from "@/domains/models/model/types"
import { formatCost } from "@/domains/runs/model/format"
import { cn } from "@/shared/lib/utils"

import styles from "./budget-meter.module.css"

export interface BudgetMeterProps {
  entry: VirtualKey
  /**
   * Whether the cap is actually being applied. With the thin proxy off nothing
   * is enforced, and a bar that looks like a limit when there is no limit is
   * the one lie this screen must not tell.
   */
  enforced: boolean
  className?: string
}

/**
 * What a key has spent against its cap, and whether anything is stopping it.
 *
 * A meter states its own numbers: the figures are the reading and the bar is
 * drawn on top of them, so nothing here is announced only as a length. Heat is
 * three readings rather than a gradient — below 85% there is nothing to decide,
 * and a screen that colours a key at 40% has taught the operator to ignore the
 * colour by the time one reaches 90%.
 *
 * When the cap is not being enforced the bar is hatched rather than filled. It
 * still shows the same fraction, because the spend is real; what it stops
 * claiming is that anything will happen when the fraction reaches one.
 */
export function BudgetMeter({ entry, enforced, className }: BudgetMeterProps) {
  const heat = budgetHeat(entry)
  const share = budgetShare(entry)
  const live = isLive(entry)

  return (
    <span
      className={cn(styles.meter, className)}
      data-test="budget-meter"
      data-heat={live ? heat : "idle"}
      data-enforced={enforced ? "" : undefined}
      title={
        enforced
          ? undefined
          : "the proxy is off — this cap is recorded but not applied"
      }
    >
      <span className={styles.figures}>
        <span className={styles.spent}>{formatCost(entry.spentUsd)}</span>
        <span className={styles.of}>/</span>
        <span className={styles.cap}>{formatCost(entry.budgetUsd)}</span>
        <span className={styles.left}>
          {heat === "over"
            ? "over"
            : `${formatCost(budgetLeftUsd(entry))} left`}
        </span>
      </span>
      <span className={styles.channel} aria-hidden="true">
        <span
          className={styles.fill}
          style={{ inlineSize: `${Math.min(100, Math.round(share * 100))}%` }}
        />
      </span>
    </span>
  )
}

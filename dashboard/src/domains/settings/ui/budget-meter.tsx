import {
  budgetHeat,
  budgetLeftUsd,
  budgetPercent,
  budgetShare,
} from "@/domains/settings/model/budgets"
import type { Budgets } from "@/domains/settings/model/types"
import { cn } from "@/shared/lib/utils"

import styles from "./budget-meter.module.css"

/** Whole dollars: the cap is set in round numbers and read in round numbers. */
const dollars = (value: number) => `$${value.toFixed(0)}`

export interface BudgetMeterProps {
  budgets: Budgets
  className?: string
}

/**
 * What the proxy has spent against the global cap, and how close the
 * kill-switch is.
 *
 * A meter states its own numbers: the percentage, the two dollar figures and
 * what is left are all text, and the bar is drawn on top of them — so nothing
 * here is announced only as a length. Heat is three readings rather than a
 * gradient, for the reason `models/ui/budget-meter` gives: a bar that changes
 * colour at 40% has taught the operator to ignore colour by 90%.
 *
 * When the kill-switch is already thrown the channel is hatched rather than
 * filled. The fraction is still true — the spend is real — but a smooth fill
 * claims the number is still moving, and with new claims blocked it is not.
 */
export function BudgetMeter({ budgets, className }: BudgetMeterProps) {
  const heat = budgetHeat(budgets)
  const percent = budgetPercent(budgets)
  const left = budgetLeftUsd(budgets)

  return (
    <div
      className={cn(styles.meter, className)}
      data-test="budget-meter"
      data-heat={heat}
      data-stopped={budgets.killSwitch ? "" : undefined}
    >
      <p className={styles.reading}>
        <span className={styles.percent}>{percent}</span>
        <span className={styles.unit}>%</span>
        <span className={styles.left}>
          {heat === "over" ? "over the cap" : `${dollars(left)} left`}
        </span>
      </p>

      <span className={styles.channel} aria-hidden="true">
        <span
          className={styles.fill}
          style={{
            inlineSize: `${Math.min(100, Math.round(budgetShare(budgets) * 100))}%`,
          }}
        />
      </span>

      <p className={styles.figures} data-test="budget-figures">
        {dollars(budgets.usedUsd)}
        <span className={styles.of}>/</span>
        {dollars(budgets.globalUsd)}
        <span className={styles.note}>
          {budgets.killSwitch
            ? "kill-switch on · new claims blocked"
            : "kill-switch at cap"}
        </span>
      </p>
    </div>
  )
}

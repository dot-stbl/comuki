import {
  budgetHeat,
  budgetLeftUsd,
  budgetPercent,
  budgetShare,
} from "@/domains/settings/model/budgets"
import type { Budgets } from "@/domains/settings/model/types"
import { cn } from "@/shared/lib/utils"
import { Meter } from "@/shared/ui"

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
 * gradient, for the reason `models/ui/key-budget-meter` gives: a bar that
 * changes colour at 40% has taught the operator to ignore colour by 90%.
 *
 * When the kill-switch is already thrown the channel is hatched rather than
 * filled. The fraction is still true — the spend is real — but a smooth fill
 * claims the number is still moving, and with new claims blocked it is not.
 *
 * The hook is `global-budget-meter` and not `budget-meter`, which is what it
 * used to be: `models/ui/key-budget-meter` renders `budget-meter` too, and one
 * mark on two components that read two different caps is a hook that finds
 * whichever of them a screen happens to contain. The models one is the one four
 * assertions already select by, so this is the side that moved.
 */
export function BudgetMeter({ budgets, className }: BudgetMeterProps) {
  const heat = budgetHeat(budgets)
  const percent = budgetPercent(budgets)
  const left = budgetLeftUsd(budgets)

  return (
    <div
      className={cn(styles.meter, className)}
      data-test="global-budget-meter"
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

      {/* The bordered channel rather than the bare one: this meter stands on
          the panel's own surface, where the lane material alone is not a wide
          enough step to read as an empty container. */}
      <Meter
        value={budgetShare(budgets)}
        tone="heat"
        edge
        hatched={budgets.killSwitch ? "queued" : undefined}
      />

      <p className={styles.figures} data-test="budget-figures">
        <span className={styles.amounts}>
          {dollars(budgets.usedUsd)}
          <span className={styles.of}>/</span>
          {dollars(budgets.globalUsd)}
        </span>
        <span className={styles.note}>
          {budgets.killSwitch
            ? "kill-switch on · new claims blocked"
            : "kill-switch at cap"}
        </span>
      </p>
    </div>
  )
}

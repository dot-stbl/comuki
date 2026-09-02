import { cn } from "@/shared/lib/utils"
import {
  spendDayAverage,
  spendPeakDay,
  spendWeekTotal,
} from "@/domains/cost/model/cost"
import type { CostDaySpend } from "@/domains/cost/model/types"
import { BarSeries } from "@/shared/ui"

import styles from "./spend-by-day.module.css"

export interface SpendByDayProps {
  days: CostDaySpend[]
  className?: string
}

/**
 * What the week cost, day by day, beside the sentence that says it.
 *
 * The bars are the time half of a report whose day half sits above them: the
 * per-day tile and this chart's last bar are one reading said twice, and the
 * chart exists to answer the question the tiles cannot — is the spend getting
 * better, or is today just a quiet day in a loud week. So the figure beside it
 * states the total, the average and the heaviest day in words, and the bars
 * confirm in shape what that sentence says. A screen reader loses nothing.
 *
 * Neutral chrome, no hue: a spend ranking carries no status, exactly like the
 * per-app bars under it. The one loud day of the week is named in the figure —
 * the auth-svc migration — and does not need a colour to be found.
 */
export function SpendByDay({ days, className }: SpendByDayProps) {
  const total = spendWeekTotal(days)
  const average = spendDayAverage(days)
  const peak = spendPeakDay(days)

  if (days.length === 0 || peak === null || average === null) {
    return (
      <p className={cn(styles.empty, className)} data-test="spend-by-day">
        nothing spent this week
      </p>
    )
  }

  return (
    <div className={cn(styles.band, className)} data-test="spend-by-day">
      <p className={styles.figure}>
        <span className={styles.figureValue}>${total.toFixed(2)}</span> over
        the last {days.length} days ·{" "}
        <span className={styles.figureValue}>${average.toFixed(2)}</span> a day
        · heaviest <span className={styles.figureValue}>{peak.label}</span> at{" "}
        <span className={styles.figureValue}>${peak.spend.toFixed(2)}</span>
      </p>

      <BarSeries
        className={styles.chart}
        points={days.map((day) => ({
          key: day.label,
          label: day.label,
          segments: [{ value: day.spend }],
        }))}
        label={`Spend by day, dollars. $${total.toFixed(2)} over the last ${
          days.length
        } days, $${average.toFixed(2)} a day on average; heaviest ${
          peak.label
        } at $${peak.spend.toFixed(2)}.`}
      />
    </div>
  )
}

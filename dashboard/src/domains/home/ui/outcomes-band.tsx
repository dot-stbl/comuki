import {
  outcomeDayTotal,
  outcomeWindowTotal,
  OUTCOME_STATUSES,
  type OutcomeDay,
} from "@/domains/home/model/outcomes"
import { cn } from "@/shared/lib/utils"
import { BarSeries } from "@/shared/ui"

import styles from "./outcomes-band.module.css"

export interface OutcomesBandProps {
  days: OutcomeDay[]
  className?: string
}

/**
 * Run outcomes per day, stacked by how they ended, beside the sentence that
 * says it.
 *
 * The section it lives in answers "what is in flight now"; this band answers
 * the question that row cannot — is the swarm clearing its work, or piling up
 * failures. The stack order is the triage order's opposite end: success is the
 * broad calm base, and the worst status a run ended the day in rides on top,
 * where a changing shape is the first thing an eye catches.
 *
 * Hue carries the status and the words carry it too — the legend under the
 * chart and the figure beside it both name every status in the product's own
 * vocabulary, so the reading survives greyscale, colour blindness and a
 * screen reader alike.
 */
export function OutcomesBand({ days, className }: OutcomesBandProps) {
  const today = days[days.length - 1]
  const weekFailed = outcomeWindowTotal(days, "failed")

  if (!today) {
    return null
  }

  const todayTotal = outcomeDayTotal(today)
  const weekTotal = days.reduce((sum, day) => sum + outcomeDayTotal(day), 0)

  return (
    <div className={cn(styles.band, className)} data-test="home-outcomes">
      <div className={styles.reading}>
        <p className={styles.figure}>
          <span className={styles.figureValue}>{todayTotal}</span> finished
          today so far · <span className={styles.figureValue}>{weekTotal}</span>{" "}
          this week ·{" "}
          <span className={styles.figureValue}>{weekFailed}</span> failed
        </p>

        <ul className={styles.legend} aria-hidden="true">
          {OUTCOME_STATUSES.map((status) => (
            <li key={status} className={styles.key}>
              <span
                className={cn(styles.swatch, styles.status)}
                data-status={status}
              />
              {status}
            </li>
          ))}
        </ul>
      </div>

      <BarSeries
        className={styles.chart}
        points={days.map((day) => ({
          key: day.label,
          label: day.label,
          segments: day.outcomes.map((entry) => ({
            value: entry.count,
            status: entry.status,
          })),
        }))}
        label={`Run outcomes by day. ${todayTotal} finished today so far, ${weekTotal} this week, ${weekFailed} of them failed.`}
      />
    </div>
  )
}

import { Trans, useTranslation } from "react-i18next"

import {
  outcomeDayTotal,
  outcomeWindowTotal,
  OUTCOME_STATUSES,
  type OutcomeDay,
} from "@/domains/home/model/outcomes"
import { cn } from "@/shared/lib/utils"
import { BarSeries, Skeleton } from "@/shared/ui"

import styles from "./outcomes-band.module.css"

/** Two bars: the reading's own line, and the chart it stands beside. */
const OUTCOMES_SKELETON = ["34%", "100%"]

export interface OutcomesBandProps {
  /** The week, once it has landed. `undefined` while it has not. */
  days: OutcomeDay[] | undefined
  /** The week is still on its way. */
  loading?: boolean
  /** The week did not load, and is not coming without another try. */
  failed?: boolean
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
export function OutcomesBand({
  days,
  loading = false,
  failed = false,
  className,
}: OutcomesBandProps) {
  const { t } = useTranslation("home")

  /* Loading first, because "no history yet" and "the history has not arrived
     yet" are different answers and the band used to give the first one for
     both — it simply was not there until the query settled, which on a slow
     link reads as a swarm that has never finished anything. */
  if (loading) {
    return (
      <Skeleton
        className={className}
        lines={OUTCOMES_SKELETON}
        inset="none"
        label={t("outcomes.loading")}
        data-test="home-outcomes-loading"
      />
    )
  }

  if (failed) {
    /* Said in the band's own quiet voice and not as an alarm band. This is
       the screen's *second* question — history — and a red rule here would
       outrank the verdict above it, which is the one thing on this screen
       that is allowed to shout.

       The sentence is written here rather than taken from the error: the
       outcomes query's own failure text is "outcomes API not implemented —
       set VITE_USE_MOCK=true", which is a note to whoever is building this
       product and not an answer to whoever is running a shift on it. */
    return (
      <p className={cn(styles.absent, className)} data-test="home-outcomes-off">
        {t("outcomes.failed")}
      </p>
    )
  }

  const today = days?.[days.length - 1]

  if (!days || !today) {
    return null
  }

  const weekFailed = outcomeWindowTotal(days, "failed")

  const todayTotal = outcomeDayTotal(today)
  const weekTotal = days.reduce((sum, day) => sum + outcomeDayTotal(day), 0)

  return (
    <div className={cn(styles.band, className)} data-test="home-outcomes">
      <div className={styles.reading}>
        <p className={styles.figure}>
          <Trans
            ns="home"
            i18nKey="outcomes.reading"
            components={{
              today: <span className={styles.figureValue}>{todayTotal}</span>,
              week: <span className={styles.figureValue}>{weekTotal}</span>,
              failed: <span className={styles.figureValue}>{weekFailed}</span>,
            }}
          />
        </p>

        <ul className={styles.legend} aria-hidden="true">
          {OUTCOME_STATUSES.map((status) => (
            <li key={status} className={styles.key}>
              <span
                className={cn(styles.swatch, styles.status)}
                data-status={status}
              />
              {t(`outcomes.status.${status}`)}
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
        label={t("outcomes.chartLabel", {
          today: todayTotal,
          week: weekTotal,
          failed: weekFailed,
        })}
      />
    </div>
  )
}

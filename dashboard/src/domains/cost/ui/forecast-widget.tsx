import type { ReactNode } from "react"

import type { CostForecast, CostHeat } from "@/domains/cost/model/cost"
import { costHeat } from "@/domains/cost/model/cost"
import { cn } from "@/shared/lib/utils"

import styles from "./forecast-widget.module.css"

export interface ForecastWidgetProps {
  forecast: CostForecast
  /** Burn rate copy ("$148.2 / day", "5.2d into a 7d window") sits beside
   *  the figure to keep the screen from being a length alone. */
  burnRateLabel: string
  /** What the figure is — period-aware ("projected end of week"). */
  projectedLabel: string
  className?: string
  /** Optional metre drawn under the figure; rendered as the heat reading's
   *  bar against the cap, matching the proxy-budget-meter on the budget tile
   *  so the two tiles can never disagree about how loud a colour should be. */
  meter?: ReactNode
}

/**
 * The projected end-of-period spend, painted with the same three readings
 * as the budget tile.
 *
 * A forecast that lives behind a colour is a forecast the operator has been
 * trained to ignore by the time it crosses 90%, so the meter is a fallback —
 * the figure states the reading in words and the line under it says what
 * the figure means. Heat is `costHeat(forecast.share)`, the same three words
 * the budget tile uses, so a forecast reading and a budget reading at the
 * same cap share cannot be tinted apart.
 */
export function ForecastWidget({
  forecast,
  burnRateLabel,
  projectedLabel,
  className,
  meter,
}: ForecastWidgetProps) {
  const heat: CostHeat = costHeat(forecast.share)
  const pct = Math.round(forecast.share * 100)

  return (
    <article
      className={cn(styles.forecast, className)}
      data-test="forecast-widget"
      data-heat={heat}
    >
      <span className={styles.label}>Forecast {projectedLabel}</span>
      <span className={styles.figure}>
        <span className={styles.unit}>$</span>
        <span className={styles.value}>
          {forecast.projectedEndOfPeriod.toFixed(2)}
        </span>
      </span>
      {meter}
      <p className={styles.sub}>
        {pct}% of ${forecast.cap.toFixed(0)} cap · burn rate {burnRateLabel}
      </p>
    </article>
  )
}

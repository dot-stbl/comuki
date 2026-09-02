import type { CSSProperties } from "react"

import type { Status } from "./status-badge"
import { cn } from "@/shared/lib/utils"

import styles from "./bar-series.module.css"

/**
 * Bars on one shared vertical axis, drawn as SVG percentages.
 *
 * The product's first time-series primitive, and deliberately the last word in
 * small: a column per point, a rectangle per segment, an axis that is always
 * the largest total in the series — the same rule every shared axis in this
 * product follows, from the spend ranking to the profile river's channels. Two
 * bars on different scales cannot be compared, and comparing is the whole task.
 *
 * A chart never carries a reading alone. `label` is the reading in words and is
 * required, not optional: the component is `role="img"`, so that sentence is
 * everything a screen reader ever gets from it, and the figure drawn beside it
 * on the screen says the same thing to a sighted operator. A segment may carry
 * one of the six real statuses — hue only ever inside data, and always with the
 * words that name it standing next to the chart.
 */

export interface BarSeriesSegment {
  /** The quantity, in the series' own unit. Zero segments draw nothing. */
  value: number
  /**
   * One of the six real statuses. When set the bar takes that status' hue; the
   * words ride the figure and the legend beside the chart, never the hue alone.
   */
  status?: Status
}

export interface BarSeriesPoint {
  /** Stable key — React reconciliation and test handles. */
  key: string
  /** The short axis label under the bar ("mon", "today"). Drawn, not announced. */
  label: string
  /**
   * What the bar is made of, stacked from the baseline up in array order.
   * A single neutral segment is the plain reading; a status per segment is the
   * stacked one.
   */
  segments: BarSeriesSegment[]
}

export interface BarSeriesProps {
  points: BarSeriesPoint[]
  /**
   * The reading in words. Required, because the chart is never the only
   * carrier of its own reading — this sentence is the whole accessible name.
   */
  label: string
  className?: string
  "data-test"?: string
}

/** The smallest drawn share of the axis, in percent. A value present is visible. */
const MIN_SHARE = 2

/** A week of columns drawn at a hundred units wide leaves room for real labels. */
const BAR_SHARE_OF_SLOT = 0.6

function round2(value: number): number {
  return Math.round(value * 100) / 100
}

function totalOf(point: BarSeriesPoint): number {
  return point.segments.reduce(
    (sum, segment) => sum + Math.max(0, segment.value),
    0
  )
}

/**
 * The axis every bar is measured against: the largest total in the series.
 *
 * Floored at one, which is the spend ranking's rule rather than arithmetic
 * hygiene — a week where almost nothing ran must not draw seven full-length
 * bars over seven cents.
 */
export function barSeriesAxis(points: BarSeriesPoint[]): number {
  return Math.max(1, ...points.map(totalOf))
}

export function BarSeries({
  points,
  label,
  className,
  "data-test": dataTest = "bar-series",
}: BarSeriesProps) {
  const axis = barSeriesAxis(points)
  const slot = points.length > 0 ? 100 / points.length : 0
  const barWidth = round2(slot * BAR_SHARE_OF_SLOT)

  return (
    <div
      className={cn(styles.series, className)}
      data-test={dataTest}
      role="img"
      aria-label={label}
    >
      {/* Geometry in percentages of the viewport: no viewBox, so the bars keep
          exact px corners (`rx` from the token) at any width the band gives
          them, and a test can read a bar's height straight off the attribute. */}
      <svg
        className={styles.plot}
        data-test="bar-series-plot"
        aria-hidden="true"
        focusable="false"
      >
        {points.map((point, index) => {
          const x = round2(slot * index + slot * (1 - BAR_SHARE_OF_SLOT) / 2)
          let stacked = 0
          return point.segments.map((segment, segmentIndex) => {
            if (segment.value <= 0) {
              return null
            }
            const share = round2(
              Math.min(100, Math.max(MIN_SHARE, (segment.value / axis) * 100))
            )
            const y = round2(100 - stacked - share)
            stacked += share
            return (
              <rect
                key={`${point.key}:${segmentIndex}`}
                className={segment.status ? styles.seg : styles.bar}
                data-test="bar-series-bar"
                data-key={point.key}
                data-status={segment.status}
                x={`${x}%`}
                y={`${y}%`}
                width={`${barWidth}%`}
                height={`${share}%`}
                style={{ "--index": index } as CSSProperties}
              />
            )
          })
        })}
      </svg>

      <div className={styles.axis} data-test="bar-series-axis" aria-hidden="true">
        {points.map((point) => (
          <span
            key={point.key}
            className={styles.tick}
            data-test="bar-series-tick"
            data-key={point.key}
          >
            {point.label}
          </span>
        ))}
      </div>
    </div>
  )
}

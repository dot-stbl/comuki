import { cn } from "@/shared/lib/utils"

import styles from "./sparkline.module.css"

/**
 * A line, and nothing else — the shape of a series riding beside figures that
 * already state the reading in words.
 *
 * The proxy panel's burn figure says the dollars and the window; the sparkline
 * confirms in shape what those numbers say ("steady morning, heavy afternoon").
 * That division of labour is why it draws no axes, no ticks and no grid: any of
 * those would invite the reading to live in the picture, and a picture that
 * carries a reading alone is the one defect this product's charts exist
 * without.
 *
 * The scale is zero-to-max rather than min-max normalised. A sparkline that
 * auto-ranges exaggerates a quiet day into a mountain range, and burn is a
 * quantity that honestly starts at zero.
 */

export interface SparklineProps {
  /**
   * The series, oldest first. One and two points draw no line — a single value
   * has no shape worth confirming.
   */
  values: number[]
  /**
   * The reading in words. Required: the element is `role="img"`, so this
   * sentence is everything a screen reader gets, and it is the same sentence
   * the figures beside it are already saying.
   */
  label: string
  className?: string
  "data-test"?: string
}

/** Breathing room at the top and bottom of the plot, in viewBox units. */
const PAD = 4

function round2(value: number): number {
  return Math.round(value * 100) / 100
}

export function Sparkline({
  values,
  label,
  className,
  "data-test": dataTest = "sparkline",
}: SparklineProps) {
  const max = values.reduce((found, value) => Math.max(found, value), 0)
  const drawn =
    values.length > 1 && max > 0
      ? values
          .map((value, index) => {
            const x = round2((index / (values.length - 1)) * 100)
            const y = round2(100 - PAD - (value / max) * (100 - PAD * 2))
            return `${x},${y}`
          })
          .join(" ")
      : null

  return (
    <svg
      className={cn(styles.spark, className)}
      data-test={dataTest}
      role="img"
      aria-label={label}
      viewBox="0 0 100 100"
      preserveAspectRatio="none"
      focusable="false"
    >
      {drawn ? (
        <polyline
          className={styles.line}
          data-test="sparkline-line"
          points={drawn}
          vectorEffect="non-scaling-stroke"
        />
      ) : null}
    </svg>
  )
}

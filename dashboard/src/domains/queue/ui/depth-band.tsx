import { depthReadings } from "@/domains/queue/model/queue"
import type { QueueDepthDay } from "@/domains/queue/model/types"
import { cn } from "@/shared/lib/utils"
import { BarSeries } from "@/shared/ui"

import styles from "./depth-band.module.css"

export interface DepthBandProps {
  days: QueueDepthDay[]
  className?: string
}

/**
 * Queue depth over the week, beside the sentence that says it.
 *
 * The header above says how many items are waiting *now*; this band says
 * whether that number is business as usual or a direction — the one question a
 * snapshot cannot answer. It sits above the split rather than inside either
 * half, because depth is the mechanism's reading rather than the queue's or
 * the pool's: a backlog accuses the pool, an idle pool excuses the backlog,
 * and the trend is what they are doing to each other.
 *
 * Neutral chrome, no hue: depth is a quantity, not a status, and the words
 * beside it already name the one day that deserves a second look.
 */
export function DepthBand({ days, className }: DepthBandProps) {
  const readings = depthReadings(days)

  if (readings === null) {
    return null
  }

  const deepest = readings.todayIsDeepest
    ? " · deepest of the week today"
    : ""

  return (
    <section
      className={cn(styles.band, className)}
      data-test="queue-depth"
      aria-label="Queue depth by day"
    >
      <p className={styles.figure}>
        <span className={styles.figureValue}>{readings.today}</span> queued now
        · the week ran{" "}
        <span className={styles.figureValue}>
          {readings.weekMin}–{readings.weekMax}
        </span>
        {deepest}
      </p>

      <BarSeries
        className={styles.chart}
        points={days.map((day) => ({
          key: day.label,
          label: day.label,
          segments: [{ value: day.depth }],
        }))}
        label={
          readings.todayIsDeepest
            ? `Queue depth by day, items waiting for a claim. ${readings.today} queued today — the deepest day of a week that ran ${readings.weekMin} to ${readings.weekMax}.`
            : `Queue depth by day, items waiting for a claim. ${readings.today} queued today; the week ran ${readings.weekMin} to ${readings.weekMax}.`
        }
      />
    </section>
  )
}

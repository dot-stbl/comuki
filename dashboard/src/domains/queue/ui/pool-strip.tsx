import { cn } from "@/shared/lib/utils"

import type { WorkerCounts } from "@/domains/queue/model/queue"

import styles from "./pool-strip.module.css"

export interface PoolStripProps {
  counts: WorkerCounts
  onExpand: () => void
  className?: string
}

/**
 * The pool collapsed: one row tall, the same capacity as a shape.
 *
 * Roughly what the flow board's collapsed strip is to the flow — the reading
 * goes and the proportion stays. Busy, draining and idle are three segments of
 * one track, so a glance says whether the swarm is saturated without asking
 * anyone to read a number at 10px. The counts survive in the accessible name
 * rather than on screen, because a strip that carries figures is not a strip.
 *
 * The strip is itself the control that brings the pool back, so the shape is
 * never a dead end for a pointer or for a keyboard.
 */
export function PoolStrip({ counts, onExpand, className }: PoolStripProps) {
  const { total, busy, draining, idle } = counts
  const share = (value: number) => (total === 0 ? 0 : (value / total) * 100)

  return (
    <button
      type="button"
      data-test="pool-strip"
      className={cn(styles.strip, className)}
      aria-expanded={false}
      aria-label={
        total === 0
          ? "Expand the pool. No workers are up."
          : `Expand the pool. ${total} workers: ${busy} busy, ${draining} draining, ${idle} idle.`
      }
      onClick={onExpand}
    >
      <span className={styles.track} aria-hidden="true">
        {total === 0 ? null : (
          <>
            <span
              className={cn(styles.segment, styles.busy)}
              style={{ inlineSize: `${share(busy)}%` }}
            />
            <span
              className={cn(styles.segment, styles.draining)}
              style={{ inlineSize: `${share(draining)}%` }}
            />
            <span
              className={cn(styles.segment, styles.idle)}
              style={{ inlineSize: `${share(idle)}%` }}
            />
          </>
        )}
      </span>
    </button>
  )
}

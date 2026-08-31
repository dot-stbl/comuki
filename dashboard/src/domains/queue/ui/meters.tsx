import { TriangleAlert } from "lucide-react"

import { formatDuration } from "@/domains/runs/model/format"
import { cn } from "@/shared/lib/utils"

import {
  ageHeat,
  ageShare,
  AGE_STALLED_SEC,
  leaseHeat,
  lostHeartbeatSentence,
} from "@/domains/queue/model/queue"
import type { QueueItem, Worker } from "@/domains/queue/model/types"

import styles from "./meters.module.css"

/**
 * The two clocks this screen is an instrument for.
 *
 * Both are set with `formatDuration` — the duty list's spelling of an elapsed
 * time — because a second spelling of a duration inside one product is a
 * second thing to learn for no reading gained.
 */

export interface AgeMeterProps {
  item: QueueItem
  className?: string
}

/**
 * How long this item has been in its status, and — for the only status where
 * the number accuses anyone — how far along that wait is.
 *
 * A queued row draws a hairline bar under its figure, growing from the start
 * edge and full at the point where waiting stops meaning "busy" and starts
 * meaning "no worker runs this profile". Length is the primary channel and hue
 * is the second, so the column still reads down the page in greyscale, and a
 * row nobody needs to look at draws no bar at all.
 */
export function AgeMeter({ item, className }: AgeMeterProps) {
  const heat = ageHeat(item)
  const share = ageShare(item)

  return (
    <span
      data-test="age-meter"
      data-heat={heat}
      className={cn(styles.age, className)}
      title={
        heat === "stalled"
          ? `queued longer than ${formatDuration(AGE_STALLED_SEC)} — no worker on this profile has claimed it`
          : undefined
      }
    >
      {heat === "none" ? null : (
        <span
          className={styles.track}
          style={{ inlineSize: `${Math.round(share * 100)}%` }}
        />
      )}
      <span className={styles.figure}>{formatDuration(item.ageSec)}</span>
    </span>
  )
}

export interface LeaseMeterProps {
  worker: Worker
  className?: string
}

/**
 * What is left of the worker's lease, and whether it is still being defended.
 *
 * A worker heartbeats against the lease it holds. One that stopped is holding
 * an item hostage until the lease lapses, and that is the pool's half of the
 * same failure the queue's age column shows — so it is marked, and it says how
 * long the silence has been rather than only that there is one.
 */
export function LeaseMeter({ worker, className }: LeaseMeterProps) {
  const heat = leaseHeat(worker)

  if (worker.leaseSec === null) {
    return (
      <span className={cn(styles.none, className)} data-test="lease-meter">
        —
      </span>
    )
  }

  return (
    <span
      data-test="lease-meter"
      data-heat={heat}
      className={cn(styles.lease, className)}
      // The sentence itself lives in the model beside the threshold that
      // decides when it is true — the worker's own page says it out loud, and
      // one consequence must not be promised in two different wordings.
      title={heat === "lost" ? lostHeartbeatSentence(worker) : undefined}
    >
      {heat === "lost" ? (
        <TriangleAlert className={styles.icon} aria-hidden="true" />
      ) : null}
      <span className={styles.figure}>{formatDuration(worker.leaseSec)}</span>
    </span>
  )
}

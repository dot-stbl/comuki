import { formatDuration } from "@/domains/runs/model/format"

import type {
  QueueItem,
  WorkItemStatus,
  Worker,
  WorkerPool,
  WorkerState,
} from "./types"

/**
 * The queue's readings — the small pile of arithmetic that turns two lists
 * into an instrument. Kept out of the components so the thresholds have one
 * spelling, and so the numbers can be asserted without a DOM.
 */

/** Every work-item status, in the order the toolbar offers them. */
export const WORK_ITEM_STATUSES: WorkItemStatus[] = [
  "queued",
  "running",
  "blocked",
  "failed",
  "cancelled",
  "succeeded",
]

/** Every worker state, in the order the toolbar offers them. */
export const WORKER_STATES: WorkerState[] = ["busy", "idle", "draining"]

/* --------------------------------------------------------------------------
 * Age
 *
 * A work item's age is the number that makes this screen worth opening.
 * Queued for eight seconds is the system working; queued for eleven minutes
 * while workers sit idle means no profile matches the item and nobody will
 * ever claim it.
 *
 * Which is why heat is read from the status as well as the clock. A *blocked*
 * item can be hours old and perfectly healthy — it is waiting on its own
 * dependencies, not on the pool — and a running item's age is answered by its
 * worker's lease, on the other half of the screen. Only `queued` age accuses
 * anyone, so only `queued` gets heat.
 * ------------------------------------------------------------------------ */

/** A queued item this old is no longer instant, but nothing is wrong yet. */
export const AGE_WARM_SEC = 60

/** Past this, a queued item is not waiting for a worker — it is unmatched. */
export const AGE_STALLED_SEC = 300

export type AgeHeat = "none" | "fresh" | "warm" | "stalled"

export function ageHeat(item: Pick<QueueItem, "status" | "ageSec">): AgeHeat {
  if (item.status !== "queued") {
    return "none"
  }
  if (item.ageSec >= AGE_STALLED_SEC) {
    return "stalled"
  }
  if (item.ageSec >= AGE_WARM_SEC) {
    return "warm"
  }
  return "fresh"
}

/**
 * How far along its wait a queued item is, `0..1`, for the track drawn beside
 * the figure. Length is the primary channel and hue is the second: the reading
 * survives greyscale, which is the rule every status in this product follows.
 */
export function ageShare(item: Pick<QueueItem, "status" | "ageSec">): number {
  if (item.status !== "queued") {
    return 0
  }
  return Math.min(1, Math.max(0, item.ageSec / AGE_STALLED_SEC))
}

/**
 * Triage order, and the reason the list opens on something useful.
 *
 * Unclaimed work first, oldest at the top — that is the failure this screen
 * catches. Then the failures a person owes a decision on, then work in flight,
 * then items blocked on their own run, then the settled ones. Age descends
 * inside every band, so the same rule reads the same way all the way down.
 *
 * Like the duty list, this composes with the table's own sorting rather than
 * competing with it: the table sorts what it is given and breaks ties on the
 * incoming index, so an explicit sort is the primary key and this is the
 * tiebreak beneath it.
 */
export const QUEUE_RANK: Record<WorkItemStatus, number> = {
  queued: 0,
  failed: 1,
  running: 2,
  blocked: 3,
  cancelled: 4,
  succeeded: 5,
}

export function compareQueueItems(a: QueueItem, b: QueueItem): number {
  const rank = QUEUE_RANK[a.status] - QUEUE_RANK[b.status]
  if (rank !== 0) {
    return rank
  }
  if (a.ageSec !== b.ageSec) {
    return b.ageSec - a.ageSec
  }
  // Ids break the last tie so two runs of the same list are the same list.
  return a.id.localeCompare(b.id)
}

export function queueOrder(items: QueueItem[]): QueueItem[] {
  return [...items].sort(compareQueueItems)
}

/** Items nobody has claimed yet. */
export function backlogOf(items: QueueItem[]): number {
  return items.filter((item) => item.status === "queued").length
}

/** Queued items that have waited longer than `seconds` — the alarming count. */
export function unclaimedOver(items: QueueItem[], seconds: number): number {
  return items.filter(
    (item) => item.status === "queued" && item.ageSec >= seconds
  ).length
}

/* --------------------------------------------------------------------------
 * Leases
 *
 * The other half of the same failure. A worker claims an item, takes a lease
 * and heartbeats against it; a worker that holds a lease and stopped
 * heartbeating is holding work hostage until the lease lapses. That is not a
 * status — the orchestrator will turn it into a failure or a requeue — so the
 * screen reads it off the two clocks instead.
 * ------------------------------------------------------------------------ */

/** No heartbeat for this long, with a lease held, and the worker is gone. */
export const HEARTBEAT_STALE_SEC = 45

/** A lease with less than this left is about to be handed back. */
export const LEASE_EXPIRING_SEC = 30

export type LeaseHeat = "none" | "expiring" | "lost"

export function leaseHeat(
  worker: Pick<Worker, "leaseSec" | "heartbeatAgeSec">
): LeaseHeat {
  if (worker.leaseSec === null) {
    return "none"
  }
  if (worker.heartbeatAgeSec >= HEARTBEAT_STALE_SEC) {
    return "lost"
  }
  if (worker.leaseSec <= LEASE_EXPIRING_SEC) {
    return "expiring"
  }
  return "none"
}

/**
 * What a lost heartbeat *means*, in one sentence — and the one spelling of it.
 *
 * The pool's lease column says it in a tooltip; the worker's own page says it
 * out loud, because on a page about one container there is room to say it and
 * a person who came looking is owed the consequence rather than the reading.
 * Two places, so the words live here beside the threshold that decides when
 * they are true. A second wording would be a second promise about what the
 * orchestrator is going to do.
 */
export function lostHeartbeatSentence(
  worker: Pick<Worker, "heartbeatAgeSec">
): string {
  return `no heartbeat for ${formatDuration(worker.heartbeatAgeSec)} — the lease lapses and the item is requeued`
}

export function lostLeases(workers: Worker[]): number {
  return workers.filter((worker) => leaseHeat(worker) === "lost").length
}

export interface WorkerCounts {
  total: number
  idle: number
  busy: number
  draining: number
}

export function workerCounts(workers: Worker[]): WorkerCounts {
  return {
    total: workers.length,
    idle: workers.filter((worker) => worker.state === "idle").length,
    busy: workers.filter((worker) => worker.state === "busy").length,
    draining: workers.filter((worker) => worker.state === "draining").length,
  }
}

/* --------------------------------------------------------------------------
 * The empty pool
 *
 * An empty worker pool is usually correct. `minIdle: 0` is create-per-task,
 * which is the configured resting state for most projects, and a pool that is
 * empty *while there is a backlog* is a pool the scaler is about to fill.
 * Neither is an outage, and a screen that shows the same blank band for both —
 * and for the third case, where the pool really is under its target — teaches
 * the operator to distrust it.
 * ------------------------------------------------------------------------ */

export type WorkerEmptyKind = "filtered" | "backlog" | "at-rest" | "under-target"

export interface WorkerEmptyInput {
  /**
   * Workers in the pool being asked about: the project filter applied and
   * nothing else. Non-zero means the pool is up and the *other* filters are
   * what emptied the table.
   */
  poolSize: number
  /** Idle workers this slice is configured to keep. */
  minIdle: number
  /** Queued, unclaimed items in the same slice the filters describe. */
  backlog: number
}

/**
 * Which of the four empty states this is. Order matters: a pool below its own
 * `minIdle` is wrong whether or not there is a backlog, so it is answered
 * before the reassuring cases get a chance to swallow it.
 */
export function resolveWorkerEmpty({
  poolSize,
  minIdle,
  backlog,
}: WorkerEmptyInput): WorkerEmptyKind {
  if (poolSize > 0) {
    return "filtered"
  }
  if (minIdle > 0) {
    return "under-target"
  }
  if (backlog > 0) {
    return "backlog"
  }
  return "at-rest"
}

/**
 * The idle target for a slice. One project answers with its own pool; the
 * unfiltered board answers with every pool it can see, because that is the
 * number of containers that ought to be up across the screen.
 */
export function minIdleFor(pools: WorkerPool[], projectId: string): number {
  if (projectId) {
    return pools.find((pool) => pool.projectId === projectId)?.minIdle ?? 0
  }
  return pools.reduce((total, pool) => total + pool.minIdle, 0)
}

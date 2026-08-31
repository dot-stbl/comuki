/**
 * The two halves of the runtime, as the screen sees them.
 *
 * A work item and a worker are one mechanism from its two ends: the
 * orchestrator queues items, a free worker claims one *by profile*, takes a
 * lease and heartbeats. `profile` is therefore the matching axis on both
 * records and the only thing they agree on by name — which is why the screen
 * filters both halves by it.
 */

/**
 * Statuses a work item can rest in, from the v1 scope draft.
 *
 * There is no `stalled`, and its absence is a design decision rather than an
 * omission: a lease that lapses without a heartbeat is an *event*, and the
 * orchestrator turns it into `failed` or requeues the item. A `stalled` status
 * would be a resting state nothing can leave and nobody can act on.
 */
export type WorkItemStatus =
  | "blocked"
  | "queued"
  | "running"
  | "succeeded"
  | "failed"
  | "cancelled"

/** Idle, holding one item, or finishing up and refusing new claims. */
export type WorkerState = "idle" | "busy" | "draining"

export type ComputeProvider = "docker" | "kubernetes"

export interface QueueItem {
  id: string
  /** The run this item belongs to — the row's link out of this screen. */
  runId: string
  /** The project that answers for this row, including for its permissions. */
  projectId: string
  /** Catalog key of the profile that may claim it. The matching axis. */
  profile: string
  /** The brain's own name for the step. Prose, never a key. */
  label: string
  status: WorkItemStatus
  /**
   * Seconds in the current status.
   *
   * On a queued row this is how long the item has gone unclaimed, and it is
   * the number this screen exists to make readable at a glance.
   */
  ageSec: number
  /** The worker holding the lease. Non-null exactly while `running`. */
  claimedBy: string | null
  /** Items in the same run this one waits on. Only `blocked` has any. */
  blockedOn: string[]
}

export interface Worker {
  id: string
  /** The pool it was raised in. Gates the admin acts on its row. */
  projectId: string
  profile: string
  state: WorkerState
  /** The item it holds a lease on; `null` while idle. */
  itemId: string | null
  provider: ComputeProvider
  /** The provider's own handle for the container. A value, not prose. */
  handle: string
  /** Seconds since the last heartbeat landed. */
  heartbeatAgeSec: number
  /** Seconds until the lease expires; `null` when it holds none. */
  leaseSec: number | null
  /** Seconds since the container came up. */
  upSec: number
  /** Short image digest — the label a claim is matched against. */
  digest: string
}

/**
 * Scale knobs a project turns. The core owns pool and scale; the project sets
 * how many workers idle. `minIdle: 0` is create-per-task, and it is the reason
 * an empty pool is usually correct rather than broken.
 */
export interface WorkerPool {
  projectId: string
  /**
   * How many idle workers the pool keeps at rest, and the ceiling on idle —
   * *not* a ceiling on workers. The platform scales against a quota and the
   * provider's allocatable; these two only decide how much warm capacity is
   * worth paying for between tasks. Zero means create-per-task.
   *
   * The Compute registry shows the same two knobs for the same pools, so the
   * names and the scale have to agree with the compute seed.
   */
  minIdle: number
  maxIdle: number
}

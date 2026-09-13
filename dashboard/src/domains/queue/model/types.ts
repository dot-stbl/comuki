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

/**
 * Idle, holding one item, or finishing up and refusing new claims — plus
 * `offline`, the host's reading for a lease whose heartbeat went stale: the
 * worker still holds the item but is a reaper candidate, which is a different
 * sentence from `busy` and must not wear its pulse. The wire derives it
 * (`GET /api/v1/workers`); the mock store never mints it because its seed
 * heartbeats never go stale without the whole row being reaped.
 */
export type WorkerState = "idle" | "busy" | "draining" | "offline"

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
  /**
   * The pool it was raised in. Gates the admin acts on its row.
   *
   * `null` on a wire row the host could not attribute — an idle worker whose
   * last claim outlived its run's visibility. The project column and the
   * permission checks both read the absence rather than inventing a project.
   */
  projectId: string | null
  profile: string | null
  state: WorkerState
  /** The item it holds a lease on; `null` while idle. */
  itemId: string | null
  /** The run the held item belongs to; absent while idle or when the source
   *  (the mock seed) has no run axis to offer. */
  runId?: string | null
  /**
   * The compute provider the container runs on. `null` is the honest reading
   * for a host that composes no compute engine and therefore cannot say —
   * the workers API derives rows from leases, not from a provider registry.
   */
  provider: ComputeProvider | null
  /**
   * The provider's own handle for the container. A value, not prose — and
   * `null` when no provider is composed to mint one.
   */
  handle: string | null
  /**
   * Seconds since the last heartbeat landed. `null` while idle — an idle
   * worker heartbeats against nothing, so there is no age to read.
   */
  heartbeatAgeSec: number | null
  /** Seconds until the lease expires; `null` when it holds none. */
  leaseSec: number | null
  /**
   * Seconds since the container came up. `null` when the wire cannot know —
   * uptime is a container-runtime fact, and the derived registry has one.
   */
  upSec: number | null
  /**
   * Short image digest — the label a claim is matched against. `null` while
   * idle (the lease carries the image, and an idle worker holds no lease).
   */
  digest: string | null
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

/**
 * One day of the depth series: how many work items were waiting for a claim.
 *
 * The same thing the header's count says about now, said about a day — so the
 * band and the header are one reading, and the series is what turns "14
 * queued" from a number into a direction.
 */
export interface QueueDepthDay {
  /** The day's short label ("mon", "today"). */
  label: string
  /** Items waiting for a claim that day. */
  depth: number
}

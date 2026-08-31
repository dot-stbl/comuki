import type {
  ComputePool,
  ComputeProvider,
  Constraint,
  WorkerVersion,
} from "./types"

/**
 * The one arithmetic this screen exists to spare an operator.
 *
 * v1 scaling is quota-aware *plus* the provider's capacity API, which means two
 * independent ceilings and two different owners: the project's quota, which a
 * project-admin raises, and the cluster's allocatable, which somebody buys
 * nodes for. A scale-up stops at whichever runs out first, and which one that
 * is changes hour to hour. Asking an operator to subtract two pairs of numbers
 * and compare the results — while they are already looking for why nothing
 * started — is the failure this module is written against.
 */

/** Slots still available under one ceiling. Never negative. */
export function headroom(constraint: Constraint): number {
  return Math.max(0, constraint.limit - constraint.used)
}

/** How full a ceiling is, 0–1. A limit of zero reads as full, not as free. */
export function share(constraint: Constraint): number {
  if (constraint.limit <= 0) {
    return 1
  }
  return Math.min(1, Math.max(0, constraint.used / constraint.limit))
}

/**
 * Which ceiling is actually stopping the next container.
 *
 * `both` is not a rounding artefact — two ceilings with equal headroom are
 * genuinely both binding, and saying "quota" there would send an operator to
 * raise a quota that buys them nothing. `unknown` is the provider whose
 * capacity API did not answer: there is no second number to compare, so the
 * screen must not pretend the quota is the answer.
 */
export type Binding = "quota" | "capacity" | "both" | "unknown"

export interface CapacityReading {
  /** The project's ceiling on concurrent workers. */
  quota: Constraint
  /** The provider's placeable slots, or `null` when it did not answer. */
  capacity: Constraint | null
  binding: Binding
  /**
   * How many more workers this pool can actually start — the smaller headroom,
   * or `null` when the capacity side is unknown and the number would be a
   * guess dressed as a reading.
   */
  room: number | null
  /** Headroom on each side, so the screen can show what the loser had spare. */
  quotaRoom: number
  capacityRoom: number | null
}

/**
 * Read one pool against its provider.
 *
 * The pair is the unit, not the pool: a pool's quota headroom means nothing
 * until you know what the cluster under it has left, and that is exactly why
 * the two live on different records and get joined here rather than being
 * denormalised into one row that could drift.
 */
export function readCapacity(
  pool: ComputePool,
  provider: ComputeProvider | undefined
): CapacityReading {
  const quotaRoom = headroom(pool.quota)
  const capacity = provider?.allocatable ?? null

  if (!capacity) {
    return {
      quota: pool.quota,
      capacity: null,
      binding: "unknown",
      room: null,
      quotaRoom,
      capacityRoom: null,
    }
  }

  const capacityRoom = headroom(capacity)
  const binding: Binding =
    quotaRoom === capacityRoom
      ? "both"
      : quotaRoom < capacityRoom
        ? "quota"
        : "capacity"

  return {
    quota: pool.quota,
    capacity,
    binding,
    room: Math.min(quotaRoom, capacityRoom),
    quotaRoom,
    capacityRoom,
  }
}

/**
 * The sentence the reading is for, written once so the card, the summary line
 * and the test all say the same thing.
 *
 * It names the winner *and* what the other side had spare, because "the quota
 * binds" alone still leaves the operator to go and check whether buying nodes
 * would have helped. Saying "65 cluster slots free" in the same breath closes
 * the question.
 */
export function bindingSentence(reading: CapacityReading): string {
  const slots = (n: number) => `${n} ${n === 1 ? "slot" : "slots"}`

  switch (reading.binding) {
    case "unknown":
      return "capacity api did not answer — no ceiling can be read for this pool"
    case "both":
      return reading.room === 0
        ? "quota and cluster are both full — nothing can start here"
        : `quota and cluster agree — room for ${slots(reading.room ?? 0)}`
    case "quota":
      return reading.room === 0
        ? `quota is the ceiling — nothing can start, and the cluster still has ${slots(reading.capacityRoom ?? 0)} free`
        : `quota is the ceiling — room for ${slots(reading.room ?? 0)}, with ${slots(reading.capacityRoom ?? 0)} free on the cluster`
    case "capacity":
      return reading.room === 0
        ? `the cluster is the ceiling — nothing can start, and the quota still allows ${slots(reading.quotaRoom)}`
        : `the cluster is the ceiling — room for ${slots(reading.room ?? 0)}, with ${slots(reading.quotaRoom)} left under the quota`
  }
}

/**
 * How a pool's `minIdle` should read.
 *
 * `0` is the case this exists for. An unset field and a deliberate
 * create-per-task pool look identical in a container listing — both are zero
 * containers — and only one of them is a fault. The pool that is *configured*
 * to sit empty says so in its own words, so an empty row stops looking like an
 * outage.
 */
export function idleReading(pool: ComputePool): string {
  if (pool.minIdle === 0) {
    return "min idle 0 — create-per-task"
  }
  return `min idle ${pool.minIdle} · max idle ${pool.maxIdle}`
}

/** True when the pool keeps no warm containers at all, by configuration. */
export function isCreatePerTask(pool: ComputePool): boolean {
  return pool.minIdle === 0
}

/**
 * The full label a claim is matched against: both halves, always together.
 *
 * Written once because the whole trap this screen defuses is that the halves
 * look separable. A container on the target image with a moved profiles ref is
 * as unmatchable as one a release behind, and it takes both values side by side
 * to see that.
 */
export function versionLabel(version: WorkerVersion): string {
  return `${version.digest} · ${version.profilesRef}`
}

/** A label that is not the one new starts use. */
export function isStale(version: WorkerVersion): boolean {
  return !version.target
}

/**
 * Idle containers that can never be matched to an item.
 *
 * The number the screen is an instrument for: it is what makes an idle pool and
 * a growing queue coexist, and it is invisible in every other view of the
 * system, because a stranded worker looks exactly like a healthy idle one.
 */
export function strandedIdle(versions: WorkerVersion[]): number {
  return versions
    .filter(isStale)
    .reduce((total, version) => total + version.idle, 0)
}

/** Which half of the label moved — the reason a row is stale. */
export function staleReason(
  version: WorkerVersion,
  target: WorkerVersion | undefined
): string | null {
  if (!target || version.target) {
    return null
  }
  const imageMoved = version.digest !== target.digest
  const profilesMoved = version.profilesRef !== target.profilesRef

  if (imageMoved && profilesMoved) {
    return "image and profiles ref both moved"
  }
  if (imageMoved) {
    return "a release behind on the image"
  }
  if (profilesMoved) {
    return "same image, the profiles ref moved"
  }
  return null
}

/** The label new starts use. There is exactly one; `undefined` while loading. */
export function targetVersion(
  versions: WorkerVersion[]
): WorkerVersion | undefined {
  return versions.find((version) => version.target)
}

/** Pools listed with the tightest first — the one about to refuse work. */
export function bindingFirst(
  pools: ComputePool[],
  providers: ComputeProvider[]
): ComputePool[] {
  const roomOf = (pool: ComputePool) => {
    const reading = readCapacity(
      pool,
      providers.find((provider) => provider.id === pool.providerId)
    )
    // An unreadable ceiling sorts last: it is a provider problem, not a pool
    // that is about to run out.
    return reading.room ?? Number.MAX_SAFE_INTEGER
  }
  return [...pools].sort((a, b) => roomOf(a) - roomOf(b))
}

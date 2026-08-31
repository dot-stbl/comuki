/**
 * The compute registry as the screen sees it.
 *
 * Three records, and they are three because the scaling rule needs all three at
 * once. A **provider** answers for the machines and for the capacity API. A
 * **pool** is a project's workers on one provider, and it carries the project's
 * quota and the knobs the project turns. A **worker version** is the label a
 * container was started with — image digest plus profiles git-ref — and it is a
 * record of its own rather than a column on a worker, because the question this
 * screen asks about labels is never "what is this container running", it is
 * "how much of the pool can still be matched to an item".
 */

/** The `IComputeProvider` implementations that exist in v1.0. containerd later. */
export type ProviderKind = "docker" | "kubernetes"

/**
 * Where a provider stands with respect to new starts. Not a run status and
 * deliberately not spelled like one — a provider is not queued or escalated.
 */
export type ProviderState = "active" | "standby" | "draining" | "unreachable"

/** One side of the scaling decision: a limit, what is spent, and who says so. */
export interface Constraint {
  used: number
  limit: number
  /** The authority behind the limit — a project quota, or the capacity api. */
  source: string
}

export interface ComputeProvider {
  id: string
  kind: ProviderKind
  /** What the orchestrator dials. A value, set in the data voice. */
  endpoint: string
  state: ProviderState
  /** The one taking new starts. Exactly one provider is. */
  takingWork: boolean
  /**
   * Slots the provider's capacity API says are placeable, or `null` when it did
   * not answer. `null` and `0` are different readings and the screen says which
   * — a full cluster refuses work for a reason an operator can act on, and a
   * silent capacity API refuses it for one they cannot.
   */
  allocatable: Constraint | null
  note: string
}

export interface ComputePool {
  projectId: string
  providerId: string
  /**
   * Containers kept warm with no lease. `0` is create-per-task: a real setting,
   * and the reason an empty pool is usually resting rather than broken.
   */
  minIdle: number
  maxIdle: number
  workers: number
  idle: number
  quota: Constraint
  profiles: string[]
}

export interface WorkerVersion {
  /** Image digest — half the label a claim is matched against. */
  digest: string
  /** Pinned profiles git-ref — the other half. */
  profilesRef: string
  /** The label a new `Start` uses today. */
  target: boolean
  workers: number
  idle: number
  oldestUpSec: number
  providerIds: string[]
}

export interface ComputeSnapshot {
  providers: ComputeProvider[]
  pools: ComputePool[]
  versions: WorkerVersion[]
}

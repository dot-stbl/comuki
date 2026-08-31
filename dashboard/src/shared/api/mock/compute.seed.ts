import type { SeedProfile } from "./runs.seed"

/**
 * The compute registry — where containers actually run.
 *
 * Fictional, like every other seed in this folder: no endpoint, digest, quota
 * or allocatable figure below came from a real cluster. The numbers are chosen
 * to make the *interesting* states reachable without a backend, not to look
 * like an average afternoon.
 *
 * The shape follows the v1 scope draft (§4 Runtime → Compute):
 *
 *   - `IComputeProvider` has exactly two implementations in v1.0, Docker for
 *     dev/compose and Kubernetes for prod. containerd is later, so it does not
 *     appear here — a seed that shows a provider the product cannot start is a
 *     seed that teaches the screen a row it will never receive.
 *   - Scale is *quota-aware plus the provider's capacity API*: the project's
 *     quota and the cluster's allocatable, both real limits, either of which
 *     can be the one actually stopping a scale-up.
 *   - A worker is labelled by **image digest + profiles git-ref**. Changing
 *     either only affects a new `Start`; an idle worker on a different label is
 *     never matched to an item. That is the whole reason a stale idle pool can
 *     sit next to a growing queue.
 *
 * The figures are kept internally consistent rather than sampled independently,
 * and `compute.seed.test.ts` in the domain asserts it: pool workers sum to the
 * provider's allocatable `used`, and to the version rollout's worker counts.
 * A mock that drifts out of those sums teaches the screen an arithmetic the
 * product cannot produce.
 */

/** The `IComputeProvider` implementations that exist in v1.0. */
export const COMPUTE_PROVIDER_KINDS = ["docker", "kubernetes"] as const

export type SeedComputeProviderKind = (typeof COMPUTE_PROVIDER_KINDS)[number]

/**
 * Where a provider stands with respect to work.
 *
 * `active` is the one taking new starts; `standby` is configured, reachable and
 * idle; `draining` still holds leases but refuses new starts; `unreachable` is
 * a provider whose control plane did not answer, which matters because its
 * capacity API is the source of the allocatable figure — an unreachable
 * provider has no capacity reading at all, not a capacity reading of zero.
 */
export const PROVIDER_STATES = [
  "active",
  "standby",
  "draining",
  "unreachable",
] as const

export type SeedProviderState = (typeof PROVIDER_STATES)[number]

/** Project ids — the same three the session seed hands out roles on. */
export const COMPUTE_PROJECT_IDS = ["p_comuki", "p_plexor", "p_atlas"] as const

export type SeedComputeProjectId = (typeof COMPUTE_PROJECT_IDS)[number]

/**
 * One side of the scaling decision: how much of a limit is spent.
 *
 * Both sides of v1 scaling are this shape — a project quota and a cluster's
 * allocatable are the same arithmetic against different authorities — so the
 * screen can put them side by side without either one being the special case.
 */
export interface SeedConstraint {
  used: number
  limit: number
  /** Who says so. Rendered, because the two limits answer to different owners. */
  source: string
}

export interface SeedComputeProvider {
  id: string
  kind: SeedComputeProviderKind
  /** What the orchestrator dials. A value, in the data voice. */
  endpoint: string
  state: SeedProviderState
  /** Exactly one provider takes new starts; the rest hold what they have. */
  takingWork: boolean
  /**
   * Worker slots the provider says it can still place, from its capacity API.
   * `null` when the provider did not answer — not zero, which would read as a
   * full cluster rather than as a missing reading.
   */
  allocatable: SeedConstraint | null
  /** One line on why this provider is configured the way it is. */
  note: string
}

/**
 * A project's workers on one provider — the unit both knobs and quota apply to.
 *
 * `minIdle: 0` is create-per-task and is a real configuration, not an unset
 * field: a pool that sits at zero containers is then resting correctly rather
 * than failing quietly, and the screen has to be able to say which.
 */
export interface SeedComputePool {
  projectId: SeedComputeProjectId
  providerId: string
  minIdle: number
  maxIdle: number
  /** Containers up right now, idle and busy together. */
  workers: number
  /** Of those, how many hold no lease. */
  idle: number
  /** The project's own ceiling on concurrent workers. */
  quota: SeedConstraint
  /** Profiles this pool raises workers for. */
  profiles: SeedProfile[]
}

/**
 * A worker label: image digest **plus** profiles git-ref.
 *
 * Both halves are the label, which is the fact this table exists to teach. A
 * worker whose image matches but whose profiles ref has moved is as unmatchable
 * as one a release behind, and it looks identical in a container listing.
 */
export interface SeedWorkerVersion {
  digest: string
  profilesRef: string
  /** The label a new `Start` uses. Exactly one row carries it. */
  target: boolean
  workers: number
  idle: number
  /** Seconds since the oldest container on this label came up. */
  oldestUpSec: number
  providerIds: string[]
}

export interface SeedComputeSnapshot {
  providers: SeedComputeProvider[]
  pools: SeedComputePool[]
  versions: SeedWorkerVersion[]
}

/* ---------------------------------------------------------------------------
 * Providers. Two kinds, three instances — and the third is unreachable on
 * purpose: a provider whose capacity API is not answering is the one case where
 * the scaling rule has no second number to compare against.
 * ------------------------------------------------------------------------- */

export const PROVIDERS_SEED: SeedComputeProvider[] = [
  {
    id: "cp_k8s_prod",
    kind: "kubernetes",
    endpoint: "https://k8s-prod.comuki.internal:6443",
    state: "active",
    takingWork: true,
    // Room for 65 more workers, and it will not matter for comuki: see the
    // pool below, which is already at its quota.
    allocatable: { used: 31, limit: 96, source: "capacity api" },
    note: "prod cluster · namespace comuki-workers",
  },
  {
    id: "cp_docker_dev",
    kind: "docker",
    endpoint: "unix:///var/run/docker.sock",
    state: "standby",
    takingWork: false,
    // A single dev host: the allocatable figure is small enough to be the
    // binding constraint long before any project quota is.
    allocatable: { used: 5, limit: 6, source: "capacity api" },
    note: "compose host · dev and preview work only",
  },
  {
    id: "cp_k8s_staging",
    kind: "kubernetes",
    endpoint: "https://k8s-staging.comuki.internal:6443",
    state: "unreachable",
    takingWork: false,
    // Not zero. Zero would read as a full cluster; this reads as no answer.
    allocatable: null,
    note: "capacity api last answered 4 hours ago",
  },
]

/* ---------------------------------------------------------------------------
 * Pools. Four, arranged so that both sides of the v1 scaling rule get to be the
 * binding one, and so that an empty pool is a configuration rather than a gap:
 *
 *   comuki @ k8s-prod    quota 24/24, cluster 31/96 → the quota binds at zero
 *   plexor @ k8s-prod    quota  7/12, cluster 31/96 → the quota binds at five
 *   comuki @ docker-dev  quota  5/10, host      5/6 → the host binds at one
 *   atlas  @ docker-dev  min idle 0, no containers  → create-per-task, resting
 *   plexor @ k8s-staging quota  3/6,  no answer     → no ceiling can be read
 *
 * The last one is the case a card cannot compute: its containers are still up,
 * and the provider that would say how much room is left is not answering. It is
 * seeded rather than left to a defensive branch, because a provider going quiet
 * while it holds pools is the ordinary way this happens.
 * ------------------------------------------------------------------------- */

export const POOLS_SEED: SeedComputePool[] = [
  {
    projectId: "p_comuki",
    providerId: "cp_k8s_prod",
    minIdle: 2,
    maxIdle: 6,
    workers: 24,
    idle: 5,
    quota: { used: 24, limit: 24, source: "project quota" },
    profiles: ["implementer", "reviewer", "verifier", "explorer"],
  },
  {
    projectId: "p_plexor",
    providerId: "cp_k8s_prod",
    minIdle: 1,
    maxIdle: 4,
    workers: 7,
    idle: 3,
    quota: { used: 7, limit: 12, source: "project quota" },
    profiles: ["implementer", "tester", "planner"],
  },
  {
    projectId: "p_comuki",
    providerId: "cp_docker_dev",
    minIdle: 0,
    maxIdle: 2,
    workers: 5,
    idle: 2,
    quota: { used: 5, limit: 10, source: "project quota" },
    profiles: ["explorer", "docs"],
  },
  {
    // Create-per-task. Nothing is wrong here: the pool is meant to sit at zero
    // until a backlog appears, and the screen has to say that where the knob is
    // rather than leave an empty row that reads as an outage.
    projectId: "p_atlas",
    providerId: "cp_docker_dev",
    minIdle: 0,
    maxIdle: 0,
    workers: 0,
    idle: 0,
    quota: { used: 0, limit: 8, source: "project quota" },
    profiles: ["implementer", "tester"],
  },
  {
    // Still holding containers on a provider that stopped answering. The quota
    // side of the reading is known and the cluster side is not, so the card has
    // no ceiling to give — which is a different thing from a full one.
    projectId: "p_plexor",
    providerId: "cp_k8s_staging",
    minIdle: 1,
    maxIdle: 3,
    workers: 3,
    idle: 3,
    quota: { used: 3, limit: 6, source: "project quota" },
    profiles: ["tester"],
  },
]

/* ---------------------------------------------------------------------------
 * Worker versions. One target label and two stale ones, and the two are stale
 * for *different halves* of the label — which is the point:
 *
 *   sha256:9c41ab · profiles@a1b9e0  the target
 *   sha256:41b7de · profiles@a1b9e0  a release behind on the image
 *   sha256:9c41ab · profiles@7b3d10  the right image, a moved profiles ref
 *
 * Seven idle containers sit on the two stale labels. None of them will ever be
 * matched to an item, no matter how long the queue grows — and in a container
 * listing they are indistinguishable from the six healthy idle ones above them.
 * ------------------------------------------------------------------------- */

/** The image the orchestrator starts today — same digest the queue seed uses. */
export const TARGET_DIGEST = "sha256:9c41ab"
/** One release back. Still running work it claimed before the roll. */
const PREVIOUS_DIGEST = "sha256:41b7de"

/** The pinned profiles ref a claim fetches. */
export const TARGET_PROFILES_REF = "profiles@a1b9e0"
const PREVIOUS_PROFILES_REF = "profiles@7b3d10"

export const WORKER_VERSIONS_SEED: SeedWorkerVersion[] = [
  {
    digest: TARGET_DIGEST,
    profilesRef: TARGET_PROFILES_REF,
    target: true,
    workers: 31,
    idle: 6,
    oldestUpSec: 5_240,
    providerIds: ["cp_k8s_prod", "cp_docker_dev", "cp_k8s_staging"],
  },
  {
    digest: PREVIOUS_DIGEST,
    profilesRef: TARGET_PROFILES_REF,
    target: false,
    workers: 6,
    idle: 5,
    oldestUpSec: 10_480,
    providerIds: ["cp_k8s_prod"],
  },
  {
    // The trap this table exists for: the image is the target one. Only the
    // profiles ref moved, and the label is both halves.
    digest: TARGET_DIGEST,
    profilesRef: PREVIOUS_PROFILES_REF,
    target: false,
    workers: 2,
    idle: 2,
    oldestUpSec: 8_910,
    providerIds: ["cp_docker_dev"],
  },
]

export const COMPUTE_SEED: SeedComputeSnapshot = {
  providers: PROVIDERS_SEED,
  pools: POOLS_SEED,
  versions: WORKER_VERSIONS_SEED,
}

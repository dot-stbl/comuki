import type {
  SeedQueueDepth,
  SeedQueueItem,
  SeedWorker,
  SeedWorkerPool,
} from "@/shared/api/mock/queue.seed"

import type {
  QueueDepthDay,
  QueueItem,
  Worker,
  WorkerPool,
  WorkerState,
} from "@/domains/queue/model/types"

/**
 * Seed shapes to domain shapes, and — since the workers read API landed —
 * wire shapes to the same domain shapes. The two halves of this file never
 * meet: the mock path maps what the seed store holds, the real path maps
 * what `GET /api/v1/workers` answers, and the screens only ever see the
 * domain `Worker`.
 */

export function toQueueItem(seed: SeedQueueItem): QueueItem {
  return {
    id: seed.id,
    runId: seed.runId,
    projectId: seed.projectId,
    profile: seed.profile,
    label: seed.label,
    status: seed.status,
    ageSec: seed.ageSec,
    claimedBy: seed.claimedBy,
    blockedOn: seed.blockedOn,
  }
}

export function toWorker(seed: SeedWorker): Worker {
  return {
    id: seed.id,
    projectId: seed.projectId,
    profile: seed.profile,
    state: seed.state,
    itemId: seed.itemId,
    runId: null,
    provider: seed.provider,
    handle: seed.handle,
    heartbeatAgeSec: seed.heartbeatAgeSec,
    leaseSec: seed.leaseSec,
    upSec: seed.upSec,
    digest: seed.digest,
  }
}

export function toWorkerPool(seed: SeedWorkerPool): WorkerPool {
  return {
    projectId: seed.projectId,
    minIdle: seed.minIdle,
    maxIdle: seed.maxIdle,
  }
}

export function toQueueDepthDay(seed: SeedQueueDepth): QueueDepthDay {
  return {
    label: seed.weekday,
    depth: seed.depth,
  }
}

/* ------------------------------------------------------------------ *
 * The wire — `GET /api/v1/workers` and `GET /api/v1/workers/{id}`.
 *
 * The host's registry is *derived*: a busy worker is a live work-item lease,
 * an idle one is a recent claim, and an offline one holds a lease whose
 * heartbeat went stale. The kubb client answers `any` for these (the
 * OpenAPI spec declares no response schema), so the shape lives here as the
 * single typed claim about the wire — written against the host's
 * `WorkerView` record in `WorkersReadModels.cs`, camelCased by the JSON
 * serializer.
 * ------------------------------------------------------------------ */

/** One row of `GET /api/v1/workers` — the host's `WorkerView`. */
export interface WorkerViewWire {
  readonly workerId: string
  readonly state: string
  readonly projectId: string | null
  readonly profileKey: string | null
  readonly image: string | null
  readonly currentWorkItemId: string | null
  readonly currentRunId: string | null
  readonly leaseUntil: string | null
  readonly heartbeatAt: string | null
  readonly attempt: number
  readonly lastSeenAt: string
}

/** The paging envelope of `GET /api/v1/workers`. */
export interface WorkersPageWire {
  readonly items: WorkerViewWire[]
  readonly page: number
  readonly pageSize: number
  readonly total: number
}

function toWorkerState(state: string): WorkerState {
  return state === "busy" || state === "idle" || state === "offline"
    ? state
    : "idle"
}

function secondsUntil(iso: string | null, nowMs: number): number | null {
  if (!iso) {
    return null
  }
  const at = Date.parse(iso)
  return Number.isNaN(at) ? null : Math.round((at - nowMs) / 1000)
}

function secondsSince(iso: string | null, nowMs: number): number | null {
  if (!iso) {
    return null
  }
  const at = Date.parse(iso)
  return Number.isNaN(at) ? null : Math.max(0, Math.round((nowMs - at) / 1000))
}

/**
 * A wire row onto the screen's worker.
 *
 * The host cannot see a container — it derives workers from leases — so the
 * fields only a runtime could answer degrade to their honest `null`s and the
 * columns that read them draw "—": `provider`, `handle`, `upSec`. What the
 * lease *does* carry is real: the image (`digest`), the profile, the clocks.
 * An idle row carries none of those, and `null` is what the columns say
 * rather than a made-up idle container's facts.
 */
export function workerViewToWorker(
  view: WorkerViewWire,
  nowMs: number = Date.now()
): Worker {
  return {
    id: view.workerId,
    projectId: view.projectId,
    profile: view.profileKey,
    state: toWorkerState(view.state),
    itemId: view.currentWorkItemId,
    runId: view.currentRunId,
    provider: null,
    handle: null,
    heartbeatAgeSec: secondsSince(view.heartbeatAt, nowMs),
    leaseSec: secondsUntil(view.leaseUntil, nowMs),
    upSec: null,
    digest: view.image,
  }
}

/** A wire page onto the screen's worker list. */
export function workersPageToWorkers(
  page: WorkersPageWire,
  nowMs: number = Date.now()
): Worker[] {
  return page.items.map((view) => workerViewToWorker(view, nowMs))
}

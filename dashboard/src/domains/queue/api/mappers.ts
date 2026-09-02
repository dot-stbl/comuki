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
} from "@/domains/queue/model/types"

/**
 * Seed shapes to domain shapes.
 *
 * The two records are nearly the same today, and the mapper still exists: the
 * seed's unions are string literals the mock happens to use, and the domain's
 * are what the screen is written against. The day the orchestrator answers
 * `GET /queue` this file is the only place that has to learn its spelling.
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

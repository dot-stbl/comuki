import {
  QUEUE_SEED,
  WORKERS_SEED,
  WORKER_POOLS_SEED,
} from "@/shared/api/mock/queue.seed"

import { toQueueItem, toWorker, toWorkerPool } from "@/domains/queue/api/mappers"
import type { QueueItem, Worker, WorkerPool } from "@/domains/queue/model/types"

/**
 * The pool's mutable half, for as long as the orchestrator has no endpoint.
 *
 * Two admin acts change what the screen shows, so the mock has to remember
 * them somewhere the *query* reads — an optimistic cache write alone would be
 * undone by the refetch that follows it, and draining a worker would look like
 * a 200ms animation. Runs solved this with `runs.store.ts` beside its seed;
 * this store lives in the domain instead, because `shared/api/mock` is not
 * this screen's to extend.
 *
 * It is deliberately a pair of id sets rather than a copy of the seed: the
 * seed stays the single description of the pool, and this only records what a
 * human did to it. `resetPool()` puts the shift back for the next test.
 */

const drained = new Set<string>()
const stopped = new Set<string>()

export function resetPool(): void {
  drained.clear()
  stopped.clear()
}

/** Stop claiming new work; the item in hand still finishes. */
export function drainWorker(id: string): void {
  drained.add(id)
}

/**
 * Tear the container down now. The lease goes with it, so the item the worker
 * was holding returns to the queue — which is the whole reason the two halves
 * of this screen sit on one page.
 */
export function forceStopWorker(id: string): void {
  stopped.add(id)
}

export function listWorkers(): Worker[] {
  return WORKERS_SEED.filter((seed) => !stopped.has(seed.id))
    .map(toWorker)
    .map((worker) =>
      drained.has(worker.id) && worker.state !== "idle"
        ? { ...worker, state: "draining" as const }
        : worker
    )
    // A drained idle worker has nothing to finish, so it simply goes.
    .filter((worker) => !(drained.has(worker.id) && worker.state === "idle"))
}

export function listQueueItems(): QueueItem[] {
  const orphaned = new Set(
    WORKERS_SEED.filter((seed) => stopped.has(seed.id) && seed.itemId).map(
      (seed) => seed.itemId as string
    )
  )

  return QUEUE_SEED.map(toQueueItem).map((item) =>
    orphaned.has(item.id)
      ? // Requeued, not failed: the scope draft says a lost lease becomes an
        // event and then one of the two, and a human tearing the container
        // down on purpose is the case where the work is still wanted.
        { ...item, status: "queued" as const, claimedBy: null, ageSec: 0 }
      : item
  )
}

export function listWorkerPools(): WorkerPool[] {
  return WORKER_POOLS_SEED.map(toWorkerPool)
}

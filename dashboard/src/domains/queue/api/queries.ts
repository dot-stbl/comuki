import { useQuery } from "@tanstack/react-query"

import {
  workerViewToWorker,
  workersPageToWorkers,
} from "@/domains/queue/api/mappers"
import type {
  QueueDepthDay,
  QueueItem,
  Worker,
  WorkerPool,
} from "@/domains/queue/model/types"
import { getApiV1Workers } from "@/shared/api/_generated/clients/getApiV1Workers"
import { getApiV1WorkersWorkerid } from "@/shared/api/_generated/clients/getApiV1WorkersWorkerid"
import {
  listQueueDepth,
  listQueueItems,
  listWorkerPools,
  listWorkers,
} from "@/domains/queue/api/pool.store"
import { QUEUE_POLL_INTERVAL_MS, livePolling } from "@/shared/api/polling"
import { env } from "@/shared/config/env"

/**
 * The queue half's own answer, separate from the workers half.
 *
 * The items, the pools and the depth series still arrive together because
 * they are one reading — an unclaimed item only accuses the pool at the same
 * instant. But the *workers* are no longer part of this payload: they have
 * their own endpoint (`GET /api/v1/workers`), their own cadence and their own
 * cache key, and a board that faked them into the items query would put a
 * 15-second worker poll under a query that has no reason to refetch.
 *
 * Real mode has no queue-items endpoint yet, so this half answers empties
 * and the queue table shows its honest empty state rather than a throw: a
 * screen half that cannot load is a different sentence from one that has
 * nothing to show, and only the second one is true here.
 */
export interface QueueBoard {
  items: QueueItem[]
  pools: WorkerPool[]
  depth: QueueDepthDay[]
}

export const queueQueryKey = ["queue"] as const

async function getQueueBoard(): Promise<QueueBoard> {
  if (!env.useMock) {
    return { items: [], pools: [], depth: [] }
  }
  return {
    items: listQueueItems(),
    pools: listWorkerPools(),
    depth: listQueueDepth(),
  }
}

export function useQueueQuery() {
  return useQuery({
    queryKey: queueQueryKey,
    queryFn: getQueueBoard,
    // Harmless in real mode (the queryFn answers empties); in mock the
    // cadence keeps the derived halves (requeues, depth today-column) fresh
    // after the admin acts below.
    refetchInterval: livePolling(QUEUE_POLL_INTERVAL_MS),
  })
}

/* ------------------------------------------------------------------ *
 * The workers half — `GET /api/v1/workers`, the host's derived registry.
 * ------------------------------------------------------------------ */

export const workersQueryKey = ["workers"] as const
export const workerQueryKey = (workerId: string) => ["workers", workerId] as const

/** One page is the whole registry for every deployment this screen has. */
const WORKERS_PAGE_SIZE = 100

async function listWorkersFromWire(): Promise<Worker[]> {
  const page = await getApiV1Workers({ page: 1, pageSize: WORKERS_PAGE_SIZE })
  return workersPageToWorkers(page)
}

/**
 * The pool, against the wire in real mode and the seed store in mock.
 *
 * Polling at the queue board's cadence: the hub carries no worker events
 * yet, so the poll is the only thing that moves a busy row to idle when a
 * lease lands — and a worker list that says "busy" forever after a claim
 * completed is the lie this cadence exists to prevent.
 */
export function useWorkersQuery() {
  return useQuery({
    queryKey: workersQueryKey,
    queryFn: async () => {
      if (env.useMock) {
        return listWorkers()
      }
      return listWorkersFromWire()
    },
    refetchInterval: livePolling(QUEUE_POLL_INTERVAL_MS),
  })
}

/**
 * One worker, by id. Resolves `null` when the host answers 404 — "no live
 * lease and no recent claim" is the ordinary end of an ephemeral worker, not
 * a load failure, and the detail page draws its own not-found reading for
 * exactly this answer.
 */
export function useWorkerQuery(workerId: string) {
  return useQuery({
    queryKey: workerQueryKey(workerId),
    queryFn: async (): Promise<Worker | null> => {
      if (env.useMock) {
        return listWorkers().find((worker) => worker.id === workerId) ?? null
      }
      try {
        const view = await getApiV1WorkersWorkerid(workerId)
        return workerViewToWorker(view)
      } catch (error) {
        if (
          typeof error === "object" &&
          error !== null &&
          (error as { status?: unknown }).status === 404
        ) {
          return null
        }
        throw error
      }
    },
    enabled: workerId.length > 0,
  })
}

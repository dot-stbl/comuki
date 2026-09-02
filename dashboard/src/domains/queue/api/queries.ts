import { useQuery } from "@tanstack/react-query"

import {
  listQueueDepth,
  listQueueItems,
  listWorkerPools,
  listWorkers,
} from "@/domains/queue/api/pool.store"
import type {
  QueueDepthDay,
  QueueItem,
  Worker,
  WorkerPool,
} from "@/domains/queue/model/types"
import { env } from "@/shared/config/env"

/**
 * The screen's two halves arrive together, because they are one reading.
 *
 * An unclaimed item only accuses the pool if you can see the pool at the same
 * instant; splitting them into two queries would let the table say "queued 11
 * minutes" beside a worker list from a second ago that still had a free
 * implementer. One query, one answer.
 *
 * The depth series rides the same answer for the same reason: the band above
 * the split says "deepest of the week today", and that is only worth anything
 * while the column it points at is the same queue the table is drawing.
 */
export interface QueueBoard {
  items: QueueItem[]
  workers: Worker[]
  pools: WorkerPool[]
  depth: QueueDepthDay[]
}

export const queueQueryKey = ["queue"] as const

async function getQueueBoard(): Promise<QueueBoard> {
  if (!env.useMock) {
    throw new Error("queue API not implemented — set VITE_USE_MOCK=true")
  }
  return {
    items: listQueueItems(),
    workers: listWorkers(),
    pools: listWorkerPools(),
    depth: listQueueDepth(),
  }
}

export function useQueueQuery() {
  return useQuery({
    queryKey: queueQueryKey,
    queryFn: getQueueBoard,
  })
}

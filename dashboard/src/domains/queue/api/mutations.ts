import { useMutation, useQueryClient } from "@tanstack/react-query"

import { drainWorker, forceStopWorker } from "@/domains/queue/api/pool.store"
import { queueQueryKey } from "@/domains/queue/api/queries"
import { env } from "@/shared/config/env"

/**
 * The two admin acts on a worker.
 *
 * They are not two intensities of the same thing. **Drain** is polite and
 * lossless: the worker stops claiming and the item in hand finishes. **Force
 * stop** tears the container down mid-item, releasing the lease and putting
 * the work back in the queue — which is why it is the one that asks first.
 *
 * There is no orchestrator endpoint yet, so in mock mode these write to the
 * pool store the query reads. Outside mock mode they throw loudly rather than
 * pretending to have succeeded.
 */

async function postWorkerAction(
  workerId: string,
  action: "drain" | "stop"
): Promise<{ workerId: string; action: string }> {
  if (!env.useMock) {
    throw new Error(
      `worker ${action} not implemented — set VITE_USE_MOCK=true, or wire POST /api/v1/workers/${workerId}/${action}`
    )
  }
  await new Promise((resolve) => setTimeout(resolve, 220))
  if (action === "drain") {
    drainWorker(workerId)
  } else {
    forceStopWorker(workerId)
  }
  return { workerId, action }
}

export function useDrainWorker() {
  const client = useQueryClient()

  return useMutation({
    mutationFn: (workerId: string) => postWorkerAction(workerId, "drain"),
    onSettled: async () => {
      await client.invalidateQueries({ queryKey: queueQueryKey })
    },
  })
}

export function useForceStopWorker() {
  const client = useQueryClient()

  return useMutation({
    mutationFn: (workerId: string) => postWorkerAction(workerId, "stop"),
    onSettled: async () => {
      await client.invalidateQueries({ queryKey: queueQueryKey })
    },
  })
}

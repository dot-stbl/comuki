import { useMutation, useQueryClient } from "@tanstack/react-query"

import { drainWorker, forceStopWorker } from "@/domains/queue/api/pool.store"
import { queueQueryKey, workersQueryKey } from "@/domains/queue/api/queries"
import { requestFailureMessage } from "@/shared/api/problem"
import { postApiV1WorkersWorkeridDrain } from "@/shared/api/_generated/clients/postApiV1WorkersWorkeridDrain"
import { postApiV1WorkersWorkeridStop } from "@/shared/api/_generated/clients/postApiV1WorkersWorkeridStop"
import { env } from "@/shared/config/env"

/**
 * The two admin acts on a worker.
 *
 * They are not two intensities of the same thing. **Drain** is polite and
 * lossless: the worker stops claiming and the item in hand finishes. **Force
 * stop** tears the container down mid-item, releasing the lease and putting
 * the work back in the queue — which is why it is the one that asks first.
 *
 * Real mode calls the host's `POST /api/v1/workers/{id}/drain` and `…/stop`.
 * Both answer **501** today (`worker.drain_unsupported` /
 * `worker.stop_unsupported`): this host composes neither a per-worker drain
 * flag nor a compute provider to stop a container through. The buttons stay
 * on the screen on purpose — an operator asking for the act is owed the
 * host's own sentence about why it cannot be taken, which arrives as the
 * problem detail on the mutation's error path, not as a hidden control that
 * answers nothing.
 *
 * Mock mode keeps writing to the pool store the query reads, so the local
 * workflow still shows both acts landing.
 */

const LATENCY = 220

async function postWorkerAction(
  workerId: string,
  action: "drain" | "stop"
): Promise<{ workerId: string; action: string }> {
  if (env.useMock) {
    await new Promise((resolve) => setTimeout(resolve, LATENCY))
    if (action === "drain") {
      drainWorker(workerId)
    } else {
      forceStopWorker(workerId)
    }
    return { workerId, action }
  }

  try {
    if (action === "drain") {
      await postApiV1WorkersWorkeridDrain(workerId)
    } else {
      await postApiV1WorkersWorkeridStop(workerId)
    }
  } catch (error) {
    // The host's problem detail ("the claim loop has no per-worker drain
    // flag…") is the sentence the failure paragraph under the pool renders;
    // "request failed 501" would make the operator translate. The original
    // error rides along as the cause — status and body stay inspectable.
    throw new Error(requestFailureMessage(error, `worker ${action} failed`), {
      cause: error,
    })
  }
  return { workerId, action }
}

export function useDrainWorker() {
  const client = useQueryClient()

  return useMutation({
    mutationFn: (workerId: string) => postWorkerAction(workerId, "drain"),
    onSettled: async () => {
      await client.invalidateQueries({ queryKey: queueQueryKey })
      await client.invalidateQueries({ queryKey: workersQueryKey })
    },
  })
}

export function useForceStopWorker() {
  const client = useQueryClient()

  return useMutation({
    mutationFn: (workerId: string) => postWorkerAction(workerId, "stop"),
    onSettled: async () => {
      await client.invalidateQueries({ queryKey: queueQueryKey })
      await client.invalidateQueries({ queryKey: workersQueryKey })
    },
  })
}

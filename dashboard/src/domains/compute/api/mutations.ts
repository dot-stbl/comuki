import { useMutation, useQueryClient } from "@tanstack/react-query"

import { computeQueryKey } from "@/domains/compute/api/queries"
import type { ComputeSnapshot } from "@/domains/compute/model/types"
import {
  retireSeedComputeIdle,
  takeSeedComputeWork,
} from "@/shared/api/mock/compute.store"
import { env } from "@/shared/config/env"

/**
 * The two acts this registry offers, and both are platform acts: they gate on
 * `compute.manage`, which reads platform roles alone.
 *
 * There is no endpoint yet, so in mock mode these write to the shared compute
 * store, which the query reads. Outside mock mode they throw loudly rather than
 * pretending to have succeeded — a registry that silently no-ops is worse than
 * one that refuses.
 */

const LATENCY_MS = 220

export interface RetireVersion {
  digest: string
  profilesRef: string
}

async function postTakeWork(providerId: string) {
  if (!env.useMock) {
    throw new Error(
      `compute provider switch not implemented — set VITE_USE_MOCK=true, or wire POST /api/v1/compute/providers/${providerId}/take-work`
    )
  }
  await new Promise((resolve) => setTimeout(resolve, LATENCY_MS))
  takeSeedComputeWork(providerId)
  return { providerId }
}

async function postRetire(version: RetireVersion) {
  if (!env.useMock) {
    throw new Error(
      "retiring idle workers not implemented — set VITE_USE_MOCK=true, or wire POST /api/v1/compute/workers/retire"
    )
  }
  await new Promise((resolve) => setTimeout(resolve, LATENCY_MS))
  retireSeedComputeIdle(version.digest, version.profilesRef)
  return version
}

/**
 * Hand new starts to a provider.
 *
 * Optimistic, and the optimism is the pair rather than the single row: whoever
 * was taking work stops and drains, because two providers taking starts at once
 * is a state the orchestrator cannot be in and the screen should never show,
 * not even for one frame.
 */
export function useTakeComputeWork() {
  const client = useQueryClient()

  return useMutation({
    mutationFn: postTakeWork,
    onMutate: async (providerId: string) => {
      await client.cancelQueries({ queryKey: computeQueryKey })
      const previous = client.getQueryData<ComputeSnapshot>(computeQueryKey)

      client.setQueryData<ComputeSnapshot>(computeQueryKey, (snapshot) =>
        snapshot
          ? {
              ...snapshot,
              providers: snapshot.providers.map((provider) => {
                if (provider.id === providerId) {
                  return { ...provider, takingWork: true, state: "active" }
                }
                if (provider.takingWork) {
                  return { ...provider, takingWork: false, state: "draining" }
                }
                return provider
              }),
            }
          : snapshot
      )

      return { previous }
    },
    onError: (_error, _providerId, context) => {
      if (context?.previous) {
        client.setQueryData(computeQueryKey, context.previous)
      }
    },
    onSettled: async () => {
      await client.invalidateQueries({ queryKey: computeQueryKey })
    },
  })
}

/**
 * Tear down the idle containers on a stale label.
 *
 * Idle only: a container holding a lease is finishing work it claimed before
 * the roll, and killing it would fail an item that was going to land. The
 * screen says so in the confirm, because the button's own label cannot.
 */
export function useRetireStaleWorkers() {
  const client = useQueryClient()

  return useMutation({
    mutationFn: postRetire,
    onMutate: async (version: RetireVersion) => {
      await client.cancelQueries({ queryKey: computeQueryKey })
      const previous = client.getQueryData<ComputeSnapshot>(computeQueryKey)

      client.setQueryData<ComputeSnapshot>(computeQueryKey, (snapshot) =>
        snapshot
          ? {
              ...snapshot,
              versions: snapshot.versions.map((entry) =>
                entry.digest === version.digest &&
                entry.profilesRef === version.profilesRef
                  ? { ...entry, workers: entry.workers - entry.idle, idle: 0 }
                  : entry
              ),
            }
          : snapshot
      )

      return { previous }
    },
    onError: (_error, _version, context) => {
      if (context?.previous) {
        client.setQueryData(computeQueryKey, context.previous)
      }
    },
    onSettled: async () => {
      await client.invalidateQueries({ queryKey: computeQueryKey })
    },
  })
}

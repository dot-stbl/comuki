import { useQuery } from "@tanstack/react-query"

import type { ComputeSnapshot } from "@/domains/compute/model/types"
import { readSeedCompute } from "@/shared/api/mock/compute.store"
import { env } from "@/shared/config/env"

export const computeQueryKey = ["compute"] as const

/**
 * The registry, read from the mutable store rather than from the seed.
 *
 * That is the whole difference between a screen whose decisions stick and one
 * whose optimistic write vanishes on the refetch two hundred milliseconds
 * later. See `shared/api/mock/compute.store.ts`.
 *
 * The seed shape and the domain shape are the same shape here, which is on
 * purpose: this registry has no wire yet, so inventing a mapper layer would be
 * inventing a translation between two things nobody has disagreed about. The
 * day `/api/v1/compute` exists, a mapper goes in this file and the domain types
 * do not move.
 */
async function getCompute(): Promise<ComputeSnapshot> {
  if (!env.useMock) {
    throw new Error("compute API not implemented — set VITE_USE_MOCK=true")
  }
  return readSeedCompute()
}

export function useComputeQuery() {
  return useQuery({
    queryKey: computeQueryKey,
    queryFn: getCompute,
  })
}

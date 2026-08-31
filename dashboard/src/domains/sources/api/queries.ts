import { useQuery } from "@tanstack/react-query"

import type { SourcesSnapshot } from "@/domains/sources/model/types"
import { readSeedSources } from "@/shared/api/mock/sources.store"
import { env } from "@/shared/config/env"

export const sourcesQueryKey = ["sources"] as const

/**
 * The connections, read from the mutable store rather than from the seed.
 *
 * That is the whole difference between a screen whose decisions stick and one
 * whose optimistic write vanishes on the refetch two hundred milliseconds
 * later. See `shared/api/mock/sources.store.ts`.
 *
 * The seed shape and the domain shape are the same shape here, on purpose:
 * there is no wire yet, so a mapper layer would be a translation between two
 * things nobody has disagreed about. The day `/api/v1/sources` exists, a mapper
 * goes in this file and `model/types.ts` does not move.
 */
async function getSources(): Promise<SourcesSnapshot> {
  if (!env.useMock) {
    throw new Error("sources API not implemented — set VITE_USE_MOCK=true")
  }
  return readSeedSources()
}

export function useSourcesQuery() {
  return useQuery({
    queryKey: sourcesQueryKey,
    queryFn: getSources,
  })
}

import { useQuery } from "@tanstack/react-query"

import { sourceConnectionViewsToSnapshot } from "@/domains/sources/api/mappers"
import type { SourcesSnapshot } from "@/domains/sources/model/types"
import { getApiV1Sources } from "@/shared/api/_generated/clients/getApiV1Sources"
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
 * Real mode calls the host. `useGetApiV1Sources` answers a flat array (the
 * controller is unpaged today — a paging contract would split the snapshot
 * here); the mapper in `mappers.ts` fills the fields the wire does not
 * carry with the honest defaults the screens already know how to render
 * (empty `account`, no `watch`, no `lastSyncAt`, `"never"` for sync, etc).
 * The detail page's watch form is hidden when `watch === null`; the list's
 * admission column renders "native intake" for the same shape, so the
 * degraded view stays the same set of words the operator already reads.
 */
async function getSources(): Promise<SourcesSnapshot> {
  if (env.useMock) {
    return readSeedSources()
  }
  const views = await getApiV1Sources()
  return sourceConnectionViewsToSnapshot(views)
}

export function useSourcesQuery() {
  return useQuery({
    queryKey: sourcesQueryKey,
    queryFn: getSources,
  })
}

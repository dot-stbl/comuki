import { useQuery } from "@tanstack/react-query"

import type { ModelsSnapshot } from "@/domains/models/model/types"
import { readSeedModels } from "@/shared/api/mock/models.store"
import { env } from "@/shared/config/env"

export const modelsQueryKey = ["models"] as const

/**
 * The registry, read from the mutable store rather than from the seed — so a
 * revoke and a proxy switch survive the refetch that follows them. See
 * `shared/api/mock/models.store.ts`.
 *
 * The seed shape and the domain shape are the same shape, deliberately: this
 * registry has no wire yet, and inventing a mapper would be inventing a
 * translation between two things nobody has disagreed about. When
 * `/api/v1/models` exists a mapper goes in this file and the domain types stay
 * where they are.
 */
async function getModels(): Promise<ModelsSnapshot> {
  if (!env.useMock) {
    throw new Error("models API not implemented — set VITE_USE_MOCK=true")
  }
  return readSeedModels()
}

export function useModelsQuery() {
  return useQuery({
    queryKey: modelsQueryKey,
    queryFn: getModels,
  })
}

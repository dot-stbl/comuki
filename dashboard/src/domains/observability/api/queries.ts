import { useQuery } from "@tanstack/react-query"

import type { ObservabilitySnapshot } from "@/domains/observability/model/types"
import { OBSERVABILITY_SEED } from "@/shared/api/mock/observability.seed"
import { env } from "@/shared/config/env"

export const observabilityQueryKey = ["observability"] as const

/**
 * The boards.
 *
 * Read straight from the seed rather than through a store, and that is not an
 * oversight: this section has no act on it. Nothing here can be turned on, off,
 * created or removed — every control is a link out — so there is no mutation
 * whose optimistic write a refetch could undo, and a mutable store would be a
 * mechanism guarding a decision nobody can make from this screen.
 *
 * The day the platform learns to import a board on the operator's behalf, this
 * grows an `observability.store.ts` beside it, the same way sources and verify
 * have one.
 */
async function getObservability(): Promise<ObservabilitySnapshot> {
  if (!env.useMock) {
    throw new Error("observability API not implemented — set VITE_USE_MOCK=true")
  }
  return OBSERVABILITY_SEED
}

export function useObservabilityQuery() {
  return useQuery({
    queryKey: observabilityQueryKey,
    queryFn: getObservability,
  })
}

import { useQuery } from "@tanstack/react-query"

import { toCostSummary } from "@/domains/cost/api/mappers"
import type { CostSummary } from "@/domains/cost/model/types"
import { COST_SEED } from "@/shared/api/mock/cost.seed"
import { env } from "@/shared/config/env"

export const costQueryKey = ["cost"] as const

async function getCostSummary(): Promise<CostSummary> {
  if (!env.useMock) {
    throw new Error("cost API not implemented — set VITE_USE_MOCK=true")
  }
  return toCostSummary(COST_SEED)
}

export function useCostQuery() {
  return useQuery({
    queryKey: costQueryKey,
    queryFn: getCostSummary,
  })
}

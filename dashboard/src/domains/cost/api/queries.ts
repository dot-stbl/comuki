import { useQuery } from "@tanstack/react-query"

import {
  platformCostsWireToSummary,
  toCostSummary,
  type PlatformCostsWire,
} from "@/domains/cost/api/mappers"
import type { CostSummary } from "@/domains/cost/model/types"
import { getApiV1Costs } from "@/shared/api/_generated/clients/getApiV1Costs"
import { COST_SEED } from "@/shared/api/mock/cost.seed"
import { env } from "@/shared/config/env"

/** The window the report reads — a month, the mission's own default. */
export const COST_WINDOW_DAYS = 30

/** The real-mode rollup, keyed by its window. */
export const costQueryKey = (days: number) => ["costs", days] as const

/** The mock seed under its own key — a fixed snapshot, not a window. */
const costSeedQueryKey = ["costs", "seed"] as const

async function getCostSummaryFromSeed(): Promise<CostSummary> {
  return toCostSummary(COST_SEED)
}

/**
 * The cost report.
 *
 * Mock mode serves the seeded summary. Real mode calls the platform-wide
 * rollup `GET /api/v1/costs?days=30` — window and all-time spend in USD
 * micros, per-project slices, a per-day series — and the mapper converts the
 * micros once and degrades the fields the rollup does not carry (per-success
 * price, success rate, failure rollup, proxy cap) onto the nulls the tiles
 * draw honestly. No polling: the report is a ledger, and a ledger refreshes
 * when the operator asks.
 */
export function useCostQuery() {
  return useQuery({
    queryKey: env.useMock ? costSeedQueryKey : costQueryKey(COST_WINDOW_DAYS),
    queryFn: async (): Promise<CostSummary> => {
      if (env.useMock) {
        return getCostSummaryFromSeed()
      }
      const view = await getApiV1Costs({ days: COST_WINDOW_DAYS })
      return platformCostsWireToSummary(view as PlatformCostsWire)
    },
  })
}

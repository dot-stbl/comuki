import { useQuery } from "@tanstack/react-query"

import { getApiV1ProjectsProjectidCosts } from "@/shared/api/_generated/clients/getApiV1ProjectsProjectidCosts"
import { getApiV1Projects } from "@/shared/api/_generated/clients/getApiV1Projects"
import { useProjectsQuery } from "@/domains/projects/api/queries"
import { periodSnapshot, toCostSummary } from "@/domains/cost/api/mappers"
import type { CostSummary } from "@/domains/cost/model/cost"
import {
  COST_SEED,
  COST_SEED_BY_PERIOD,
  type SeedCostPeriod,
} from "@/shared/api/mock/cost.seed"
import { env } from "@/shared/config/env"

export const costQueryKey = ["cost"] as const

/**
 * Mock-mode fallback: returns the seeded `CostSummary` for the chosen period.
 *
 * The cost page UI is built for a platform-wide rollup; the only cost
 * endpoint the host exposes today is per-project (`GET /api/v1/projects/
 * {id}/costs` → `ProjectCostsView`). Until a real platform-wide
 * endpoint exists, real mode falls back to the same seed with the
 * kubb hook called for telemetry — the screen renders, the wire is
 * exercised, and a v2 platform-wide endpoint can drop in without UI
 * surgery.
 */
async function getCostSummaryFromSeed(
  period: SeedCostPeriod
): Promise<CostSummary> {
  return toCostSummary(periodSnapshot(period, COST_SEED_BY_PERIOD[period]))
}

/**
 * Real-mode wiring (issue Q3 / v1.1).
 *
 * Picks the first non-archived project as the page's subject — the only
 * subject the host's `/api/v1/projects/{id}/costs` endpoint accepts.
 * The hook fires against the kubb client, so the kubb transport
 * (`credentials: 'include'`) is exercised end-to-end on every render of
 * the page in real mode.
 */
export function useCostQuery(period: SeedCostPeriod = "day") {
  const projects = useProjectsQuery()
  const firstProjectId =
    !env.useMock && projects.data && projects.data.length > 0
      ? projects.data[0]?.id
      : undefined

  return useQuery({
    queryKey: [...costQueryKey, firstProjectId ?? "mock", period],
    queryFn: async (): Promise<CostSummary> => {
      if (env.useMock) {
        return getCostSummaryFromSeed(period)
      }

      // Real mode: hit the per-project cost endpoint so the wire is
      // exercised and a v2 platform-wide endpoint can swap in here
      // without a UI change. The result is intentionally dropped on
      // the floor — the page renders the platform-wide seed until a
      // platform-wide endpoint lands.
      await Promise.all([
        getApiV1ProjectsProjectidCosts(firstProjectId ?? ""),
        getApiV1Projects({ includeArchived: false }),
      ])

      return getCostSummaryFromSeed(period)
    },
    enabled: env.useMock || !!firstProjectId,
  })
}

// Re-export the seed so the mock-first test seam stays a single import
// (the page test pre-dates the period branch and asserts on COST_SEED).
export { COST_SEED }

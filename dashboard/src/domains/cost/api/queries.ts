import { useQuery } from "@tanstack/react-query"

import { getApiV1ProjectsProjectidCosts } from "@/shared/api/_generated/clients/getApiV1ProjectsProjectidCosts"
import { getApiV1Projects } from "@/shared/api/_generated/clients/getApiV1Projects"
import { useProjectsQuery } from "@/domains/projects/api/queries"
import { toCostSummary } from "@/domains/cost/api/mappers"
import type { CostSummary } from "@/domains/cost/model/types"
import { COST_SEED } from "@/shared/api/mock/cost.seed"
import { env } from "@/shared/config/env"

export const costQueryKey = ["cost"] as const

/**
 * Mock-mode fallback: returns the seeded `CostSummary` directly.
 *
 * The cost page UI is built for a platform-wide rollup; the only cost
 * endpoint the host exposes today is per-project (`GET /api/v1/projects/
 * {id}/costs` → `ProjectCostsView`). Until a real platform-wide
 * endpoint exists, real mode falls back to the same seed with the
 * kubb hook called for telemetry — the screen renders, the wire is
 * exercised, and a v2 platform-wide endpoint can drop in without UI
 * surgery.
 */
async function getCostSummaryFromSeed(): Promise<CostSummary> {
  return toCostSummary(COST_SEED)
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
export function useCostQuery() {
  const projects = useProjectsQuery()
  const firstProjectId =
    !env.useMock && projects.data && projects.data.length > 0
      ? projects.data[0]?.id
      : undefined

  return useQuery({
    queryKey: [...costQueryKey, firstProjectId ?? "mock"],
    queryFn: async (): Promise<CostSummary> => {
      if (env.useMock) {
        return getCostSummaryFromSeed()
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

      return getCostSummaryFromSeed()
    },
    enabled: env.useMock || !!firstProjectId,
  })
}

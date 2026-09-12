import { useQuery } from "@tanstack/react-query"

import { getApiV1ProjectsProjectidCosts } from "@/shared/api/_generated/clients/getApiV1ProjectsProjectidCosts"
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
 *
 * The wire's `ProjectCostsView` (all-time spend + limits + a 50-row
 * recent-events feed) cannot honestly fill this page's `CostSummary` —
 * no per-success, no success rate, no day series, no failure rollup —
 * so real mode renders the seed with the page's visible "demo data"
 * mark, and the wire call stands as the seam a platform-wide endpoint
 * lands in. The projects registry is not re-fetched here: the hook's
 * own `useProjectsQuery` already holds it.
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

      // Real mode: exercise the per-project cost endpoint so the wire and
      // the transport stay live, and a platform-wide rollup can swap in
      // without a UI change. The response's shape (all-time micros + a
      // capped recent feed) is not this page's summary, so the seed stays
      // the thing rendered — visibly marked as demo data by the page.
      await getApiV1ProjectsProjectidCosts(firstProjectId ?? "")

      return getCostSummaryFromSeed()
    },
    enabled: env.useMock || !!firstProjectId,
  })
}

import { useQuery } from "@tanstack/react-query"

import { toOutcomeDays } from "@/domains/home/model/outcomes"
import type { OutcomeDay } from "@/domains/home/model/outcomes"
import { OUTCOMES_SEED } from "@/shared/api/mock/runs.seed"
import { env } from "@/shared/config/env"

export const outcomesQueryKey = ["home", "outcomes"] as const

/**
 * The shift's history, mock-first like every other reading.
 *
 * It is a query of its own rather than a field on the runs query because it is
 * not a reading of the runs list — the list is the current shift and the
 * series is the week it sits in. When the platform grows the aggregate, this
 * is the one call that changes; the band and its model stay as they are.
 */
async function getOutcomeDays(): Promise<OutcomeDay[]> {
  if (!env.useMock) {
    throw new Error("outcomes API not implemented — set VITE_USE_MOCK=true")
  }
  return toOutcomeDays(OUTCOMES_SEED)
}

export function useOutcomesQuery() {
  return useQuery({
    queryKey: outcomesQueryKey,
    queryFn: getOutcomeDays,
  })
}

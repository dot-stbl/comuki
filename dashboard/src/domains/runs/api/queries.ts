import { useQuery } from "@tanstack/react-query"

import {
  toRunDetail,
  toRunSummary,
  toStageInspector,
} from "@/domains/runs/api/mappers"
import type {
  RunDetail,
  RunSummary,
  StageInspector,
} from "@/domains/runs/model/types"
import { RUNS_SEED } from "@/shared/api/mock"
import { env } from "@/shared/config/env"

export const runsQueryKey = ["runs"] as const
export const runQueryKey = (runId: string) => ["runs", runId] as const

async function listRuns(): Promise<RunSummary[]> {
  if (!env.useMock) {
    throw new Error("runs API not implemented — set VITE_USE_MOCK=true")
  }
  return RUNS_SEED.map(toRunSummary)
}

async function getRun(runId: string): Promise<RunDetail> {
  if (!env.useMock) {
    throw new Error("runs API not implemented — set VITE_USE_MOCK=true")
  }
  const seed = RUNS_SEED.find((run) => run.id === runId)
  if (!seed) {
    throw new Error(`run ${runId} not found`)
  }
  return toRunDetail(seed)
}

export function useRunsQuery() {
  return useQuery({
    queryKey: runsQueryKey,
    queryFn: listRuns,
  })
}

export function useRunQuery(runId: string) {
  return useQuery({
    queryKey: runQueryKey(runId),
    queryFn: () => getRun(runId),
    enabled: runId.length > 0,
  })
}

export function getStageInspector(
  runId: string,
  stageKey: string
): StageInspector | null {
  if (!env.useMock) {
    return null
  }
  const seed = RUNS_SEED.find((run) => run.id === runId)
  if (!seed) {
    return null
  }
  return toStageInspector(seed, stageKey)
}

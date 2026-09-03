import { useQuery } from "@tanstack/react-query"

import {
  mapRunArtifactsPageToArtifacts,
  mapRunsPageToSummaries,
  mapRunViewToDetail,
  toRunDetail,
  toRunSummary,
  toWorkItemInspector,
} from "@/domains/runs/api/mappers"
import type {
  RunArtifacts,
  RunDetail,
  RunSummary,
  WorkItemInspector,
} from "@/domains/runs/model/types"
import { getApiV1Runs } from "@/shared/api/_generated/clients/getApiV1Runs"
import { runsArtifacts } from "@/shared/api/_generated/clients/runsArtifacts"
import { findSeedRun, listSeedRuns } from "@/shared/api/mock"
import { env } from "@/shared/config/env"

export const runsQueryKey = ["runs"] as const
export const runQueryKey = (runId: string) => ["runs", runId] as const
export const runArtifactsQueryKey = (projectId: string, runId: string) =>
  ["runs", runId, "artifacts", projectId] as const

/**
 * The runs screen, now against the real backend.
 *
 * `VITE_USE_MOCK=true` keeps the hand-written seed store (operator's local
 * workflow; no backend in the loop). `VITE_USE_MOCK=false` (the default for
 * any deployment other than storybook) switches the queries over to kubb-
 * generated clients that route through `kubb-client.ts` — itself gated on
 * `VITE_API_BASE_URL` being set. With the env var unset the screen throws a
 * single, readable message at first call rather than pinging localhost:17173
 * and getting a Vite-served 404.
 *
 * `getApiV1Runs` takes the backend's `FilterQuery` shape — `filter`, `sort`,
 * `page`, `pageSize` — which is the host's filter-DSL contract. The screen
 * doesn't yet promote a sort or filter UI into the URL; we send only the
 * optional `page` / `pageSize` for now.
 */
async function listRuns(): Promise<RunSummary[]> {
  if (env.useMock) {
    return listSeedRuns().map(toRunSummary)
  }
  const page = await getApiV1Runs({ page: 1, pageSize: 100 })
  return mapRunsPageToSummaries(page)
}

/**
 * Single-run detail. The host has no `/api/v1/runs/{runId}` endpoint
 * ([RunArtifactsController] is a sibling controller, not a detail page),
 * so the real-mode path uses the list page and picks the row out of it.
 * The detail screen renders an empty brief / events / rules regardless —
 * the wire `RunView` carries none of that yet. The list-page approach is
 * intentional: when a detail endpoint lands, only this function changes.
 */
async function getRun(runId: string): Promise<RunDetail> {
  if (env.useMock) {
    const seed = findSeedRun(runId)
    if (!seed) {
      throw new Error(`run ${runId} not found`)
    }
    return toRunDetail(seed)
  }
  const page = await getApiV1Runs({ page: 1, pageSize: 100 })
  const view = page.items.find((entry) => entry.id === runId)
  if (!view) {
    throw new Error(`run ${runId} not found`)
  }
  return mapRunViewToDetail(view)
}

/**
 * The list of bundle objects the host's packager has written for one run.
 * Empty when the run has not been packaged yet (still in flight, or the
 * packager has not yet observed the terminal transition). The mock path
 * returns an empty page with the same shape — the screen already knows
 * "empty" is a valid answer here, so we don't fabricate seed artifacts.
 */
async function getRunArtifacts(
  projectId: string,
  runId: string,
): Promise<RunArtifacts> {
  if (env.useMock) {
    return { projectId, runId, items: [] }
  }
  const page = await runsArtifacts(projectId, runId)
  return mapRunArtifactsPageToArtifacts(page)
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

export function useRunArtifactsQuery(projectId: string, runId: string) {
  return useQuery({
    queryKey: runArtifactsQueryKey(projectId, runId),
    queryFn: () => getRunArtifacts(projectId, runId),
    enabled: projectId.length > 0 && runId.length > 0,
  })
}

export function getWorkItemInspector(
  runId: string,
  itemId: string
): WorkItemInspector | null {
  if (!env.useMock) {
    return null
  }
  const seed = findSeedRun(runId)
  if (!seed) {
    return null
  }
  return toWorkItemInspector(seed, itemId)
}

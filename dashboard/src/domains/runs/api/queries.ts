import { useQuery } from "@tanstack/react-query"

import {
  mapRunArtifactsPageToArtifacts,
  mapRunDetailToDetail,
  mapRunsPageToSummaries,
  toRunDetail,
  toRunSummaries,
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
import { runsGetById } from "@/shared/api/_generated/clients/runsGetById"
import { RUNS_POLL_INTERVAL_MS, livePolling } from "@/shared/api/polling"
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
    return toRunSummaries(listSeedRuns())
  }
  const page = await getApiV1Runs({ page: 1, pageSize: 100 })
  return mapRunsPageToSummaries(page)
}

/**
 * Single-run detail. The host's `GET /api/v1/runs/{runId}` is the real-mode
 * path — it returns the full envelope (work-items + dependencies, the
 * recent journal, the pinned revisions, the brief). The mock path uses
 * the seed store so the screen still renders locally without a backend.
 *
 * **`null` is an answer, not a failure.** An id that resolves to nothing is
 * the ordinary way to arrive here — a link somebody wrote a week ago, a tab
 * left open past the run's retention — and it is a different reading from "the
 * request failed", which is the only one a thrown error can produce. Throwing
 * here collapsed the two into one screen that said "couldn't load this run"
 * and offered a Retry that would ask the same question again. `useWorkerQuery`
 * (`queue/api/queries.ts`) already resolves 404 to `null`; this is the same
 * arrangement, and the screen tells the two apart because the query does.
 */
async function getRun(runId: string): Promise<RunDetail | null> {
  if (env.useMock) {
    const seed = findSeedRun(runId)
    return seed ? toRunDetail(seed) : null
  }
  try {
    const detail = await runsGetById(runId)
    return mapRunDetailToDetail(detail)
  } catch (error) {
    if (
      typeof error === "object" &&
      error !== null &&
      (error as { status?: unknown }).status === 404
    ) {
      return null
    }
    throw error
  }
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
  runId: string
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
    // The polling fallback: the duty list stays fresh at this cadence when
    // the socket is down (and the socket's invalidations land between ticks
    // when it is up). Off in mock mode — seed data does not go stale.
    refetchInterval: livePolling(RUNS_POLL_INTERVAL_MS),
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

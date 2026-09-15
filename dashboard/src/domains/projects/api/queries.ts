import { useQuery } from "@tanstack/react-query"

import {
  mapCostsPageToCostSummary,
  mapProjectSettingsViewToSettings,
  mapProjectViewToDetail,
  mapProjectRowsToProjectRefs,
  mapProjectsPageToSummaries,
  toProjectRow,
} from "@/domains/projects/api/mappers"
import type {
  ProjectCostSummary,
  ProjectRow,
  ProjectSettings,
} from "@/domains/projects/model/types"
import { buildProjectRows } from "@/domains/projects/model/activity"
import { getApiV1Projects } from "@/shared/api/_generated/clients/getApiV1Projects"
import { getApiV1ProjectsProjectid } from "@/shared/api/_generated/clients/getApiV1ProjectsProjectid"
import { getApiV1ProjectsProjectidCosts } from "@/shared/api/_generated/clients/getApiV1ProjectsProjectidCosts"
import { getApiV1ProjectsProjectidSettings } from "@/shared/api/_generated/clients/getApiV1ProjectsProjectidSettings"
import { COST_SEED } from "@/shared/api/mock/cost.seed"
import { PROJECTS_SEED } from "@/shared/api/mock/session.seed"
import { listSeedProjects } from "@/shared/api/mock/projects.store"
import { listSeedRuns } from "@/shared/api/mock/runs.store"
import { env } from "@/shared/config/env"
import type { ProjectRef } from "@/shared/session"

export const projectsQueryKey = ["projects"] as const
export const projectQueryKey = (projectId: string) =>
  ["projects", projectId] as const
export const projectSettingsQueryKey = (projectId: string) =>
  ["projects", projectId, "settings"] as const
export const projectCostsQueryKey = (projectId: string) =>
  ["projects", projectId, "costs"] as const

/**
 * The registry, joined with what each project is doing — the canonical
 * `["projects"]` read every consumer shares.
 *
 * `VITE_USE_MOCK=true` reads the mutable seed store (the only way a freshly
 * created project sticks across refetches) and joins each row with the run
 * and cost reports so `activeRuns` / `totalRuns` / `spendToday` are real
 * numbers, not zeros.
 *
 * `VITE_USE_MOCK=false` calls the host **once, with `includeArchived: true`**
 * — the widest read any consumer needs. The cache keeps archived rows so
 * identity can still name grants against them; screen-facing hooks filter
 * them out through their `select`, so no consumer sees a row it did not
 * see before this cache was shared. The wire `ProjectView[]` carries
 * `id` / `slug` / `name` / `gitProfileRepo` / `createdAt` only — the derived
 * columns collapse to the honest defaults (`0`, `0`, `null`). The screen
 * already knows how to render those, and the `kubb-client` test pins the
 * "no real call without VITE_API_BASE_URL" contract.
 */
async function listProjects(): Promise<ProjectRow[]> {
  if (env.useMock) {
    return buildProjectRows(listSeedProjects(), listSeedRuns(), COST_SEED.byApp)
  }
  const views = await getApiV1Projects({ includeArchived: true })
  return mapProjectsPageToSummaries(views)
}

/**
 * One project, by id. The detail page could read from the list cache (the
 * runs PR does, because there is no detail endpoint yet) — but the host
 * has a real `GET /api/v1/projects/{id}`, so we call it instead and let
 * the list cache serve the registry. When the row is missing the host
 * returns 404 and the kubb-client throws; the screen renders that as
 * the "no project with that id" empty state.
 */
async function getProject(projectId: string): Promise<ProjectRow> {
  if (env.useMock) {
    const seed = listSeedProjects().find((entry) => entry.id === projectId)
    if (!seed) {
      throw new Error(`project ${projectId} not found`)
    }
    const joined = buildProjectRows([seed], listSeedRuns(), COST_SEED.byApp)
    return joined[0] ?? toProjectRow(seed)
  }
  const view = await getApiV1ProjectsProjectid(projectId)
  return mapProjectViewToDetail(view)
}

/**
 * One project's settings. The mock layer keeps no per-project settings
 * seed, so the mock path returns a sensible default (1 idle, 8 max,
 * approval on, soft budget unset). When the settings seed grows per-
 * project numbers, this function reads from it the way `mockProjectCosts`
 * reads from the cost seed.
 */
async function getProjectSettings(projectId: string): Promise<ProjectSettings> {
  if (env.useMock) {
    return {
      projectId,
      minIdle: 1,
      maxConcurrent: 8,
      idleTtlSeconds: 300,
      approveRequired: true,
      knowledgeEnabled: true,
      verifyEnabled: true,
      proxyEnabled: false,
      softBudgetUsdMicros: null,
      hardBudgetUsdMicros: null,
      version: 1,
      updatedAt: "2026-09-04T00:00:00.000+00:00",
    }
  }
  const view = await getApiV1ProjectsProjectidSettings(projectId)
  return mapProjectSettingsViewToSettings(view)
}

/**
 * One project's cost rollup — the soft/hard cap and a feed of recent usage.
 * Mock mode returns an empty feed (`recent: []`); the panel already knows
 * "no data yet" is a valid answer, and the seed does not include per-
 * project usage events. Budget caps stay `null` until the mock layer grows
 * a project→budget mapping.
 */
async function getProjectCosts(projectId: string): Promise<ProjectCostSummary> {
  if (env.useMock) {
    return {
      projectId,
      spentUsd: 0,
      softBudgetUsd: null,
      hardBudgetUsd: null,
      softExceeded: false,
      hardExceeded: false,
      recent: [],
    }
  }
  const view = await getApiV1ProjectsProjectidCosts(projectId)
  return mapCostsPageToCostSummary(view)
}

/**
 * `useCreateProjectMutation` lives in `mutations.ts` — the page that submits
 * (`create-project-page.tsx`) imports it from there.
 */

/** How long the registry stays fresh: five minutes. */
const PROJECTS_STALE_TIME_MS = 5 * 60 * 1000

/**
 * The one options object behind the `["projects"]` cache — the registry
 * read the whole dashboard shares. The hook below, the session hook and
 * any queryFn that needs the registry mid-flight (`identity`'s
 * `loadIdentityReal`) all go through this, so there is exactly one
 * `/api/v1/projects` round trip per staleness window no matter how many
 * consumers mount.
 *
 * The contract, in one place:
 * - **One key** — `["projects"]`, holding `ProjectRow[]` *including*
 *   archived rows (the widest read any consumer needs).
 * - **One request** — consumers project through `select` /
 *   `ensureQueryData`; none of them owns a private registry fetch.
 * - **Five-minute staleness** — the registry changes only through the
 *   mutations below, which invalidate the key; focus refetch stays off
 *   (the client-wide default) and remounts read the cache instead of
 *   the wire.
 * - **Mutations settle it by invalidation** — create / rename / delete
 *   in `mutations.ts` invalidate `["projects"]`, so the refetch lands
 *   the new state in every consumer at once, session selects included.
 * - **Mock branch** — `VITE_USE_MOCK=true` reads the mutable seed store
 *   joined with runs and costs; no wire, no kubb transport.
 *
 * Mirrors `meQueryOptions()` in the identity domain: one options object,
 * shared by a hook and a non-React reader, is the established pattern for
 * "one endpoint, one cache entry".
 */
export function projectsQueryOptions() {
  return {
    queryKey: projectsQueryKey,
    queryFn: listProjects,
    staleTime: PROJECTS_STALE_TIME_MS,
  }
}

/** The registry rows a screen shows: everything the host has not archived. */
function selectActiveProjectRows(rows: ProjectRow[]): ProjectRow[] {
  return rows.filter((row) => !row.archived)
}

/** The session's pick list: the active registry, narrowed to `ProjectRef`. */
function selectSessionProjects(rows: ProjectRow[]): ProjectRef[] {
  return mapProjectRowsToProjectRefs(selectActiveProjectRows(rows))
}

export function useProjectsQuery() {
  return useQuery({
    ...projectsQueryOptions(),
    select: selectActiveProjectRows,
  })
}

export function useProjectQuery(projectId: string) {
  return useQuery({
    queryKey: projectQueryKey(projectId),
    queryFn: () => getProject(projectId),
    enabled: projectId.length > 0,
  })
}

export function useProjectSettingsQuery(projectId: string) {
  return useQuery({
    queryKey: projectSettingsQueryKey(projectId),
    queryFn: () => getProjectSettings(projectId),
    enabled: projectId.length > 0,
  })
}

export function useProjectCostsQuery(projectId: string) {
  return useQuery({
    queryKey: projectCostsQueryKey(projectId),
    queryFn: () => getProjectCosts(projectId),
    enabled: projectId.length > 0,
  })
}

/**
 * The session's own read of the registry — the same `["projects"]` cache
 * the screens read, narrowed through `select` rather than refetched.
 *
 * `useSessionProjects` is mounted above every screen in the product (inside
 * the auth boot), so it answers synchronously and never suspends: mock mode
 * hands back the seed untouched, real mode returns the query's answer or an
 * empty list until one arrives — a session whose projects have not landed
 * yet is a session that offers no project to pick, which is honest rather
 * than broken. Sharing the screen's key means the boot's read and every
 * screen's read are one cache entry and one round trip, and a project
 * created mid-session lands here through the mutation's invalidation
 * without a reload.
 */
export function useSessionProjects(): ProjectRef[] {
  const query = useQuery({
    ...projectsQueryOptions(),
    select: selectSessionProjects,
    enabled: !env.useMock,
  })

  if (env.useMock) {
    return PROJECTS_SEED
  }
  return query.data ?? []
}

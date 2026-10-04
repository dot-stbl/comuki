import { useQuery } from "@tanstack/react-query"

import { env } from "@/shared/config/env"
import {
  findSeedProcedureLive,
  findSeedProcedureTrace,
  findSeedProcedureVersion,
} from "@/shared/api/mock"
import { getApiV1ProceduresProjectidProcedurekey } from "@/shared/api/_generated/clients/getApiV1ProceduresProjectidProcedurekey"
import { getApiV1ProceduresRunsRunidLive } from "@/shared/api/_generated/clients/getApiV1ProceduresRunsRunidLive"
import { getApiV1ProceduresRunsRunidTrace } from "@/shared/api/_generated/clients/getApiV1ProceduresRunsRunidTrace"
import { getApiV1ProceduresVersionsVersionid } from "@/shared/api/_generated/clients/getApiV1ProceduresVersionsVersionid"
import type { GetApiV1ProceduresProjectidProcedurekeyQueryResponse } from "@/shared/api/_generated/types/GetApiV1ProceduresProjectidProcedurekey"
import type { GetApiV1ProceduresRunsRunidLiveQueryResponse } from "@/shared/api/_generated/types/GetApiV1ProceduresRunsRunidLive"
import type { GetApiV1ProceduresRunsRunidTraceQueryResponse } from "@/shared/api/_generated/types/GetApiV1ProceduresRunsRunidTrace"
import type { GetApiV1ProceduresVersionsVersionidQueryResponse } from "@/shared/api/_generated/types/GetApiV1ProceduresVersionsVersionid"

import {
  mapProcedureLiveResponse,
  mapProcedureTraceResponse,
  mapProcedureVersionResponse,
} from "@/domains/procedures/api/mappers"
import { PROCEDURES_SEED } from "@/shared/api/mock/procedures.seed"
import type {
  ProcedureLive,
  ProcedureTrace,
  ProcedureVersion,
} from "@/domains/procedures/model/types"

/**
 * Query keys the Procedures screen shares. TanStack Query treats keys
 * as dependency arrays — bumping the version id or mode invalidates the
 * match by virtue of the new tuple.
 */
export const procedureVersionQueryKey = (projectId: string, procedureKey: string) =>
  ["procedures", projectId, procedureKey, "version"] as const

export const procedureCompiledVersionQueryKey = (versionId: string) =>
  ["procedures", "version", versionId] as const

export const procedureTraceQueryKey = (runId: string) =>
  ["procedures", "trace", runId] as const

export const procedureLiveQueryKey = (runId: string) =>
  ["procedures", "live", runId] as const

/**
 * Read the latest compiled version of a procedure. `404` is the
 * ordinary answer for a procedure that has never been published —
 * the dashboard renders an "unpublished" card, not an error banner.
 * `null` is the typed shape the screen handles.
 */
async function loadLatestVersion(
  projectId: string,
  procedureKey: string,
): Promise<ProcedureVersion | null> {
  if (env.useMock) {
    return findSeedProcedureVersion(projectId, procedureKey)
  }
  try {
    const wire = (await getApiV1ProceduresProjectidProcedurekey(
      projectId,
      procedureKey,
    )) as GetApiV1ProceduresProjectidProcedurekeyQueryResponse
    return mapProcedureVersionResponse(wire)
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

async function loadCompiledVersion(
  versionId: string,
): Promise<ProcedureVersion | null> {
  if (env.useMock) {
    return (
      PROCEDURES_SEED.find((seed) => seed.version.versionId === versionId)
        ?.version ?? null
    )
  }
  try {
    const wire = (await getApiV1ProceduresVersionsVersionid(
      versionId,
    )) as GetApiV1ProceduresVersionsVersionidQueryResponse
    return mapProcedureVersionResponse(wire)
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
 * Read the planned-vs-observed trace for a run. `null` when the run
 * has not been admitted against a procedure — the workbench renders
 * the empty trace as "no pin yet", not an error.
 */
async function loadTrace(runId: string): Promise<ProcedureTrace | null> {
  if (env.useMock) {
    return findSeedProcedureTrace(runId)
  }
  try {
    const wire = (await getApiV1ProceduresRunsRunidTrace(
      runId,
    )) as GetApiV1ProceduresRunsRunidTraceQueryResponse
    return mapProcedureTraceResponse(wire)
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
 * Read the Live run panel's composed projection — pinned version +
 * timeline + drift. `null` when the run has no trace (unpinned
 * default); the workbench renders the empty state, not an error.
 */
async function loadLive(runId: string): Promise<ProcedureLive | null> {
  if (env.useMock) {
    return findSeedProcedureLive(runId)
  }
  try {
    const wire = (await getApiV1ProceduresRunsRunidLive(
      runId,
    )) as GetApiV1ProceduresRunsRunidLiveQueryResponse
    return mapProcedureLiveResponse(wire)
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

/** Latest-version query — keyed by (project, procedure). */
export function useLatestProcedureVersionQuery(
  projectId: string,
  procedureKey: string,
) {
  return useQuery({
    queryKey: procedureVersionQueryKey(projectId, procedureKey),
    queryFn: () => loadLatestVersion(projectId, procedureKey),
    enabled: projectId.length > 0 && procedureKey.length > 0,
  })
}

/** Compiled-version-by-id query — the Studio canvas reads this when the
 * page is opened on a pinned version id. */
export function useCompiledProcedureVersionQuery(versionId: string) {
  return useQuery({
    queryKey: procedureCompiledVersionQueryKey(versionId),
    queryFn: () => loadCompiledVersion(versionId),
    enabled: versionId.length > 0,
  })
}

/** Planned-vs-observed trace query — the Replay panel reads this. */
export function useProcedureTraceQuery(runId: string) {
  return useQuery({
    queryKey: procedureTraceQueryKey(runId),
    queryFn: () => loadTrace(runId),
    enabled: runId.length > 0,
  })
}

/** Live run projection query — the Live run panel reads this. */
export function useProcedureLiveQuery(runId: string) {
  return useQuery({
    queryKey: procedureLiveQueryKey(runId),
    queryFn: () => loadLive(runId),
    enabled: runId.length > 0,
  })
}
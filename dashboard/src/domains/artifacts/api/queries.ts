import { useQuery } from "@tanstack/react-query"

import { mapVisualArtifactPageToPage } from "@/domains/artifacts/api/mappers"
import type {
  VisualArtifact,
  VisualArtifactFilters,
  VisualArtifactPage,
} from "@/domains/artifacts/model/types"
import { artifactsVisualList } from "@/shared/api/_generated/clients/artifactsVisualList"
import { env } from "@/shared/config/env"

import {
  ARTIFACTS_VISUAL_SEED,
  type SeedVisualArtifact,
} from "./seed"

/**
 * The single read against visual artifacts.
 *
 * One query, three surfaces: the run page passes `{ runId }`, the inbox
 * passes `{ ticketId }`, the chat pane intersects the artifact ids
 * against the project's list. The host's EF query accepts the same
 * filter arguments the kubb `ArtifactsVisualListQueryParams` declares;
 * the cubb client routes through `kubb-client.ts`, which carries the
 * cookie auth and rejects 401/403 — the same seam every kubb-shaped
 * read uses.
 *
 * Mock mode reads the seeded list (`api/seed.ts`). The seed is empty
 * today — slice 1 shipped the controller with a placeholder
 * `FetchAsync` that always returns `[]` (see
 * `VisualArtifactsControllerQueriesHelpers.FetchAsync`); slice 3 wires
 * the dashboard to render "no evidence yet" exactly as that empty list
 * dictates. When the EF query lands, this seed keeps the mock path
 * honest for fresh operators / tests.
 */
export const visualArtifactsQueryKey = (
  projectId: string,
  filters: VisualArtifactFilters = {},
) =>
  [
    "artifacts-visual",
    projectId,
    {
      runId: filters.runId ?? null,
      workItemId: filters.workItemId ?? null,
      ticketId: filters.ticketId ?? null,
    },
  ] as const

async function listVisualArtifacts(
  projectId: string,
  filters: VisualArtifactFilters,
): Promise<VisualArtifactPage> {
  if (env.useMock) {
    const filtered = ARTIFACTS_VISUAL_SEED.filter(
      (entry) => matchesMockFilters(entry, projectId, filters),
    )
    return { projectId, items: filtered.map(seedToDomain) }
  }
  // Wire accepts only `runId` and `workItemId` — no `ticketId` yet
  // (issue #51 slices 2 / 4 may add it). The `ticketId` filter is a
  // client-side narrowing on the full project page. Cheap on a project
  // page; the dashboard should defer the round-trip cost by widening
  // the wire filter the day volumes matter.
  const page = await artifactsVisualList(projectId, {
    runId: filters.runId,
    workItemId: filters.workItemId,
  })
  const mapped = mapVisualArtifactPageToPage(page, projectId)
  if (filters.ticketId === undefined) {
    return mapped
  }
  return {
    projectId,
    items: mapped.items.filter((entry) => entry.ticketId === filters.ticketId),
  }
}

export function useVisualArtifactsQuery(
  projectId: string,
  filters: VisualArtifactFilters = {},
) {
  return useQuery({
    queryKey: visualArtifactsQueryKey(projectId, filters),
    queryFn: () => listVisualArtifacts(projectId, filters),
    enabled: projectId.length > 0,
  })
}

/**
 * The URL the browser fetches to render an artifact's body.
 *
 * Constructed client-side rather than going through the kubb content
 * hook because the dashboard embeds it in `<img src>` and
 * `<iframe src>` — a JS-driven fetch followed by `URL.createObjectURL`
 * would lock the same MIME out of `<img>`'s native loading and force a
 * CSP-allowlisted connection back to the dashboard origin (which the
 * iframe sandbox + CSP stack forbids). The proxy is cookie-authed, so
 * `<img>` riding the cookie is the path the issue clears.
 *
 * The base URL is `import.meta.env.VITE_API_BASE_URL` (see
 * `shared/config/env.ts`); empty in mock mode, where the URL itself is
 * the artefact preview path that never resolves.
 */
export function visualArtifactContentUrl(
  baseUrl: string,
  projectId: string,
  artifactId: string,
): string {
  const trimmed = baseUrl.replace(/\/+$/, "")
  return `${trimmed}/api/v1/projects/${projectId}/artifacts/${artifactId}/content`
}

/* --- mock-side helpers (file-static, see class-layout-and-tooling §1a). --- */

function seedToDomain(entry: SeedVisualArtifact): VisualArtifact {
  return {
    id: entry.id,
    projectId: entry.projectId,
    filename: entry.filename,
    contentType: entry.contentType,
    sizeBytes: entry.sizeBytes,
    title: entry.title ?? null,
    createdAt: entry.createdAt,
    createdBy: entry.createdBy,
    runId: entry.runId ?? null,
    workItemId: entry.workItemId ?? null,
    sessionId: entry.sessionId ?? null,
    ticketId: entry.ticketId ?? null,
    version: entry.version,
  }
}

function matchesMockFilters(
  entry: SeedVisualArtifact,
  projectId: string,
  filters: VisualArtifactFilters,
): boolean {
  if (entry.projectId !== projectId) {
    return false
  }
  if (filters.runId !== undefined && entry.runId !== filters.runId) {
    return false
  }
  if (
    filters.workItemId !== undefined &&
    entry.workItemId !== filters.workItemId
  ) {
    return false
  }
  if (filters.ticketId !== undefined && entry.ticketId !== filters.ticketId) {
    return false
  }
  return true
}

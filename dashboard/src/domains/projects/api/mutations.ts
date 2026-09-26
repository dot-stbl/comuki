import { useMutation, useQueryClient } from "@tanstack/react-query"
import type { UseMutationResult } from "@tanstack/react-query"

import {
  mapCreateProjectInputToCreateRequest,
  mapProjectSettingsToUpdateRequest,
  mapProjectSettingsViewToSettings,
  mapProjectUpdateToUpdateRequest,
  mapProjectViewToDetail,
  toProjectRow,
} from "@/domains/projects/api/mappers"
import type {
  CreateProjectInput,
  ProjectRow,
  ProjectSettings,
  ProjectUpdate,
} from "@/domains/projects/model/types"
import { deleteApiV1ProjectsProjectid } from "@/shared/api/_generated/clients/deleteApiV1ProjectsProjectid"
import { patchApiV1ProjectsProjectid } from "@/shared/api/_generated/clients/patchApiV1ProjectsProjectid"
import { postApiV1Projects } from "@/shared/api/_generated/clients/postApiV1Projects"
import { putApiV1ProjectsProjectidSettings } from "@/shared/api/_generated/clients/putApiV1ProjectsProjectidSettings"
import { createSeedProject } from "@/shared/api/mock/projects.store"
import { env } from "@/shared/config/env"

import {
  projectsQueryKey,
  projectCostsQueryKey,
  projectQueryKey,
  projectSettingsQueryKey,
} from "./queries"

/**
 * Mutations on the project registry.
 *
 * Mock mode writes to the shared seed store so a freshly created project
 * sticks across refetches (the same reason `runs.store.ts` exists for the
 * runs domain). Real mode routes through the kubb-generated clients.
 *
 * Every registry write settles the shared `["projects"]` cache by
 * **invalidation**, not by splicing rows: the refetch lands the host's
 * truth in every consumer at once — the screen's rows, the session's
 * project picks and identity's registry — and in mock mode the refetch
 * reads the seed store the mutation just wrote. The invalidation is
 * `exact`, so per-project detail / settings / costs entries are left to
 * the mutations that actually concern them.
 *
 * Only `createProject` has a mock-mode path today — the seed store grows
 * update/delete/settings operations in a later slice. Calling the other
 * three in mock mode throws the kubb-client's `VITE_API_BASE_URL is not
 * set` error, which is the same readable message a screen would render
 * for any other unwired endpoint. All four endpoints exist on the host
 * (`ProjectsModuleEndpoints` + the costs endpoint), so there is no
 * deferred-mutation throw here — the runs PR's `decision` endpoints are
 * the ones that pattern was written for.
 */

async function createProject(input: CreateProjectInput): Promise<ProjectRow> {
  if (env.useMock) {
    return toProjectRow(
      createSeedProject({
        name: input.name,
        slug: input.slug,
        gitProfileRepo: input.gitProfileRepo,
        icon: input.icon,
        color: input.color,
        tags: input.tags,
      })
    )
  }
  return mapProjectViewToDetail(
    await postApiV1Projects(mapCreateProjectInputToCreateRequest(input))
  )
}

async function updateProject(
  projectId: string,
  patch: ProjectUpdate
): Promise<ProjectRow> {
  // The body's shape — including the absent-vs-empty tags distinction the
  // wire makes for the one list field (design D5) — lives in the mapper,
  // where it is pinned by tests rather than by reading this call site.
  return mapProjectViewToDetail(
    await patchApiV1ProjectsProjectid(
      projectId,
      mapProjectUpdateToUpdateRequest(patch)
    )
  )
}

async function updateSettings(
  projectId: string,
  settings: ProjectSettings
): Promise<ProjectSettings> {
  return mapProjectSettingsViewToSettings(
    await putApiV1ProjectsProjectidSettings(
      projectId,
      mapProjectSettingsToUpdateRequest(settings)
    )
  )
}

async function deleteProject(projectId: string): Promise<void> {
  await deleteApiV1ProjectsProjectid(projectId)
}

export function useCreateProjectMutation(): UseMutationResult<
  ProjectRow,
  Error,
  CreateProjectInput,
  unknown
> {
  const client = useQueryClient()

  return useMutation({
    mutationFn: createProject,
    onSuccess: async () => {
      await client.invalidateQueries({
        queryKey: projectsQueryKey,
        exact: true,
      })
    },
  })
}

export function useUpdateProjectMutation(): UseMutationResult<
  ProjectRow,
  Error,
  { projectId: string; patch: ProjectUpdate },
  unknown
> {
  const client = useQueryClient()

  return useMutation({
    mutationFn: ({
      projectId,
      patch,
    }: {
      projectId: string
      patch: ProjectUpdate
    }) => updateProject(projectId, patch),
    onSuccess: async (_row, { projectId }) => {
      // The rename shows in the list and in that project's own detail
      // entry; settings and costs say nothing about the name and stay.
      await Promise.all([
        client.invalidateQueries({
          queryKey: projectsQueryKey,
          exact: true,
        }),
        client.invalidateQueries({
          queryKey: projectQueryKey(projectId),
          exact: true,
        }),
      ])
    },
  })
}

export function useUpdateProjectSettingsMutation(): UseMutationResult<
  ProjectSettings,
  Error,
  { projectId: string; settings: ProjectSettings },
  unknown
> {
  const client = useQueryClient()

  return useMutation({
    mutationFn: ({
      projectId,
      settings,
    }: {
      projectId: string
      settings: ProjectSettings
    }) => updateSettings(projectId, settings),
    onSuccess: (next, { projectId }) => {
      client.setQueryData(projectSettingsQueryKey(projectId), next)
    },
  })
}

export function useDeleteProjectMutation(): UseMutationResult<
  void,
  Error,
  { projectId: string },
  unknown
> {
  const client = useQueryClient()

  return useMutation({
    mutationFn: ({ projectId }: { projectId: string }) =>
      deleteProject(projectId),
    onSuccess: async (_void, { projectId }) => {
      await client.invalidateQueries({
        queryKey: projectsQueryKey,
        exact: true,
      })
      client.removeQueries({ queryKey: projectQueryKey(projectId) })
      client.removeQueries({ queryKey: projectSettingsQueryKey(projectId) })
      client.removeQueries({ queryKey: projectCostsQueryKey(projectId) })
    },
  })
}

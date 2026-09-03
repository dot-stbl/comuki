import { useMutation, useQueryClient } from "@tanstack/react-query"

import {
  mapCreateProjectInputToCreateRequest,
  mapProjectSettingsToUpdateRequest,
  mapProjectSettingsViewToSettings,
  mapProjectViewToDetail,
  toProjectRow,
} from "@/domains/projects/api/mappers"
import type {
  CreateProjectInput,
  ProjectRow,
  ProjectSettings,
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
    const created = createSeedProject({
      name: input.name,
      slug: input.slug,
      gitProfileRepo: input.gitProfileRepo,
    })
    return toProjectRow(created)
  }
  const view = await postApiV1Projects(
    mapCreateProjectInputToCreateRequest(input)
  )
  return mapProjectViewToDetail(view)
}

async function updateProject(
  projectId: string,
  patch: ProjectUpdate
): Promise<ProjectRow> {
  const view = await patchApiV1ProjectsProjectid(projectId, {
    name: patch.name,
    description: patch.description,
    profilesGitUrl: null,
    profilesGitRef: null,
  })
  return mapProjectViewToDetail(view)
}

async function updateSettings(
  projectId: string,
  settings: ProjectSettings
): Promise<ProjectSettings> {
  const view = await putApiV1ProjectsProjectidSettings(
    projectId,
    mapProjectSettingsToUpdateRequest(settings)
  )
  return mapProjectSettingsViewToSettings(view)
}

async function deleteProject(projectId: string): Promise<void> {
  await deleteApiV1ProjectsProjectid(projectId)
}

/** The fields a `PATCH /api/v1/projects/{id}` accepts. Mirrors the wire DTO. */
export interface ProjectUpdate {
  name: string | null
  description: string | null
}

export function useCreateProjectMutation() {
  const client = useQueryClient()

  return useMutation({
    mutationFn: createProject,
    onSuccess: (created) => {
      client.setQueryData<ProjectRow[]>(projectsQueryKey, (rows) =>
        rows ? [...rows, created] : [created]
      )
    },
  })
}

export function useUpdateProjectMutation() {
  const client = useQueryClient()

  return useMutation({
    mutationFn: ({
      projectId,
      patch,
    }: {
      projectId: string
      patch: ProjectUpdate
    }) => updateProject(projectId, patch),
    onSuccess: (row, { projectId }) => {
      client.setQueryData<ProjectRow[]>(projectsQueryKey, (rows) =>
        rows?.map((existing) => (existing.id === projectId ? row : existing))
      )
      client.setQueryData(projectQueryKey(projectId), row)
    },
  })
}

export function useUpdateProjectSettingsMutation() {
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

export function useDeleteProjectMutation() {
  const client = useQueryClient()

  return useMutation({
    mutationFn: ({ projectId }: { projectId: string }) =>
      deleteProject(projectId),
    onSuccess: (_void, { projectId }) => {
      client.setQueryData<ProjectRow[]>(projectsQueryKey, (rows) =>
        rows?.filter((row) => row.id !== projectId)
      )
      client.removeQueries({ queryKey: projectQueryKey(projectId) })
      client.removeQueries({ queryKey: projectSettingsQueryKey(projectId) })
      client.removeQueries({ queryKey: projectCostsQueryKey(projectId) })
    },
  })
}

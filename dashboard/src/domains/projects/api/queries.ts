import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"

import { buildProjectRows } from "@/domains/projects/model/activity"
import type { CreateProjectInput, ProjectRow } from "@/domains/projects/model/types"
import { COST_SEED } from "@/shared/api/mock/cost.seed"
import {
  createSeedProject,
  listSeedProjects,
} from "@/shared/api/mock/projects.store"
import { listSeedRuns } from "@/shared/api/mock/runs.store"
import { env } from "@/shared/config/env"

export const projectsQueryKey = ["projects"] as const

/**
 * The registry, joined with what each project is doing.
 *
 * It reads the mutable store rather than the seed constant, which is the whole
 * reason that store exists: a `queryFn` that returned the constant would undo
 * the project the operator just created on the next refetch.
 */
async function listProjects(): Promise<ProjectRow[]> {
  if (!env.useMock) {
    throw new Error("projects API not implemented — set VITE_USE_MOCK=true")
  }
  return buildProjectRows(listSeedProjects(), listSeedRuns(), COST_SEED.byApp)
}

async function createProject(input: CreateProjectInput): Promise<ProjectRow[]> {
  if (!env.useMock) {
    throw new Error("projects API not implemented — set VITE_USE_MOCK=true")
  }
  createSeedProject({
    name: input.name,
    slug: input.slug,
    gitProfileRepo: input.gitProfileRepo,
  })
  return listProjects()
}

export function useProjectsQuery() {
  return useQuery({
    queryKey: projectsQueryKey,
    queryFn: listProjects,
  })
}

export function useCreateProjectMutation() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: createProject,
    onSuccess: (next) => {
      queryClient.setQueryData(projectsQueryKey, next)
    },
  })
}

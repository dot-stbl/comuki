import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"

import { deleteApiV1ProjectsProjectidScheduledJobsJobid } from "@/shared/api/_generated/clients/deleteApiV1ProjectsProjectidScheduledJobsJobid"
import { getApiV1ProjectsProjectidScheduledJobs } from "@/shared/api/_generated/clients/getApiV1ProjectsProjectidScheduledJobs"
import { patchApiV1ProjectsProjectidScheduledJobsJobid } from "@/shared/api/_generated/clients/patchApiV1ProjectsProjectidScheduledJobsJobid"
import { postApiV1ProjectsProjectidScheduledJobs } from "@/shared/api/_generated/clients/postApiV1ProjectsProjectidScheduledJobs"
import type { ScheduledJobView } from "@/shared/api/_generated/types/ScheduledJobView"
import { listSeedProjects } from "@/shared/api/mock/projects.store"
import { env } from "@/shared/config/env"

/**
 * The scheduled-jobs surface — the per-project cron / one-shot admission
 * source (`GET/POST/PATCH/DELETE /api/v1/projects/{projectId}/scheduled-jobs`).
 * The kubb clients for all four endpoints have existed since the backend
 * slice landed; this file is the first FE consumer.
 *
 * Backend permissions are `scheduler:read` / `scheduler:write`; the
 * dashboard's session vocabulary has no scheduler keys yet, so the section
 * on the project page gates on the neighbouring intake permissions
 * (`sources.view` to read, `sources.edit` to act) until the matrix grows.
 */

/**
 * One scheduled job, as the project page renders it.
 *
 * A deliberate 1:1 with the wire `ScheduledJobView` — the mapping exists so
 * no screen imports a kubb type (the domain invariant), not because
 * anything is reshaped. Timestamps stay ISO strings; the page formats them
 * the way it formats every other date it shows.
 */
export interface ScheduledJob {
  id: string
  projectId: string
  cronExpression: string
  profileKey: string
  briefJson: string
  runOnOnceAt: string | null
  enabled: boolean
  lastFiredAt: string | null
  nextFireAt: string
  createdAt: string
  updatedAt: string
}

function viewToJob(view: ScheduledJobView): ScheduledJob {
  return {
    id: view.id,
    projectId: view.projectId,
    cronExpression: view.cronExpression,
    profileKey: view.profileKey,
    briefJson: view.briefJson,
    runOnOnceAt: view.runOnOnceAt,
    enabled: view.enabled,
    lastFiredAt: view.lastFiredAt,
    nextFireAt: view.nextFireAt,
    createdAt: view.createdAt,
    updatedAt: view.updatedAt,
  }
}

export const scheduledJobsQueryKey = (projectId: string) =>
  ["scheduled-jobs", projectId] as const

/* ---------------------------------------------------------------------------
 * Mock store — one mutable list, seeded from the projects seed so every
 * project's page has something to show in dev:mock and storybook. The same
 * reason `sources.store` exists: a queryFn returning a constant undoes a
 * write on the next refetch.
 * ------------------------------------------------------------------------- */

const SEED_JOBS: ScheduledJob[] = listSeedProjects().flatMap((project, index) => [
  {
    id: `sj_nightly_${index + 1}`,
    projectId: project.id,
    cronExpression: "0 3 * * *",
    profileKey: "implementer",
    briefJson: '{"title":"Nightly dependency sweep"}',
    runOnOnceAt: null,
    enabled: true,
    lastFiredAt: "2026-09-12T03:00:04Z",
    nextFireAt: "2026-09-13T03:00:00Z",
    createdAt: "2026-08-30T09:12:00Z",
    updatedAt: "2026-09-10T14:02:00Z",
  },
  ...(index === 0
    ? [
        {
          id: "sj_paused_1",
          projectId: project.id,
          cronExpression: "*/30 * * * *",
          profileKey: "reviewer",
          briefJson: '{"title":"Re-verify open diffs"}',
          runOnOnceAt: null,
          enabled: false,
          lastFiredAt: "2026-09-08T10:30:00Z",
          nextFireAt: "2026-09-08T11:00:00Z",
          createdAt: "2026-08-30T09:12:00Z",
          updatedAt: "2026-09-08T10:41:00Z",
        } satisfies ScheduledJob,
      ]
    : []),
])

let jobsStore: ScheduledJob[] = [...SEED_JOBS]

/** Test contract — restores the pristine seed between module-scoped cases. */
export function resetSeedScheduledJobs(): void {
  jobsStore = [...SEED_JOBS]
}

function listSeedJobs(projectId: string): ScheduledJob[] {
  return jobsStore
    .filter((job) => job.projectId === projectId)
    .sort((left, right) => (left.createdAt < right.createdAt ? 1 : -1))
}

const LATENCY = 220

function wait(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, LATENCY))
}

/**
 * The list for one project. The host pages this endpoint; the page does not
 * promote a pager into the UI (a project with more than a hundred cron
 * entries is a platform incident, not a screen state), so we read the first
 * page at the endpoint's own default size.
 */
async function listScheduledJobs(projectId: string): Promise<ScheduledJob[]> {
  if (env.useMock) {
    await wait()
    return listSeedJobs(projectId)
  }
  const page = await getApiV1ProjectsProjectidScheduledJobs(projectId)
  return page.items.map(viewToJob)
}

export function useScheduledJobsQuery(projectId: string) {
  return useQuery({
    queryKey: scheduledJobsQueryKey(projectId),
    queryFn: () => listScheduledJobs(projectId),
    enabled: projectId.length > 0,
  })
}

export interface CreateScheduledJobInput {
  projectId: string
  cronExpression: string
  profileKey: string
  /** Stored verbatim — nothing between the textarea and the host touches it. */
  briefJson: string
}

/**
 * Schedule a new job. No optimistic write: the id and the `nextFireAt`
 * computation are the server's to mint, and a row invented here would be
 * replaced by a differently-identified one on the refetch.
 */
export function useCreateScheduledJobMutation() {
  const queryClient = useQueryClient()

  return useMutation<ScheduledJob, Error, CreateScheduledJobInput>({
    mutationFn: async (input) => {
      if (env.useMock) {
        await wait()
        const created: ScheduledJob = {
          id: `sj_${Date.now()}`,
          projectId: input.projectId,
          cronExpression: input.cronExpression,
          profileKey: input.profileKey,
          briefJson: input.briefJson,
          runOnOnceAt: null,
          enabled: true,
          lastFiredAt: null,
          nextFireAt: new Date(Date.now() + 3600_000).toISOString(),
          createdAt: new Date().toISOString(),
          updatedAt: new Date().toISOString(),
        }
        jobsStore = [created, ...jobsStore]
        return created
      }
      const view = await postApiV1ProjectsProjectidScheduledJobs(
        input.projectId,
        {
          cronExpression: input.cronExpression,
          profileKey: input.profileKey,
          briefJson: input.briefJson,
        },
      )
      return viewToJob(view)
    },
    onSettled: async (_data, _error, input) => {
      await queryClient.invalidateQueries({
        queryKey: scheduledJobsQueryKey(input.projectId),
      })
    },
  })
}

export interface SetScheduledJobEnabledInput {
  projectId: string
  jobId: string
  enabled: boolean
}

/**
 * Enable or disable a job — the one act an operator performs routinely
 * (pause the cron while an incident is triaged, resume after). A partial
 * update on the wire (`PATCH { enabled }`); the mock store flips the row.
 */
export function useSetScheduledJobEnabledMutation() {
  const queryClient = useQueryClient()

  return useMutation<ScheduledJob, Error, SetScheduledJobEnabledInput>({
    mutationFn: async (input) => {
      if (env.useMock) {
        await wait()
        const job = jobsStore.find((entry) => entry.id === input.jobId)
        if (!job) {
          throw new Error(`scheduled job ${input.jobId} not found`)
        }
        job.enabled = input.enabled
        job.updatedAt = new Date().toISOString()
        return { ...job }
      }
      const view = await patchApiV1ProjectsProjectidScheduledJobsJobid(
        input.projectId,
        input.jobId,
        { enabled: input.enabled },
      )
      return viewToJob(view)
    },
    onSettled: async (_data, _error, input) => {
      await queryClient.invalidateQueries({
        queryKey: scheduledJobsQueryKey(input.projectId),
      })
    },
  })
}

export interface DeleteScheduledJobInput {
  projectId: string
  jobId: string
}

/**
 * Remove a schedule. The host deletes idempotently (a missing id is a
 * no-op), so the mutation never fights a double-click.
 */
export function useDeleteScheduledJobMutation() {
  const queryClient = useQueryClient()

  return useMutation<unknown, Error, DeleteScheduledJobInput>({
    mutationFn: async (input) => {
      if (env.useMock) {
        await wait()
        jobsStore = jobsStore.filter((entry) => entry.id !== input.jobId)
        return input.jobId
      }
      await deleteApiV1ProjectsProjectidScheduledJobsJobid(
        input.projectId,
        input.jobId,
      )
      return input.jobId
    },
    onSettled: async (_data, _error, input) => {
      await queryClient.invalidateQueries({
        queryKey: scheduledJobsQueryKey(input.projectId),
      })
    },
  })
}

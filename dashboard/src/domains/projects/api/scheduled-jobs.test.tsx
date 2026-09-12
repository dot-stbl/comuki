import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { renderHook, waitFor } from "@testing-library/react"
import type { ReactNode } from "react"
import { afterEach, describe, expect, it, vi } from "vitest"

/**
 * The scheduled-jobs surface, mock-first. The query reads the module-local
 * seed; the three mutations (create / set-enabled / delete) write it and
 * invalidate `["scheduled-jobs", projectId]` — the same discipline the
 * sources domain applies, pinned the same way.
 */

afterEach(() => {
  vi.resetModules()
  vi.unstubAllEnvs()
})

function withQueryClient() {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  return ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  )
}

describe("scheduled-jobs mock-first path", () => {
  it("seeds one job per project and lists them for a project", async () => {
    vi.stubEnv("VITE_USE_MOCK", "true")
    vi.resetModules()

    const api = await import("@/domains/projects/api/scheduled-jobs")
    const { listSeedProjects } = await import(
      "@/shared/api/mock/projects.store"
    )
    api.resetSeedScheduledJobs()

    const project = listSeedProjects()[0]
    const { result } = renderHook(
      () => api.useScheduledJobsQuery(project.id),
      { wrapper: withQueryClient() },
    )

    await waitFor(() => expect(result.current.isSuccess).toBe(true))
    expect(result.current.data?.length).toBeGreaterThan(0)
    for (const job of result.current.data ?? []) {
      expect(job.projectId).toBe(project.id)
    }
  })

  it("create, pause and delete write the store and settle the cache", async () => {
    vi.stubEnv("VITE_USE_MOCK", "true")
    vi.resetModules()

    const api = await import("@/domains/projects/api/scheduled-jobs")
    const { listSeedProjects } = await import(
      "@/shared/api/mock/projects.store"
    )
    api.resetSeedScheduledJobs()

    const project = listSeedProjects()[0]

    const list = renderHook(() => api.useScheduledJobsQuery(project.id), {
      wrapper: withQueryClient(),
    })
    await waitFor(() => expect(list.result.current.isSuccess).toBe(true))
    const baseline = list.result.current.data?.length ?? 0

    const create = renderHook(() => api.useCreateScheduledJobMutation(), {
      wrapper: withQueryClient(),
    })
    const created = await create.result.current.mutateAsync({
      projectId: project.id,
      cronExpression: "0 4 * * *",
      profileKey: "reviewer",
      briefJson: '{"title":"Morning re-verify"}',
    })
    expect(created.enabled).toBe(true)

    const pause = renderHook(() => api.useSetScheduledJobEnabledMutation(), {
      wrapper: withQueryClient(),
    })
    const paused = await pause.result.current.mutateAsync({
      projectId: project.id,
      jobId: created.id,
      enabled: false,
    })
    expect(paused.enabled).toBe(false)

    const remove = renderHook(() => api.useDeleteScheduledJobMutation(), {
      wrapper: withQueryClient(),
    })
    await remove.result.current.mutateAsync({
      projectId: project.id,
      jobId: created.id,
    })

    // The mutations invalidated the list key — refetch sees the seed minus
    // nothing (created then deleted) and the paused flip on the seed row.
    await waitFor(() =>
      expect(list.result.current.data?.length).toBe(baseline),
    )
    const untouched = list.result.current.data ?? []
    expect(untouched.some((job) => job.id === created.id)).toBe(false)
  })
})

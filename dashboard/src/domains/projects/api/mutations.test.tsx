import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { renderHook, waitFor } from "@testing-library/react"
import type { ReactNode } from "react"
import { afterEach, describe, expect, it, vi } from "vitest"

/**
 * Mock-first contract for the projects mutations.
 *
 * Mock mode writes to the seed store so a freshly created project sticks
 * across refetches — the same reason `runs.store.ts` exists for the
 * runs domain. The test asserts the seed path stays reachable when
 * `VITE_USE_MOCK=true` and that every public mutation hook is exported.
 *
 * The four real-mode mutations (create / patch / settings PUT / delete)
 * all have backend endpoints; the runs PR's `decision` endpoints are
 * the ones we couldn't write, not these.
 */

afterEach(() => {
  vi.resetModules()
  vi.unstubAllEnvs()
})

function withQueryClient() {
  const client = new QueryClient({
    defaultOptions: { mutations: { retry: false } },
  })
  return ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  )
}

describe("mutations.ts mock-first path", () => {
  it("creates a project in the seed store when env.useMock is true", async () => {
    vi.stubEnv("VITE_USE_MOCK", "true")
    vi.resetModules()

    const mutations = await import("@/domains/projects/api/mutations")
    const { resetSeedProjects, listSeedProjects } =
      await import("@/shared/api/mock/projects.store")
    resetSeedProjects()
    const baseline = listSeedProjects().length

    const { result } = renderHook(() => mutations.useCreateProjectMutation(), {
      wrapper: withQueryClient(),
    })

    const created = await result.current.mutateAsync({
      name: "Vega clone",
      slug: "vega-clone",
      gitProfileRepo: null,
    })

    expect(created.id).toBe("p_vega_clone")
    expect(created.slug).toBe("vega-clone")
    expect(created.name).toBe("Vega clone")
    expect(created.activeRuns).toBe(0)
    expect(listSeedProjects().length).toBe(baseline + 1)

    await waitFor(() => expect(result.current.isSuccess).toBe(true))
  })

  it("exposes every public mutation hook", async () => {
    vi.stubEnv("VITE_USE_MOCK", "true")
    vi.resetModules()

    const mutations = await import("@/domains/projects/api/mutations")

    expect(typeof mutations.useCreateProjectMutation).toBe("function")
    expect(typeof mutations.useUpdateProjectMutation).toBe("function")
    expect(typeof mutations.useUpdateProjectSettingsMutation).toBe("function")
    expect(typeof mutations.useDeleteProjectMutation).toBe("function")
  })
})

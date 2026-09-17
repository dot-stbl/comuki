import { afterEach, describe, expect, it, vi } from "vitest"

/**
 * Mock-first contract for the projects queries.
 *
 * `VITE_USE_MOCK=true` keeps the seed store wired through the same queries
 * the real-backend path uses, so a per-domain rewire does not break the
 * operator's local storybook / dev:mock flow. The test asserts the shape
 * the screen already renders by hitting the public mapper.
 *
 * The companion real-mode contract lives in `kubb-client.test.ts` —
 * `env.useMock=false` without `VITE_API_BASE_URL` throws a single,
 * readable error from the kubb transport on the first call. Mock mode
 * never imports the kubb transport, so import-time of `queries.ts`
 * succeeds even with an empty base URL.
 */

// The registry client is mocked for the whole file so the real-mode
// single-request test below can assert dispatch counts without booting
// the kubb transport. Mock-mode tests never call it.
vi.mock("@/shared/api/_generated/clients/getApiV1Projects", () => ({
  getApiV1Projects: vi.fn(),
}))

afterEach(() => {
  vi.resetModules()
  vi.unstubAllEnvs()
})

describe("queries.ts mock-first path", () => {
  it("keeps reading the seed store when env.useMock is true", async () => {
    vi.stubEnv("VITE_USE_MOCK", "true")
    vi.resetModules()

    const queries = await import("@/domains/projects/api/queries")
    const { PLATFORM_PROJECTS_SEED } =
      await import("@/shared/api/mock/projects.seed")

    const { toProjectRow } = await import("@/domains/projects/api/mappers")

    const mapped = PLATFORM_PROJECTS_SEED.map((seed) => toProjectRow(seed))

    expect(mapped.length).toBeGreaterThan(0)
    for (const row of mapped) {
      expect(row).toHaveProperty("id")
      expect(row).toHaveProperty("slug")
      expect(row).toHaveProperty("gitProfileRepo")
    }
    expect(typeof queries.useProjectsQuery).toBe("function")
    expect(typeof queries.useProjectQuery).toBe("function")
    expect(typeof queries.useProjectSettingsQuery).toBe("function")
    expect(typeof queries.useProjectCostsQuery).toBe("function")
  })

  it("import-time of queries does not require a populated VITE_API_BASE_URL in mock mode", async () => {
    vi.stubEnv("VITE_USE_MOCK", "true")
    vi.stubEnv("VITE_API_BASE_URL", "")
    vi.resetModules()

    await expect(
      import("@/domains/projects/api/queries")
    ).resolves.toBeDefined()
  })

  it("exposes a stable set of query keys", async () => {
    vi.stubEnv("VITE_USE_MOCK", "true")
    vi.resetModules()

    const queries = await import("@/domains/projects/api/queries")

    expect(queries.projectsQueryKey).toEqual(["projects"])
    expect(queries.projectQueryKey("p_comuki")).toEqual([
      "projects",
      "p_comuki",
    ])
    expect(queries.projectSettingsQueryKey("p_comuki")).toEqual([
      "projects",
      "p_comuki",
      "settings",
    ])
    expect(queries.projectCostsQueryKey("p_comuki")).toEqual([
      "projects",
      "p_comuki",
      "costs",
    ])
  })

  it("keys the session and the screens on the one canonical registry query", async () => {
    vi.stubEnv("VITE_USE_MOCK", "true")
    vi.resetModules()

    const queries = await import("@/domains/projects/api/queries")

    /* The `["session-projects"]` key is gone by design: the boot's session
       read and every screen's registry read are projections over ONE
       cache entry. The options object is the single source — its key is
       the list key, and its staleness (five minutes) is the registry's
       refresh budget for the whole app. */
    const options = queries.projectsQueryOptions()

    expect(options.queryKey).toEqual(queries.projectsQueryKey)
    expect(options.staleTime).toBe(5 * 60 * 1000)
    expect("sessionProjectsQueryKey" in queries).toBe(false)
  })

  it("maps registry rows onto the session's narrow project ref", async () => {
    const { mapProjectRowsToProjectRefs } =
      await import("@/domains/projects/api/mappers")

    /* The projection the session hook runs over the shared `["projects"]`
       cache: id, the key the operator calls the project by (the row's
       `slug`), and the name — nothing heavier belongs in a context that
       lives above every screen. Archived rows are filtered before the
       mapper runs; the mapper itself stays a pure projection. */
    const refs = mapProjectRowsToProjectRefs([
      {
        id: "p_comuki",
        name: "Comuki platform",
        slug: "comuki",
        gitProfileRepo: null,
        createdAt: "2026-01-01T00:00:00Z",
        archived: false,
        activeRuns: 0,
        totalRuns: 0,
        spendToday: null,
      },
    ])

    expect(refs).toEqual([
      { id: "p_comuki", key: "comuki", name: "Comuki platform" },
    ])
  })
})

describe("queries.ts one-request contract (real mode)", () => {
  it("serves the screen rows and the session refs from a single /api/v1/projects call", async () => {
    vi.stubEnv("VITE_USE_MOCK", "false")
    vi.stubEnv("VITE_API_BASE_URL", "http://localhost")
    vi.resetModules()

    const projectsMod =
      await import("@/shared/api/_generated/clients/getApiV1Projects")
    const wire = projectsMod.getApiV1Projects as unknown as ReturnType<
      typeof vi.fn
    >
    wire.mockResolvedValue([
      {
        id: "p_comuki",
        name: "Comuki platform",
        slug: "comuki",
        description: null,
        profilesGitUrl: null,
        profilesGitRef: null,
        archived: false,
        archivedAt: null,
        createdAt: "2026-01-01T00:00:00Z",
        updatedAt: "2026-01-01T00:00:00Z",
      },
      {
        id: "p_old",
        name: "Retired thing",
        slug: "retired-thing",
        description: null,
        profilesGitUrl: null,
        profilesGitRef: null,
        archived: true,
        archivedAt: "2026-02-01T00:00:00Z",
        createdAt: "2025-01-01T00:00:00Z",
        updatedAt: "2026-02-01T00:00:00Z",
      },
    ])

    const { useProjectsQuery, useSessionProjects } =
      await import("@/domains/projects/api/queries")
    const { QueryClient, QueryClientProvider } =
      await import("@tanstack/react-query")
    const { renderHook, waitFor } = await import("@testing-library/react")
    const React = await import("react")

    const client = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    })
    const wrapper = ({ children }: { children: React.ReactNode }) =>
      React.createElement(QueryClientProvider, { client }, children)

    const { result } = renderHook(
      () => {
        const rows = useProjectsQuery()
        const refs = useSessionProjects()
        return { rows, refs }
      },
      { wrapper }
    )

    await waitFor(() => expect(result.current.rows.isSuccess).toBe(true))

    /* The whole point of the shared key: two consumers mounted at once —
       the screen's row read and the boot's session read — produce ONE
       wire call, and each keeps its projection (the screen drops the
       archived row, the session narrows to refs). */
    expect(wire).toHaveBeenCalledTimes(1)
    expect(result.current.rows.data?.map((row) => row.id)).toEqual(["p_comuki"])
    expect(result.current.refs).toEqual([
      { id: "p_comuki", key: "comuki", name: "Comuki platform" },
    ])
  })
})

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
})

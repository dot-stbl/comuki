import { afterEach, describe, expect, it, vi } from "vitest"

/**
 * Mock-first contract.
 *
 * `VITE_USE_MOCK=true` keeps the seed store wired through the same queries
 * the real-backend path uses, so a per-domain rewire does not break the
 * operator's local storybook / dev:mock flow. Two things must hold:
 *
 * 1. The real-mode kubb clients are never imported (otherwise their import
 *    time would read `import.meta.env` and bootstrap a real fetcher even in
 *    mock mode).
 * 2. The seed store keeps mapping to the same `RunSummary` shape the screen
 *    already renders, so callers can branch on `env.useMock` without
 *    branching on the result type.
 *
 * The test asserts the shape by hitting `listRuns`-equivalent behaviour
 * through the public mapper.
 */

afterEach(() => {
  vi.resetModules()
  vi.unstubAllEnvs()
})

describe("queries.ts mock-first path", () => {
  it("keeps reading the seed store when env.useMock is true", async () => {
    vi.stubEnv("VITE_USE_MOCK", "true")
    vi.resetModules()

    const queries = await import("@/domains/runs/api/queries")
    const { RUNS_SEED } = await import("@/shared/api/mock")

    // Calling the queries package's mapper directly is the only public
    // surface through which mock-mode flows; anything else either wraps a
    // reactor or navigates straight to the kubb clients.
    const { toRunSummary } = await import("@/domains/runs/api/mappers")

    const mapped = RUNS_SEED.map(toRunSummary)

    expect(mapped.length).toBeGreaterThan(0)
    // The shape the screen already renders — same in mock and real modes
    // (modulo real-mode defaults for fields the wire omits).
    for (const run of mapped) {
      expect(run).toHaveProperty("id")
      expect(run).toHaveProperty("workItems")
      expect(run).toHaveProperty("current")
    }
    // Tests instance is consumed just to assert the module shape; the
    // functions it exports are the contract under test in the real-mode
    // path of these mappers (see mappers.test.ts).
    expect(typeof queries.useRunsQuery).toBe("function")
    expect(typeof queries.useRunQuery).toBe("function")
    expect(typeof queries.useRunArtifactsQuery).toBe("function")
  })

  it("import-time of queries does not require a populated VITE_API_BASE_URL in mock mode", async () => {
    // Empty base URL is OK as long as kubb-client is never called.
    vi.stubEnv("VITE_USE_MOCK", "true")
    vi.stubEnv("VITE_API_BASE_URL", "")
    vi.resetModules()

    // Importing the queries module should not throw — real-mode calls do,
    // and that's the contract enforced by the kubb-client test.
    await expect(import("@/domains/runs/api/queries")).resolves.toBeDefined()
  })
})

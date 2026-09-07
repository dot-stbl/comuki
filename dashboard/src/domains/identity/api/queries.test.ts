import { afterEach, describe, expect, it, vi } from "vitest"

/**
 * Mock-first contract for the identity domain's kubb-wire queries.
 *
 * Two things must hold, both mirrored from the runs pattern:
 *
 * 1. The real-mode kubb clients are never imported when
 *    `VITE_USE_MOCK=true` (otherwise their import time would read
 *    `import.meta.env` and bootstrap a real fetcher even in mock mode).
 * 2. The seed store keeps mapping to the same shape the screens render, so
 *    callers can branch on `env.useMock` without branching on the result
 *    type.
 *
 * The mocks live under `vi.mock(...)` so the test does not have to spin up a
 * QueryClient — we are asserting the dispatch, not the React surface.
 */

afterEach(() => {
  vi.resetModules()
  vi.unstubAllEnvs()
})

vi.mock("@/shared/api/_generated/clients/getApiV1AuthMe", () => ({
  getApiV1AuthMe: vi.fn(),
}))
vi.mock("@/shared/api/_generated/clients/getApiV1AuthOidcProviderStart", () => ({
  getApiV1AuthOidcProviderStart: vi.fn(),
}))
// vi.mock factories type their exports as the original kubb function so we
// cast through `vi.fn` to access mock controls — see "kubb-client mock"
// pattern in other domains. The mocks return the wire shapes by contract.
function mockFn<T>(): ReturnType<typeof vi.fn> & { mockResolvedValue(v: T): void } {
  return vi.fn() as ReturnType<typeof vi.fn> & { mockResolvedValue(v: T): void }
}
vi.mock("@/shared/api/_generated/clients/getApiV1Users", () => ({
  getApiV1Users: mockFn(),
}))
vi.mock("@/shared/api/_generated/clients/getApiV1Grants", () => ({
  getApiV1Grants: mockFn(),
}))
vi.mock("@/shared/api/_generated/clients/getApiV1Keys", () => ({
  getApiV1Keys: mockFn(),
}))
vi.mock("@/shared/api/_generated/clients/getApiV1Projects", () => ({
  getApiV1Projects: mockFn(),
}))

describe("queries.ts mock-first path", () => {
  it("hands the seeded duty engineer to useCurrentUserQuery in mock mode", async () => {
    vi.stubEnv("VITE_USE_MOCK", "true")
    vi.stubEnv("VITE_API_BASE_URL", "")
    vi.resetModules()

    const { useCurrentUserQuery } = await import(
      "@/domains/identity/api/queries"
    )
    const { SESSION_USER_SEED } = await import("@/shared/api/mock/session.seed")
    const { QueryClient, QueryClientProvider } = await import(
      "@tanstack/react-query"
    )
    const { renderHook, waitFor } = await import("@testing-library/react")
    const React = await import("react")

    const client = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    })

    const wrapper = ({ children }: { children: React.ReactNode }) =>
      React.createElement(QueryClientProvider, { client }, children)

    const { result } = renderHook(() => useCurrentUserQuery(), { wrapper })

    await waitFor(() => expect(result.current.isSuccess).toBe(true))

    expect(result.current.data).toEqual(SESSION_USER_SEED)
  })

  it("returns a mock URL for startOidc in mock mode and never calls kubb", async () => {
    vi.stubEnv("VITE_USE_MOCK", "true")
    vi.stubEnv("VITE_API_BASE_URL", "")
    vi.resetModules()

    const oidcClient = await import(
      "@/shared/api/_generated/clients/getApiV1AuthOidcProviderStart"
    )
    const { useStartOidcQuery } = await import(
      "@/domains/identity/api/queries"
    )
    const { QueryClient, QueryClientProvider } = await import(
      "@tanstack/react-query"
    )
    const { renderHook, waitFor } = await import("@testing-library/react")
    const React = await import("react")

    const client = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    })

    const wrapper = ({ children }: { children: React.ReactNode }) =>
      React.createElement(QueryClientProvider, { client }, children)

    const { result } = renderHook(() => useStartOidcQuery("comuki"), {
      wrapper,
    })

    await waitFor(() => expect(result.current.isSuccess).toBe(true))

    expect(result.current.data).toBe("mock://oidc/comuki/start")
    expect(oidcClient.getApiV1AuthOidcProviderStart).not.toHaveBeenCalled()
  })

  it("keeps reading the identity admin snapshot in mock mode", async () => {
    vi.stubEnv("VITE_USE_MOCK", "true")
    vi.stubEnv("VITE_API_BASE_URL", "")
    vi.resetModules()

    const queries = await import("@/domains/identity/api/queries")

    // The public surface — every screen that asks the identity admin
    // question calls one of these. The mock-mode path must keep working
    // for storybook / dev:mock without depending on the kubb transport.
    expect(typeof queries.useIdentityQuery).toBe("function")
    expect(typeof queries.useInviteUserMutation).toBe("function")
    expect(typeof queries.useLinkOidcMutation).toBe("function")
    expect(typeof queries.useSetUserDisabledMutation).toBe("function")
    expect(typeof queries.useGrantRoleMutation).toBe("function")
    expect(typeof queries.useRevokeRoleMutation).toBe("function")
    expect(typeof queries.useRevokeApiKeyMutation).toBe("function")
    expect(typeof queries.useCreateApiKeyMutation).toBe("function")
  })

  it("imports without throwing even when VITE_API_BASE_URL is unset, in mock mode", async () => {
    // Empty base URL is fine as long as kubb-client is never called. The
    // kubb-client throws a helpful message in real mode (covered by its own
    // test); here we only assert the import-time contract.
    vi.stubEnv("VITE_USE_MOCK", "true")
    vi.stubEnv("VITE_API_BASE_URL", "")
    vi.resetModules()

    await expect(
      import("@/domains/identity/api/queries"),
    ).resolves.toBeDefined()
  })
})

describe("queries.ts real-mode F13 read path (#45)", () => {
  it("loads the snapshot from the three list kubb clients in real mode", async () => {
    vi.stubEnv("VITE_USE_MOCK", "false")
    vi.stubEnv("VITE_API_BASE_URL", "http://localhost")
    vi.resetModules()

    const usersMod = await import(
      "@/shared/api/_generated/clients/getApiV1Users"
    )
    const grantsMod = await import(
      "@/shared/api/_generated/clients/getApiV1Grants"
    )
    const keysMod = await import("@/shared/api/_generated/clients/getApiV1Keys")
    const projectsMod = await import(
      "@/shared/api/_generated/clients/getApiV1Projects"
    )

    ;(usersMod.getApiV1Users as unknown as ReturnType<typeof vi.fn>).mockResolvedValue({
      items: [
        {
          id: { value: "u_alice" },
          email: "alice@example.com",
          displayName: "Alice",
          disabled: false,
          tokensVersion: 1,
          createdAt: "2026-01-01T00:00:00+00:00",
        },
      ],
      total: 1,
    })
    ;(grantsMod.getApiV1Grants as unknown as ReturnType<typeof vi.fn>).mockResolvedValue({
      items: [
        {
          id: { value: "g_alice_platform" },
          role: "platform-admin",
          scopeLevel: "platform",
          scopeProjectId: null,
          subjectType: "user",
          subjectId: "u_alice",
          grantedBy: null,
          createdAt: "2026-01-02T00:00:00+00:00",
          revokedAt: null,
          isActive: true,
        },
      ],
      total: 1,
    })
    ;(keysMod.getApiV1Keys as unknown as ReturnType<typeof vi.fn>).mockResolvedValue({ items: [], total: 0 })
    ;(projectsMod.getApiV1Projects as unknown as ReturnType<typeof vi.fn>).mockResolvedValue([])

    const { useIdentityQuery } = await import(
      "@/domains/identity/api/queries"
    )
    const { QueryClient, QueryClientProvider } = await import(
      "@tanstack/react-query"
    )
    const { renderHook, waitFor } = await import("@testing-library/react")
    const React = await import("react")

    const client = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    })
    const wrapper = ({ children }: { children: React.ReactNode }) =>
      React.createElement(QueryClientProvider, { client }, children)

    const { result } = renderHook(() => useIdentityQuery(), { wrapper })

    await waitFor(() => expect(result.current.isSuccess).toBe(true))

    const snapshot = result.current.data
    expect(snapshot?.users).toHaveLength(1)
    expect(snapshot?.users[0]?.email).toBe("alice@example.com")
    expect(snapshot?.users[0]?.oidcSubject).toBeNull()
    expect(snapshot?.grants).toHaveLength(1)
    expect(snapshot?.grants[0]?.subjectLabel).toBe("alice@example.com")
    expect(snapshot?.keys).toEqual([])
    expect(usersMod.getApiV1Users).toHaveBeenCalledTimes(1)
    expect(grantsMod.getApiV1Grants).toHaveBeenCalledTimes(1)
    expect(keysMod.getApiV1Keys).toHaveBeenCalledTimes(1)
    expect(projectsMod.getApiV1Projects).toHaveBeenCalledTimes(1)
  })

  it("logs a real-mode caller that previously threw: no exception on load", async () => {
    // The pre-#45 contract: a real-mode caller of useIdentityQuery
    // surfaced as an empty-state branch because loadIdentity threw.
    // After the F13 wiring this is the regression that keeps the screen
    // honest — the query resolves a snapshot (possibly empty), it never
    // rejects on the env.useMock=false branch alone.
    vi.stubEnv("VITE_USE_MOCK", "false")
    vi.stubEnv("VITE_API_BASE_URL", "http://localhost")
    vi.resetModules()

    const usersMod = await import(
      "@/shared/api/_generated/clients/getApiV1Users"
    )
    const grantsMod = await import(
      "@/shared/api/_generated/clients/getApiV1Grants"
    )
    const keysMod = await import("@/shared/api/_generated/clients/getApiV1Keys")
    const projectsMod = await import(
      "@/shared/api/_generated/clients/getApiV1Projects"
    )

    // vi.mock keeps the kubb client signature on the export; cast through
  //   vi.fn so test code can reach .mockResolvedValue / .toHaveBeenCalled*.
  ;(usersMod.getApiV1Users as unknown as ReturnType<typeof vi.fn>).mockResolvedValue({ items: [], total: 0 })
  ;(grantsMod.getApiV1Grants as unknown as ReturnType<typeof vi.fn>).mockResolvedValue({ items: [], total: 0 })
  ;(keysMod.getApiV1Keys as unknown as ReturnType<typeof vi.fn>).mockResolvedValue({ items: [], total: 0 })
  ;(projectsMod.getApiV1Projects as unknown as ReturnType<typeof vi.fn>).mockResolvedValue([])

    const { useIdentityQuery } = await import(
      "@/domains/identity/api/queries"
    )
    const { QueryClient, QueryClientProvider } = await import(
      "@tanstack/react-query"
    )
    const { renderHook, waitFor } = await import("@testing-library/react")
    const React = await import("react")

    const client = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    })
    const wrapper = ({ children }: { children: React.ReactNode }) =>
      React.createElement(QueryClientProvider, { client }, children)

    const { result } = renderHook(() => useIdentityQuery(), { wrapper })

    // Empty-state resolves with no error rather than re-throwing.
    await waitFor(() => expect(result.current.isLoading).toBe(false))
    expect(result.current.isError).toBe(false)
  })
})
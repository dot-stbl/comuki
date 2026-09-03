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
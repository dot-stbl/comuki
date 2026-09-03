import { afterEach, describe, expect, it, vi } from "vitest"

/**
 * Mock-first contract for the identity domain's auth mutations.
 *
 * The login / logout mutations are the bridge between the auth store and
 * the host. In mock mode they must keep touching the hand-written seed
 * store; in real mode they route through the kubb-generated clients. Two
 * things must hold:
 *
 * 1. Successful login invalidates the `me` and `projects` queries so the
 *    rail and the cached project-permissions view refetch against the new
 *    session.
 * 2. Successful logout does the same — the rail must read signed-out and
 *    any cached `me` value must be discarded.
 *
 * The test asserts both branches through `useMutation`'s onSuccess path by
 * rendering inside a QueryClient and watching `invalidateQueries` calls.
 */

afterEach(() => {
  vi.resetModules()
  vi.unstubAllEnvs()
})

vi.mock("@/shared/api/_generated/clients/postApiV1AuthLogin", () => ({
  postApiV1AuthLogin: vi.fn(),
}))
vi.mock("@/shared/api/_generated/clients/postApiV1AuthLogout", () => ({
  postApiV1AuthLogout: vi.fn(),
}))
vi.mock("@/shared/api/_generated/clients/getApiV1AuthMe", () => ({
  getApiV1AuthMe: vi.fn(),
}))

describe("mutations.ts mock-first path", () => {
  it("signs in through the mock store in mock mode and invalidates me + projects", async () => {
    vi.stubEnv("VITE_USE_MOCK", "true")
    vi.stubEnv("VITE_API_BASE_URL", "")
    vi.resetModules()

    const loginClient = await import(
      "@/shared/api/_generated/clients/postApiV1AuthLogin"
    )
    const logoutClient = await import(
      "@/shared/api/_generated/clients/postApiV1AuthLogout"
    )
    const meClient = await import(
      "@/shared/api/_generated/clients/getApiV1AuthMe"
    )
    const { useLoginMutation } = await import(
      "@/domains/identity/api/mutations"
    )
    const { QueryClient, QueryClientProvider } = await import(
      "@tanstack/react-query"
    )
    const { renderHook, act, waitFor } = await import(
      "@testing-library/react"
    )
    const React = await import("react")

    const client = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    })
    const invalidateSpy = vi.spyOn(client, "invalidateQueries")

    const wrapper = ({ children }: { children: React.ReactNode }) =>
      React.createElement(QueryClientProvider, { client }, children)

    const { result } = renderHook(() => useLoginMutation(), { wrapper })

    await act(async () => {
      await result.current.mutateAsync({
        email: "duty@comuki.local",
        password: "anything",
      })
    })

    await waitFor(() => expect(result.current.isSuccess).toBe(true))

    // Mock-mode path: never reaches the kubb clients.
    expect(loginClient.postApiV1AuthLogin).not.toHaveBeenCalled()
    expect(logoutClient.postApiV1AuthLogout).not.toHaveBeenCalled()
    expect(meClient.getApiV1AuthMe).not.toHaveBeenCalled()

    // The `me` and `projects` caches were invalidated on success.
    const invalidatedKeys = invalidateSpy.mock.calls.flatMap(
      ([arg]) => (arg as { queryKey: readonly unknown[] }).queryKey,
    )
    expect(invalidatedKeys).toContain("me")
    expect(invalidatedKeys).toContain("projects")
  })

  it("signs out through the mock store in mock mode and invalidates me + projects", async () => {
    vi.stubEnv("VITE_USE_MOCK", "true")
    vi.stubEnv("VITE_API_BASE_URL", "")
    vi.resetModules()

    const logoutClient = await import(
      "@/shared/api/_generated/clients/postApiV1AuthLogout"
    )
    const { useLogoutMutation } = await import(
      "@/domains/identity/api/mutations"
    )
    const { QueryClient, QueryClientProvider } = await import(
      "@tanstack/react-query"
    )
    const { renderHook, act, waitFor } = await import(
      "@testing-library/react"
    )
    const React = await import("react")

    const client = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    })
    const invalidateSpy = vi.spyOn(client, "invalidateQueries")

    const wrapper = ({ children }: { children: React.ReactNode }) =>
      React.createElement(QueryClientProvider, { client }, children)

    const { result } = renderHook(() => useLogoutMutation(), { wrapper })

    await act(async () => {
      await result.current.mutateAsync()
    })

    await waitFor(() => expect(result.current.isSuccess).toBe(true))

    expect(logoutClient.postApiV1AuthLogout).not.toHaveBeenCalled()

    const invalidatedKeys = invalidateSpy.mock.calls.flatMap(
      ([arg]) => (arg as { queryKey: readonly unknown[] }).queryKey,
    )
    expect(invalidatedKeys).toContain("me")
    expect(invalidatedKeys).toContain("projects")
  })

  it("imports without throwing even when VITE_API_BASE_URL is unset, in mock mode", async () => {
    vi.stubEnv("VITE_USE_MOCK", "true")
    vi.stubEnv("VITE_API_BASE_URL", "")
    vi.resetModules()

    await expect(
      import("@/domains/identity/api/mutations"),
    ).resolves.toBeDefined()
  })
})
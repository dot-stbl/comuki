import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { renderHook, waitFor } from "@testing-library/react"
import { describe, expect, it, vi } from "vitest"
import type { ReactElement, ReactNode } from "react"

import { SESSION_USER_SEED } from "@/shared/api/mock/session.seed"

/**
 * The two surfaces of `useAuthState`, pinned to `env.useMock` via the
 * mocked `@/shared/config/env` module.
 *
 * Each `describe` re-mocks `env` to the value its tests need, so the
 * SUT reads the right `useMock` flag without re-importing.
 */

const { useCurrentUserQuery } = vi.hoisted(() => ({
  useCurrentUserQuery: vi.fn(),
}))

vi.mock("@/domains/identity/api/queries", () => ({
  useCurrentUserQuery: useCurrentUserQuery,
  meQueryKey: ["me"],
}))

/** Empty-but-defined result — `auth.ts` only reads `.data`. */
function emptyMeQuery(): { data: undefined; isPending: boolean; isError: boolean; isSuccess: boolean } {
  return {
    data: undefined,
    isPending: false,
    isError: false,
    isSuccess: false,
  }
}

function withQuery({ children }: { children: ReactNode }): ReactElement {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false, gcTime: 0 } },
  })
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>
}

describe("useAuthState — mock mode", () => {
  it("returns the seeded signed-in shift from the mock store", async () => {
    vi.doMock("@/shared/config/env", () => ({
      env: { useMock: true, apiBaseUrl: "", oidcProvider: null },
    }))
    vi.resetModules()
    useCurrentUserQuery.mockReturnValue({
      ...emptyMeQuery(),
      data: SESSION_USER_SEED,
    })

    const { useAuthState } = await import("./auth")
    const { result } = renderHook(() => useAuthState(), { wrapper: withQuery })

    await waitFor(() => {
      expect(result.current.user).not.toBeNull()
    })
    expect(result.current.user).toEqual(SESSION_USER_SEED)
    // The mock store ships with a default OIDC provider so the screen
    // exercises the "Continue with OIDC" button out of the box.
    expect(result.current.oidc?.id).toBe("comuki-oidc")
  })

  it("returns the seeded signed-in user even when VITE_OIDC_PROVIDER is set", async () => {
    // Mock store is authoritative — the env var does not override it.
    vi.doMock("@/shared/config/env", () => ({
      env: { useMock: true, apiBaseUrl: "", oidcProvider: "comuki" },
    }))
    vi.resetModules()
    useCurrentUserQuery.mockReturnValue({
      ...emptyMeQuery(),
      data: SESSION_USER_SEED,
    })

    const { useAuthState } = await import("./auth")
    const { result } = renderHook(() => useAuthState(), { wrapper: withQuery })

    await waitFor(() => {
      expect(result.current.user).not.toBeNull()
    })
    expect(result.current.oidc?.id).toBe("comuki-oidc")
  })
})

describe("useAuthState — real mode", () => {
  it("subscribes to useCurrentUserQuery and returns its data", async () => {
    vi.doMock("@/shared/config/env", () => ({
      env: { useMock: false, apiBaseUrl: "http://localhost:17173", oidcProvider: "comuki" },
    }))
    vi.resetModules()
    useCurrentUserQuery.mockReturnValue({
      ...emptyMeQuery(),
      data: SESSION_USER_SEED,
      isSuccess: true,
    })

    const { useAuthState } = await import("./auth")
    const { result } = renderHook(() => useAuthState(), { wrapper: withQuery })

    await waitFor(() => {
      expect(result.current.user).toEqual(SESSION_USER_SEED)
    })
    expect(result.current.oidc).toEqual({ id: "comuki", label: "comuki" })
    // Cookie sessions carry no record of why they ended — the only consumer
    // that needed it (`guardSession`) reads the mock store directly.
    expect(result.current.endedBy).toBeNull()
  })

  it("returns a null user while the query is loading", async () => {
    vi.doMock("@/shared/config/env", () => ({
      env: { useMock: false, apiBaseUrl: "http://localhost:17173", oidcProvider: null },
    }))
    vi.resetModules()
    useCurrentUserQuery.mockReturnValue({
      ...emptyMeQuery(),
      isPending: true,
    })

    const { useAuthState } = await import("./auth")
    const { result } = renderHook(() => useAuthState(), { wrapper: withQuery })

    expect(result.current.user).toBeNull()
    // No provider configured → the OIDC button must not render.
    expect(result.current.oidc).toBeNull()
  })

  it("disables the me query in mock mode so kubb-client is never imported", async () => {
    vi.doMock("@/shared/config/env", () => ({
      env: { useMock: true, apiBaseUrl: "", oidcProvider: null },
    }))
    vi.resetModules()

    const enabledMock = vi.fn()
    useCurrentUserQuery.mockImplementation((options) => {
      enabledMock(options?.enabled ?? true)
      return {
        ...emptyMeQuery(),
        data: SESSION_USER_SEED,
      }
    })

    const { useAuthState } = await import("./auth")
    renderHook(() => useAuthState(), { wrapper: withQuery })

    expect(enabledMock).toHaveBeenCalledWith(false)
  })
})
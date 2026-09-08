import { describe, expect, it, vi } from "vitest"

/* Issue Q3 / v1.1: real-mode wiring must not throw. The mock store is
   irrelevant — the kubb hook and the projects hook are the only
   real-mode callers, and they are both mocked here. */
vi.mock("@/shared/config/env", () => ({
  env: {
    useMock: false,
    apiBaseUrl: "http://localhost",
    proxyKey: "test-key",
    oidcProvider: null,
    repoUrl: null,
    commitSha: "",
    deployEnv: "local",
  },
}))

vi.mock("@/shared/api/_generated/clients/getApiV1ProjectsProjectidCosts", () => ({
  getApiV1ProjectsProjectidCosts: vi.fn().mockResolvedValue({}),
}))
vi.mock("@/shared/api/_generated/clients/getApiV1Projects", () => ({
  getApiV1Projects: vi.fn().mockResolvedValue([
    {
      id: { value: "p_alpha" },
      slug: "alpha",
      name: "Alpha",
      gitProfileRepo: null,
      createdAt: "2026-01-01T00:00:00+00:00",
    },
  ]),
}))

import { renderHook, waitFor } from "@testing-library/react"
import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import * as React from "react"

import { useCostQuery } from "@/domains/cost/api/queries"

describe("cost query, real mode", () => {
  it("Given a project registry, when useCostQuery runs, then it resolves without throwing", async () => {
    const client = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    })
    const wrapper = ({ children }: { children: React.ReactNode }) =>
      React.createElement(QueryClientProvider, { client }, children)

    const { result } = renderHook(() => useCostQuery(), { wrapper })

    await waitFor(() => expect(result.current.isLoading).toBe(false))

    // Real-mode success: no throw. The data shape is the same as the
    // mock branch until a platform-wide cost endpoint lands.
    expect(result.current.isError).toBe(false)
    expect(result.current.data).toBeDefined()
  })
})

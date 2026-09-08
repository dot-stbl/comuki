import { describe, expect, it, vi } from "vitest"

/* Issue Q7 / v1.1: real-mode wiring must not throw. The proxy fetch
   is the only real-mode caller; it is mocked here. */
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

vi.mock("@/shared/api/models-proxy", () => ({
  fetchProxyModelsAsync: vi.fn().mockResolvedValue({
    object: "list",
    data: [
      { id: "gpt-4", object: "model", created: 0, owned_by: "comuki-proxy" },
      { id: "claude-3", object: "model", created: 0, owned_by: "comuki-proxy" },
    ],
  }),
}))

import { renderHook, waitFor } from "@testing-library/react"
import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import * as React from "react"

import { useModelsQuery } from "@/domains/models/api/queries"

describe("models query, real mode", () => {
  it("Given a proxy response, when useModelsQuery runs, then it resolves with the proxy models and no throw", async () => {
    const client = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    })
    const wrapper = ({ children }: { children: React.ReactNode }) =>
      React.createElement(QueryClientProvider, { client }, children)

    const { result } = renderHook(() => useModelsQuery(), { wrapper })

    await waitFor(() => expect(result.current.isLoading).toBe(false))

    expect(result.current.isError).toBe(false)
    const data = result.current.data
    expect(data).toBeDefined()
    expect(data?.endpoints).toHaveLength(1)
    expect(data?.endpoints[0]?.models).toEqual(["gpt-4", "claude-3"])
  })
})

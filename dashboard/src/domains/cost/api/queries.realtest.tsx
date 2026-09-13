import { describe, expect, it, vi } from "vitest"

/* The platform rollup is the only real-mode caller now; the kubb client is
   mocked here and the mapper is asserted in mappers.test.ts. */
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

vi.mock("@/shared/api/_generated/clients/getApiV1Costs", () => ({
  getApiV1Costs: vi.fn().mockResolvedValue({
    since: "2026-08-14T00:00:00Z",
    windowDays: 30,
    windowUsdMicros: 12_340_000,
    allTimeUsdMicros: 190_500_000,
    byProject: [
      {
        projectId: "b3d8a402-1111-2222-3333-444444444444",
        costUsdMicros: 9_100_000,
        runs: 34,
      },
    ],
    byDay: [{ date: "2026-09-12", costUsdMicros: 820_000 }],
  }),
}))

import { renderHook, waitFor } from "@testing-library/react"
import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import * as React from "react"

import { useCostQuery } from "@/domains/cost/api/queries"

describe("cost query, real mode", () => {
  it("Given the platform rollup, when useCostQuery runs, then it resolves with real dollars", async () => {
    const client = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    })
    const wrapper = ({ children }: { children: React.ReactNode }) =>
      React.createElement(QueryClientProvider, { client }, children)

    const { result } = renderHook(() => useCostQuery(), { wrapper })

    await waitFor(() => expect(result.current.isLoading).toBe(false))

    expect(result.current.isError).toBe(false)
    // Micros became dollars exactly once: the window tile reads $12.34.
    expect(result.current.data?.windowUsd).toBeCloseTo(12.34, 2)
    expect(result.current.data?.allTimeUsd).toBeCloseTo(190.5, 2)
    // The seed's demo badge is gone with the seed itself.
    expect(result.current.data?.perSuccess).toBeNull()
  })
})

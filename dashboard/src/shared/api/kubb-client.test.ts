import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

/**
 * The kubb-client transport is the only place the FE looks at
 * `VITE_API_BASE_URL` and the only place a missing-variable turns into a
 * readable error. These tests pin that contract — every generated hook
 * in `_generated/clients/*` calls this transport, so the contract is the
 * whole surface the dashboard relies on.
 *
 * `import.meta.env` is read once at module load. To test the unset and
 * set cases we reset the module via `vi.resetModules()` between cases.
 */

const ORIGINAL_ENV = { ...import.meta.env }

beforeEach(() => {
  vi.resetModules()
})

afterEach(() => {
  for (const key of Object.keys(import.meta.env)) {
    if (!(key in ORIGINAL_ENV)) {
      delete (import.meta.env as Record<string, string | undefined>)[key]
    }
  }
})

async function loadClient(
  baseUrl: string | undefined,
): Promise<typeof import("@/shared/api/kubb-client")> {
  if (baseUrl === undefined) {
    delete (import.meta.env as Record<string, string | undefined>)
      .VITE_API_BASE_URL
  } else {
    ;(import.meta.env as Record<string, string>).VITE_API_BASE_URL = baseUrl
  }
  return import("@/shared/api/kubb-client")
}

describe("kubb-client transport (issue #29)", () => {
  it("throws a helpful error when VITE_API_BASE_URL is unset", async () => {
    const { default: client } = await loadClient(undefined)

    await expect(
      client({ method: "GET", url: "/api/v1/runs" }),
    ).rejects.toThrow(/VITE_API_BASE_URL is not set/)
    await expect(
      client({ method: "GET", url: "/api/v1/runs" }),
    ).rejects.toThrow(/mock layer/)
  })

  it("strips the trailing slash from VITE_API_BASE_URL so /api/v1 doesn't double-emit", async () => {
    const { default: client } = await loadClient("http://localhost:17173/")

    const fetchSpy = vi.fn().mockResolvedValue({
      status: 200,
      statusText: "OK",
      body: null,
      headers: new Headers(),
      json: async () => ({}),
    })
    vi.stubGlobal("fetch", fetchSpy)

    await client({ method: "GET", url: "/api/v1/runs" })

    const called = fetchSpy.mock.calls[0]?.[0] as string
    expect(called).toBe("http://localhost:17173/api/v1/runs")
    expect(called).not.toContain("//api")
  })

  it("rejects 401 / 403 with a tagged boundary error so queries fall into their error branch", async () => {
    const { default: client } = await loadClient("http://localhost:17173")

    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue({
        status: 401,
        statusText: "Unauthorized",
        body: null,
        headers: new Headers(),
        json: async () => ({ status: 401 }),
      }),
    )

    await expect(
      client({ method: "GET", url: "/api/v1/runs" }),
    ).rejects.toMatchObject({
      status: 401,
      message: expect.stringContaining("auth boundary 401"),
    })
  })

  it("forwards credentials: 'include' so the cookie session survives the cross-origin hop", async () => {
    const { default: client } = await loadClient("http://localhost:17173")

    const fetchSpy = vi
      .fn()
      .mockResolvedValue({
        status: 200,
        body: null,
        headers: new Headers(),
        json: async () => ({}),
      })
    vi.stubGlobal("fetch", fetchSpy)

    await client({ method: "GET", url: "/api/v1/runs" })

    const init = fetchSpy.mock.calls[0]?.[1] as RequestInit
    expect(init.credentials).toBe("include")
  })
})

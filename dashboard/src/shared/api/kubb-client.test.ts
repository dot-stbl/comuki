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

/** A Response-shaped stub — the transport reads `ok`, `text()` and status. */
function fakeResponse(
  status: number,
  body: string,
  statusText = "",
): {
  status: number
  ok: boolean
  statusText: string
  headers: Headers
  text: () => Promise<string>
} {
  return {
    status,
    ok: status >= 200 && status < 300,
    statusText,
    headers: new Headers(),
    text: async () => body,
  }
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

    const fetchSpy = vi.fn().mockResolvedValue(fakeResponse(200, "{}", "OK"))
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
      vi.fn().mockResolvedValue(
        fakeResponse(
          401,
          JSON.stringify({
            status: 401,
            code: "auth.invalid_credentials",
            detail: "email or password is incorrect",
          }),
          "Unauthorized",
        ),
      ),
    )

    await expect(
      client({ method: "GET", url: "/api/v1/runs" }),
    ).rejects.toMatchObject({
      status: 401,
      message: expect.stringContaining("auth boundary 401"),
      // The ProblemDetails body rides along so screens can map `code` /
      // `detail` to a human sentence instead of the raw message.
      data: {
        code: "auth.invalid_credentials",
        detail: "email or password is incorrect",
      },
    })
  })

  it("forwards credentials: 'include' so the cookie session survives the cross-origin hop", async () => {
    const { default: client } = await loadClient("http://localhost:17173")

    const fetchSpy = vi
      .fn()
      .mockResolvedValue(fakeResponse(200, "{}"))
    vi.stubGlobal("fetch", fetchSpy)

    await client({ method: "GET", url: "/api/v1/runs" })

    const init = fetchSpy.mock.calls[0]?.[1] as RequestInit
    expect(init.credentials).toBe("include")
  })

  // 400/429/5xx used to return the problem body as if it were a success
  // payload — worse than no data at all. They reject now, same shape as 401.
  it("rejects every non-2xx with status and the problem body", async () => {
    const { default: client } = await loadClient("http://localhost:17173")

    const problem = JSON.stringify({
      status: 400,
      title: "One or more validation errors occurred.",
      errors: { email: ["not an email address"] },
    })
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(fakeResponse(400, problem)),
    )

    await expect(
      client({ method: "POST", url: "/api/v1/auth/login", data: {} }),
    ).rejects.toMatchObject({
      status: 400,
      message: "request failed 400",
      data: { errors: { email: ["not an email address"] } },
    })
  })

  // The login rate limiter answers 429 with an empty body — json() would
  // throw on it and lose the status. The transport reads text and survives.
  it("survives an empty 429 body and still surfaces the status", async () => {
    const { default: client } = await loadClient("http://localhost:17173")

    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(fakeResponse(429, "", "Too Many Requests")),
    )

    await expect(
      client({ method: "POST", url: "/api/v1/auth/login", data: {} }),
    ).rejects.toMatchObject({
      status: 429,
      message: "request failed 429",
      data: {},
    })
  })
})

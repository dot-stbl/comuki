import { afterEach, describe, expect, it, setSystemTime } from "bun:test"
import {
  ComukiApiError,
  ComukiClient,
  isAbortError,
  nextLatencyEma,
  type ChatMessagesPageView,
  type ChatTurnResultView,
} from "./client"
import { resolveConfig } from "./config"

/** Scripted fetch: matches method+path, returns the queued response. */
function fakeFetch(
  routes: Record<
    string,
    { status?: number; body?: unknown; setCookie?: string[] }
  >
) {
  const calls: {
    method: string
    url: string
    headers: Record<string, string>
    body?: unknown
  }[] = []
  const impl = (async (input: string, init?: RequestInit) => {
    const url = new URL(input)
    const key = `${init?.method ?? "GET"} ${url.pathname}`
    const route = routes[key]
    calls.push({
      method: init?.method ?? "GET",
      url: input,
      headers: (init?.headers ?? {}) as Record<string, string>,
      body: init?.body ? JSON.parse(String(init.body)) : undefined,
    })
    if (!route) {
      return new Response(JSON.stringify({ title: "no route" }), {
        status: 404,
      })
    }
    const headers = new Headers({ "Content-Type": "application/json" })
    for (const cookie of route.setCookie ?? []) {
      headers.append("set-cookie", cookie)
    }
    return new Response(
      route.body === undefined ? "" : JSON.stringify(route.body),
      {
        status: route.status ?? 200,
        headers,
      }
    )
  }) as typeof fetch
  return { impl, calls }
}

const session = {
  id: "018f1a2b-3c4d-7e5f-8a9b-0c1d2e3f4a5b",
  projectId: null,
  title: "t",
  status: "active",
  createdAt: "2026-09-17T00:00:00Z",
  updatedAt: "2026-09-17T00:00:00Z",
}

describe("ComukiClient", () => {
  it("sends the api key as bearer and the tenant header", async () => {
    const { impl, calls } = fakeFetch({
      "GET /api/v1/auth/me": { body: { subjectId: "s" } },
    })
    const client = new ComukiClient(
      resolveConfig(
        { COMUKI_URL: "http://t" },
        { apiKey: "ck_test", tenant: "acme" }
      ),
      { fetchImpl: impl }
    )
    await client.me()
    expect(calls[0]?.headers.Authorization).toBe("Bearer ck_test")
    expect(calls[0]?.headers["X-Comuki-Tenant"]).toBe("acme")
  })

  it("sends the stored cookie when no api key is set", async () => {
    const { impl, calls } = fakeFetch({
      "GET /api/v1/auth/me": { body: { subjectId: "s" } },
    })
    const client = new ComukiClient(
      resolveConfig(
        { COMUKI_URL: "http://t" },
        { cookie: ".Comuki.Session=xyz" }
      ),
      { fetchImpl: impl }
    )
    await client.me()
    expect(calls[0]?.headers.Cookie).toBe(".Comuki.Session=xyz")
  })

  it("creates a session with project and title", async () => {
    const { impl, calls } = fakeFetch({
      "POST /api/v1/chat/sessions": { status: 201, body: session },
    })
    const client = new ComukiClient(
      resolveConfig({ COMUKI_URL: "http://t" }),
      { fetchImpl: impl }
    )
    await client.createSession({ projectId: "p1", title: "t" })
    expect(calls[0]?.body).toEqual({ projectId: "p1", title: "t" })
  })

  it("posts a message and returns the turn result", async () => {
    const turn: ChatTurnResultView = {
      messages: [],
      awaitingApproval: false,
      pendingPlan: null,
    }
    const { impl, calls } = fakeFetch({
      "POST /api/v1/chat/sessions/abc/messages": { body: turn },
    })
    const client = new ComukiClient(
      resolveConfig({ COMUKI_URL: "http://t" }),
      { fetchImpl: impl }
    )
    await client.postMessage("abc", "hello")
    expect(calls[0]?.body).toEqual({ message: "hello" })
  })

  it("reads the transcript page", async () => {
    const page: ChatMessagesPageView = {
      items: [],
      page: 1,
      pageSize: 50,
      total: 0,
    }
    const { impl } = fakeFetch({
      "GET /api/v1/chat/sessions/abc/messages": { body: page },
    })
    const client = new ComukiClient(
      resolveConfig({ COMUKI_URL: "http://t" }),
      { fetchImpl: impl }
    )
    expect(await client.listMessages("abc")).toEqual(page)
  })

  it("approves with reason in the body", async () => {
    const { impl, calls } = fakeFetch({
      "POST /api/v1/chat/sessions/abc/approve": {
        body: { messages: [], awaitingApproval: false, pendingPlan: null },
      },
    })
    const client = new ComukiClient(
      resolveConfig({ COMUKI_URL: "http://t" }),
      { fetchImpl: impl }
    )
    await client.approve("abc", false, "too broad")
    expect(calls[0]?.body).toEqual({ approved: false, reason: "too broad" })
  })

  it("turns problem+json into ComukiApiError with code and detail", async () => {
    const { impl } = fakeFetch({
      "GET /api/v1/runs": {
        status: 403,
        body: { code: "permission.denied", detail: "run:read required" },
      },
    })
    const client = new ComukiClient(
      resolveConfig({ COMUKI_URL: "http://t" }),
      { fetchImpl: impl }
    )
    try {
      await client.runs()
      throw new Error("expected runs() to throw")
    } catch (error) {
      expect(error).toBeInstanceOf(ComukiApiError)
      const apiError = error as ComukiApiError
      expect(apiError.status).toBe(403)
      expect(apiError.code).toBe("permission.denied")
      expect(apiError.detail).toBe("run:read required")
    }
  })

  it("captures the session cookie from login, skipping antiforgery", async () => {
    const { impl } = fakeFetch({
      "POST /api/v1/auth/login": {
        body: { userId: "u1", email: "a@b.c", displayName: "A" },
        setCookie: [
          ".Comuki.Session=abc123; Path=/; HttpOnly",
          "__Host-antiforgery=x; Path=/",
        ],
      },
    })
    const client = new ComukiClient(
      resolveConfig({ COMUKI_URL: "http://t" }),
      { fetchImpl: impl }
    )
    const success = await client.login("a@b.c", "pw")
    expect(success.cookie).toBe(".Comuki.Session=abc123")
    expect(success.displayName).toBe("A")
  })

  it("runs list carries page, pageSize and filter as query", async () => {
    const { impl, calls } = fakeFetch({
      "GET /api/v1/runs": {
        body: { items: [], page: 2, pageSize: 5, total: 0 },
      },
    })
    const client = new ComukiClient(
      resolveConfig({ COMUKI_URL: "http://t" }),
      { fetchImpl: impl }
    )
    await client.runs(2, 5, "status==queued")
    expect(calls[0]?.url).toContain("page=2")
    expect(calls[0]?.url).toContain("pageSize=5")
    expect(calls[0]?.url).toContain("filter=status%3D%3Dqueued")
  })

  it("exposes hub url and hub headers for the signalr connection", () => {
    const client = new ComukiClient(
      resolveConfig(
        { COMUKI_URL: "http://h:1/" },
        { apiKey: "ck_k", tenant: "t" }
      )
    )
    expect(client.hubUrl()).toBe("http://h:1/ws/runs")
    expect(client.hubHeaders()).toEqual({
      Authorization: "Bearer ck_k",
      "X-Comuki-Tenant": "t",
    })
  })

  it("passes a per-turn abort signal to the fetch and rejects as AbortError", async () => {
    const controller = new AbortController()
    // A hanging turn: resolves only through the abort signal — exactly
    // what /stop does to a slow brain.
    const impl = (async (_input: string, init?: RequestInit) => {
      const signal = init?.signal
      if (!signal) {
        throw new Error("expected a signal on the turn request")
      }
      return await new Promise<Response>((_, reject) => {
        signal.addEventListener("abort", () => {
          reject(new DOMException("aborted", "AbortError"))
        })
      })
    }) as typeof fetch
    const client = new ComukiClient(
      resolveConfig({ COMUKI_URL: "http://t" }),
      { fetchImpl: impl }
    )

    const pending = client.postMessage("abc", "slow question", controller.signal)
    controller.abort()
    const error = await pending.then(
      () => undefined,
      (failure: unknown) => failure
    )

    expect(isAbortError(error)).toBe(true)
    expect(client.chatLatencyMs()).toBeNull()
  })

  it("isAbortError recognises only the abort rejection", () => {
    expect(isAbortError(new DOMException("x", "AbortError"))).toBe(true)
    expect(isAbortError(new Error("AbortError"))).toBe(false)
    expect(isAbortError(new ComukiApiError(500, undefined, "boom"))).toBe(false)
    expect(isAbortError("nope")).toBe(false)
  })
})

describe("nextLatencyEma", () => {
  it("seeds with the first sample and clamps negatives to zero", () => {
    expect(nextLatencyEma(null, 120)).toBe(120)
    expect(nextLatencyEma(null, -5)).toBe(0)
  })

  it("smooths with alpha 0.3", () => {
    expect(nextLatencyEma(100, 200)).toBe(130)
    expect(nextLatencyEma(130, 100)).toBe(121)
  })
})

describe("postMessage latency sampling", () => {
  afterEach(() => {
    setSystemTime(new Date())
  })

  /**
   * Deterministic clock: `setSystemTime` freezes `Date.now`, and the
   * fetch mock advances it mid-flight — exactly between postMessage's
   * two `Date.now()` reads — so the measured round-trip is exact.
   */
  function clockFetch(impl: typeof fetch, advanceMs: () => number) {
    return (async (input: string, init?: RequestInit) => {
      setSystemTime(new Date(Date.now() + advanceMs()))
      return impl(input, init)
    }) as typeof fetch
  }

  it("feeds the EMA of successful chat POST round-trips", async () => {
    setSystemTime(new Date("2026-09-18T10:00:00Z"))
    const turn: ChatTurnResultView = {
      messages: [],
      awaitingApproval: false,
      pendingPlan: null,
    }
    const { impl } = fakeFetch({
      "POST /api/v1/chat/sessions/abc/messages": { body: turn },
    })
    let advance = 42
    const samples: number[] = []
    const client = new ComukiClient(resolveConfig({ COMUKI_URL: "http://t" }), {
      fetchImpl: clockFetch(impl, () => advance),
      onLatencySample: (latencyMs) => samples.push(latencyMs),
    })

    await client.postMessage("abc", "one")
    advance = 140
    await client.postMessage("abc", "two")

    // 42 seeds; 140 blends: 42 + 0.3 × (140 − 42) = 71.4 → 71.
    expect(client.chatLatencyMs()).toBe(71)
    expect(samples).toEqual([42, 71])
  })

  it("does not sample a failed send", async () => {
    setSystemTime(new Date("2026-09-18T10:00:00Z"))
    const { impl } = fakeFetch({
      "POST /api/v1/chat/sessions/abc/messages": {
        status: 503,
        body: { detail: "down" },
      },
    })
    const samples: number[] = []
    const client = new ComukiClient(resolveConfig({ COMUKI_URL: "http://t" }), {
      fetchImpl: impl,
      onLatencySample: (latencyMs) => samples.push(latencyMs),
    })

    await client.postMessage("abc", "hello").catch(() => {
      // The rejection is the point; latency must stay untouched.
    })

    expect(samples).toEqual([])
    expect(client.chatLatencyMs()).toBeNull()
  })
})

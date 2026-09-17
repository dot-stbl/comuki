import { describe, expect, it } from "bun:test"
import {
  ComukiApiError,
  ComukiClient,
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
})

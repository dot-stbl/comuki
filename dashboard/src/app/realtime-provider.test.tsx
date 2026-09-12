import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { cleanup, render, waitFor } from "@testing-library/react"
import type { ReactNode } from "react"
import { afterEach, describe, expect, it, vi } from "vitest"

/**
 * The realtime provider's lifecycle contract:
 *
 * - real mode + signed out → no connection is ever built;
 * - real mode + session resolved (`me` succeeded, `user` non-null) → the
 *   connection is built, started, and its events invalidate the query cache;
 * - sign-out → the connection is stopped;
 * - mock mode → nothing connects at all (the `demo` reading).
 *
 * `@microsoft/signalr` is mocked at the builder seam, so these tests assert
 * what the provider does with a connection, not what the library does over
 * the wire — the event→invalidation mapping itself is pinned in
 * `runs-hub.test.ts` against the real bind function.
 */

interface FakeConnection {
  on: ReturnType<typeof vi.fn>
  onreconnecting: ReturnType<typeof vi.fn>
  onreconnected: ReturnType<typeof vi.fn>
  onclose: ReturnType<typeof vi.fn>
  start: ReturnType<typeof vi.fn>
  stop: ReturnType<typeof vi.fn>
  invoke: ReturnType<typeof vi.fn>
  state: string
}

const harness = vi.hoisted(() => {
  return {
    auth: {
      user: null as { id: string } | null,
      endedBy: null,
      oidc: null,
    },
    built: [] as FakeConnection[],
  }
})

vi.mock("@microsoft/signalr", () => ({
  HubConnectionState: { Disconnected: "Disconnected", Connected: "Connected" },
  HttpTransportType: {
    WebSockets: 1,
    ServerSentEvents: 2,
    LongPolling: 4,
  },
  HubConnectionBuilder: class {
    withUrl() {
      return this
    }
    withAutomaticReconnect() {
      return this
    }
    build() {
      const connection = {
        on: vi.fn(),
        onreconnecting: vi.fn(),
        onreconnected: vi.fn(),
        onclose: vi.fn(),
        start: vi.fn(() => Promise.resolve()),
        stop: vi.fn(() => Promise.resolve()),
        invoke: vi.fn(() => Promise.resolve()),
        state: "Disconnected",
      }
      harness.built.push(connection)
      return connection
    }
  },
}))

vi.mock("@/domains/auth", () => ({
  useAuthState: () => harness.auth,
}))

vi.mock("@/domains/projects/api/queries", () => ({
  useProjectsQuery: () => ({ data: undefined }),
}))

afterEach(() => {
  cleanup()
  vi.resetModules()
  vi.unstubAllEnvs()
  harness.built.length = 0
  harness.auth = { user: null, endedBy: null, oidc: null }
})

interface Loaded {
  provider: typeof import("@/app/realtime-provider")
  runsHub: typeof import("@/shared/realtime/runs-hub")
}

/** Fresh module registry for the env being stubbed, with a clean singleton. */
async function loadRealtime(): Promise<Loaded> {
  vi.resetModules()
  const provider = await import("@/app/realtime-provider")
  const runsHub = await import("@/shared/realtime/runs-hub")
  runsHub.setRunsHubConnection(null)
  runsHub.resetRunsHubStatus()
  return { provider, runsHub }
}

function realtimeTree(
  provider: typeof import("@/app/realtime-provider"),
  client: QueryClient,
): ReactNode {
  return (
    <QueryClientProvider client={client}>
      <provider.RealtimeProvider>
        <span>screen</span>
      </provider.RealtimeProvider>
    </QueryClientProvider>
  )
}

describe("RealtimeProvider", () => {
  it("never builds a connection while signed out, builds and starts one when the session resolves, and stops it on sign-out", async () => {
    vi.stubEnv("VITE_USE_MOCK", "false")
    vi.stubEnv("VITE_API_BASE_URL", "http://localhost:17180")
    const { provider, runsHub } = await loadRealtime()

    const client = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    })
    const view = render(realtimeTree(provider, client))

    // Signed out (`me` unresolved or refused): no socket, ever.
    expect(harness.built).toHaveLength(0)

    // The me query resolves — the session is real, the socket opens.
    harness.auth = { user: { id: "u-duty" }, endedBy: null, oidc: null }
    view.rerender(realtimeTree(provider, client))

    await waitFor(() => expect(harness.built).toHaveLength(1))
    const connection = harness.built[0]
    await waitFor(() => expect(connection.start).toHaveBeenCalledTimes(1))
    await waitFor(() =>
      expect(runsHub.runsHubStatusStore.getSnapshot().status).toBe("live"),
    )

    // A server event lands on the bound callback and invalidates the cache.
    client.setQueryData(["runs"], [])
    expect(client.getQueryState(["runs"])?.isInvalidated).toBe(false)
    const runEventHandler = connection.on.mock.calls.find(
      ([method]) => method === "RunEvent",
    )?.[1] as (event: unknown) => void
    expect(runEventHandler).toBeDefined()
    runEventHandler({
      runId: "11111111-1111-4111-8111-111111111111",
      type: "work_item.status_changed",
      workItemId: null,
      occurredAtUnixMs: 1,
      payloadJson: null,
      payloadOmitted: false,
    })
    await waitFor(() =>
      expect(client.getQueryState(["runs"])?.isInvalidated).toBe(true),
    )

    // The session dies (cache cleared, user null): the socket goes with it.
    harness.auth = { user: null, endedBy: null, oidc: null }
    view.rerender(realtimeTree(provider, client))
    await waitFor(() => expect(connection.stop).toHaveBeenCalled())
    expect(runsHub.getRunsHubConnection()).toBeNull()
  })

  it("builds nothing in mock mode even when a user is signed in", async () => {
    vi.stubEnv("VITE_USE_MOCK", "true")
    const { provider, runsHub } = await loadRealtime()
    harness.auth = { user: { id: "u-duty" }, endedBy: null, oidc: null }

    const client = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    })
    render(realtimeTree(provider, client))

    expect(harness.built).toHaveLength(0)
    expect(runsHub.runsHubStatusStore.getSnapshot().status).toBe("demo")
  })

  it("stays silent (no connection, polling status) when real mode has no API base URL", async () => {
    vi.stubEnv("VITE_USE_MOCK", "false")
    vi.stubEnv("VITE_API_BASE_URL", "")
    const { provider, runsHub } = await loadRealtime()
    harness.auth = { user: { id: "u-duty" }, endedBy: null, oidc: null }

    const client = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    })
    render(realtimeTree(provider, client))

    expect(harness.built).toHaveLength(0)
    expect(runsHub.runsHubStatusStore.getSnapshot().status).toBe("polling")
  })
})

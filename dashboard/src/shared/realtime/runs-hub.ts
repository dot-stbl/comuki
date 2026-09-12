import { useSyncExternalStore } from "react"

import {
  HubConnectionBuilder,
  HttpTransportType,
  type HubConnection,
} from "@microsoft/signalr"

import { env } from "@/shared/config/env"

/**
 * The dashboard's client half of the runs hub.
 *
 * The server half lives in `platform/src/host/Comuki.Host/Realtime/`:
 * `RunsHub` is mapped at `/ws/runs` (`RealtimeExtensions.MapComukiRealtime`)
 * and speaks two client callbacks — `RunEvent` (one journal append, to the
 * `run:{id}` group) and `Attention` (an attention-worthy transition, to the
 * `project:{id}:attention` group). Both broadcasts are **group-addressed**,
 * so a connection that joins nothing hears nothing: the provider joins the
 * attention groups of the projects the session can see, and the run detail
 * screen joins the one run it is showing.
 *
 * Mock mode never connects (`env.useMock === true` → `null` connection, the
 * badge says `demo`, and the polling layer is the only refresh there is).
 * A real-mode connection that fails stays silent on purpose — the polling
 * fallback still refreshes the same queries, so a dead socket degrades to
 * slower data rather than an error state.
 */

/**
 * Client callback names, mirrored from
 * `Comuki.Shared.Contracts/Realtime/RealtimeTransportMethods.cs`. Renaming
 * either side is a wire break — the values must stay byte-identical.
 */
export const RealtimeTransportMethods = {
  RunEvent: "RunEvent",
  Attention: "Attention",
} as const

/**
 * One journal append broadcast to the `run:{id}` group after every
 * `run_events` write. Mirrors `RunEventView` (the C# record is the source of
 * truth; the hub's JSON protocol serialises it camelCase, which is also what
 * the C#→TS contract emitter produces).
 */
export interface RunEventView {
  readonly runId: string
  readonly type: string
  readonly workItemId: string | null
  readonly occurredAtUnixMs: number
  readonly payloadJson: string | null
  readonly payloadOmitted: boolean
}

/**
 * An attention-worthy transition (running / failed / escalated /
 * awaiting_approval) broadcast to the owning project's attention group.
 * Mirrors `AttentionView`.
 */
export interface AttentionView {
  readonly runId: string
  readonly projectId: string
  readonly workItemId: string | null
  readonly status: string
  readonly attentionKind: string
  readonly occurredAtUnixMs: number
}

/**
 * The hub's address under the same base URL the kubb transport uses
 * (`VITE_API_BASE_URL`). Trailing slashes are stripped the same way
 * `kubb-client.ts` strips them, so `http://host/` and `http://host` both
 * produce `http://host/ws/runs`.
 */
export function runsHubUrl(baseUrl: string): string {
  return baseUrl.replace(/\/+$/, "") + "/ws/runs"
}

/**
 * What the connection indicator in the topbar should say.
 *
 * - `demo` — mock mode; there is no socket and the data is seed data.
 * - `polling` — real mode with no live socket (never started, connecting,
 *   reconnecting, or closed). The polling layer owns refresh in this state.
 * - `live` — the hub connection is up and events are arriving.
 */
export type RunsHubStatus = "demo" | "polling" | "live"

type StatusListener = () => void

const statusListeners = new Set<StatusListener>()
let status: RunsHubStatus = env.useMock ? "demo" : "polling"
let statusVersion = 0
let statusSnapshot: { status: RunsHubStatus; version: number } = {
  status,
  version: statusVersion,
}

function publishStatus(next: RunsHubStatus): void {
  status = next
  statusVersion += 1
  statusSnapshot = { status, version: statusVersion }
  for (const listener of statusListeners) {
    listener()
  }
}

/** The `useSyncExternalStore` triple for the badge — one snapshot object per change. */
export const runsHubStatusStore = {
  subscribe(listener: StatusListener): () => void {
    statusListeners.add(listener)
    return () => {
      statusListeners.delete(listener)
    }
  },
  getSnapshot(): { status: RunsHubStatus; version: number } {
    return statusSnapshot
  },
}

/** Test seam: restore the pristine status between module-scoped cases. */
export function resetRunsHubStatus(): void {
  publishStatus(env.useMock ? "demo" : "polling")
}

/** Writes the status from the connection's own lifecycle events. */
export function updateRunsHubStatus(next: RunsHubStatus): void {
  publishStatus(next)
}

/** The status as render state — the badge and the provider read this. */
export function useRunsHubStatus(): RunsHubStatus {
  return useSyncExternalStore(
    runsHubStatusStore.subscribe,
    runsHubStatusStore.getSnapshot,
    runsHubStatusStore.getSnapshot,
  ).status
}

/**
 * Builds the singleton connection, or `null` when connecting is not this
 * build's job: mock mode (`env.useMock`) and real mode without a pointed
 * backend (`VITE_API_BASE_URL` empty — the same contract `kubb-client.ts`
 * enforces for REST) both stay offline, and the polling layer carries the
 * refresh alone.
 *
 * Cookie auth rides for free: the connection is same-origin with the API
 * base URL the kubb transport uses, and the SignalR browser client sends
 * cookies on negotiate and WebSocket requests by default.
 */
export function createRunsHubConnection(): HubConnection | null {
  if (env.useMock || env.apiBaseUrl.length === 0) {
    return null
  }

  return new HubConnectionBuilder()
    .withUrl(runsHubUrl(env.apiBaseUrl), {
      // The cookie must cross the SPA→host origin split (Vite :17173 → host
      // :NNNN in dev), so the negotiate request is not same-origin in
      // practice and needs the credential opt-in — the same reason
      // kubb-client forces `credentials: "include"`.
      withCredentials: true,
      // Long-polling only as the fallback it is: WebSockets first, then
      // server-sent events, then polling — the default ladder, spelled out
      // because a proxy that eats upgrades should degrade, not fail.
      transport:
        HttpTransportType.WebSockets |
        HttpTransportType.ServerSentEvents |
        HttpTransportType.LongPolling,
    })
    .withAutomaticReconnect()
    .build()
}

let connectionSingleton: HubConnection | null = null

/** The process-wide connection; `null` until the provider starts it. */
export function getRunsHubConnection(): HubConnection | null {
  return connectionSingleton
}

/** Test seam — the provider also uses it to drop a stopped connection. */
export function setRunsHubConnection(next: HubConnection | null): void {
  connectionSingleton = next
}

/**
 * The run-list root key, mirrored from `domains/runs/api/queries.ts`
 * (`runsQueryKey`). Duplicated here so the shared realtime layer does not
 * import from a domain; the test pins the two literals together so drift
 * fails the build, not the operator's screen.
 */
const RUNS_ROOT_KEY = ["runs"] as const

/**
 * A `RunEvent` touches the run it names: the duty list (its status column)
 * and the run's own detail query (timeline, plan, journal). Both are prefix
 * matches under the runs root, but the detail key is spelled out so the
 * mapping reads as intent rather than as a prefix accident.
 */
export function invalidationsForRunEvent(
  event: RunEventView,
): readonly (readonly unknown[])[] {
  return [RUNS_ROOT_KEY, ["runs", event.runId]]
}

/**
 * An `Attention` signal is a run transition first (same invalidations as
 * `RunEvent`) and a project heartbeat second — the registry's "in flight"
 * count is stale the moment a run fails or escalates on that project.
 * No worker/pool event exists on the hub today; the queue key stays out of
 * the mapping until one does.
 */
export function invalidationsForAttention(
  event: AttentionView,
): readonly (readonly unknown[])[] {
  return [RUNS_ROOT_KEY, ["runs", event.runId], ["projects"]]
}

/**
 * Registers the two server→client callbacks on a connection. Pure wiring —
 * the mapping functions above decide what an event invalidates, so this can
 * bind to any connection-shaped object in a test and the provider can pass
 * its real `queryClient.invalidateQueries` in production.
 */
export function bindRunsHubEvents(
  connection: Pick<HubConnection, "on">,
  invalidate: (queryKey: readonly unknown[]) => void,
): void {
  connection.on(RealtimeTransportMethods.RunEvent, (event: RunEventView) => {
    for (const queryKey of invalidationsForRunEvent(event)) {
      invalidate(queryKey)
    }
  })
  connection.on(
    RealtimeTransportMethods.Attention,
    (event: AttentionView) => {
      for (const queryKey of invalidationsForAttention(event)) {
        invalidate(queryKey)
      }
    },
  )
}

/**
 * Join/leave wrappers. The hub's joins are permission-gated (`run:read` /
 * `project:read` on the project) and reject with a `HubException` when the
 * session cannot see the target — that is the server saying "not yours",
 * which is a normal answer for a role-scoped dashboard, so the rejection is
 * swallowed here. A group never joined simply delivers nothing, which is
 * the honest degradation.
 */
export async function joinProjectAttentionGroup(
  connection: HubConnection,
  projectId: string,
): Promise<void> {
  try {
    await connection.invoke("JoinProjectAsync", projectId)
  } catch {
    // Permission denied or the project is gone — either way, no group.
  }
}

export async function leaveProjectAttentionGroup(
  connection: HubConnection,
  projectId: string,
): Promise<void> {
  try {
    await connection.invoke("LeaveProjectAsync", projectId)
  } catch {
    // Leaving is always allowed server-side; a rejection here means the
    // connection is already gone, which is what we wanted anyway.
  }
}

export async function joinRunGroup(
  connection: HubConnection,
  runId: string,
): Promise<void> {
  try {
    await connection.invoke("JoinRunAsync", runId)
  } catch {
    // Unknown run or no run:read — the detail screen's REST query is the
    // truth anyway.
  }
}

export async function leaveRunGroup(
  connection: HubConnection,
  runId: string,
): Promise<void> {
  try {
    await connection.invoke("LeaveRunAsync", runId)
  } catch {
    // Same as leaving a project group.
  }
}

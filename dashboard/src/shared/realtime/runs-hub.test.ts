import { describe, expect, it, vi } from "vitest"

import { runQueryKey, runsQueryKey } from "@/domains/runs/api/queries"
import { projectsQueryKey } from "@/domains/projects/api/queries"

import {
  RealtimeTransportMethods,
  bindRunsHubEvents,
  invalidationsForAttention,
  invalidationsForRunEvent,
  resetRunsHubStatus,
  runsHubStatusStore,
  runsHubUrl,
  updateRunsHubStatus,
  type AttentionView,
  type RunEventView,
} from "@/shared/realtime/runs-hub"

/**
 * The contract with the runs hub, pinned. The server half lives in
 * `platform/src/host/Comuki.Host/Realtime/` — `RunsHub` at `/hubs/runs`
 * sends `RunEvent` (RunEventView) and `Attention` (AttentionView) — and
 * these tests hold the dashboard half to the same names and shapes:
 *
 * - the callback names are byte-identical to
 *   `RealtimeTransportMethods.cs` (a rename there is a wire break the
 *   dashboard must follow);
 * - every event invalidates exactly the queries it makes stale, **spelled
 *   with the domain's own key factories** — the mapping here asserts
 *   against `runsQueryKey` / `runQueryKey` / `projectsQueryKey`, so a key
 *   that drifts in a domain fails this test rather than silently missing;
 * - the status store behind the topbar badge notifies its listeners.
 */

const RUN_ID = "11111111-1111-4111-8111-111111111111"
const PROJECT_ID = "22222222-2222-4222-8222-222222222222"

const runEvent: RunEventView = {
  runId: RUN_ID,
  type: "work_item.status_changed",
  workItemId: null,
  occurredAtUnixMs: 1_760_000_000_000,
  payloadJson: '{"to":"Failed"}',
  payloadOmitted: false,
}

const attention: AttentionView = {
  runId: RUN_ID,
  projectId: PROJECT_ID,
  workItemId: null,
  status: "Failed",
  attentionKind: "failed",
  occurredAtUnixMs: 1_760_000_000_000,
}

describe("runsHubUrl", () => {
  it("appends the hub path under the API base URL and strips trailing slashes", () => {
    expect(runsHubUrl("http://localhost:17180")).toBe(
      "http://localhost:17180/hubs/runs",
    )
    expect(runsHubUrl("http://localhost:17180///")).toBe(
      "http://localhost:17180/hubs/runs",
    )
  })
})

describe("event → invalidation mapping", () => {
  it("a RunEvent invalidates the duty list and the run's own detail key — the domain's own literals", () => {
    // The drift guard: the mapping is spelled with a local literal, so this
    // assertion is what welds it to the domain key factories.
    expect(invalidationsForRunEvent(runEvent)).toEqual([
      runsQueryKey,
      runQueryKey(RUN_ID),
    ])
  })

  it("an Attention signal also invalidates the project registry — its run counts went stale", () => {
    expect(invalidationsForAttention(attention)).toEqual([
      runsQueryKey,
      runQueryKey(RUN_ID),
      projectsQueryKey,
    ])
  })

  it("the callback names match the server constants byte-for-byte", () => {
    expect(RealtimeTransportMethods).toEqual({
      RunEvent: "RunEvent",
      Attention: "Attention",
    })
  })
})

describe("bindRunsHubEvents", () => {
  it("registers both callbacks under their wire names and routes each to the mapped keys", () => {
    const handlers = new Map<string, (...args: unknown[]) => void>()
    const connection = {
      on: vi.fn((method: string, handler: (...args: unknown[]) => void) => {
        handlers.set(method, handler)
      }),
    }
    const invalidate = vi.fn()

    bindRunsHubEvents(connection, invalidate)

    expect(connection.on).toHaveBeenCalledWith(
      "RunEvent",
      expect.any(Function),
    )
    expect(connection.on).toHaveBeenCalledWith(
      "Attention",
      expect.any(Function),
    )

    handlers.get("RunEvent")!(runEvent)
    expect(invalidate).toHaveBeenCalledWith(runsQueryKey)
    expect(invalidate).toHaveBeenCalledWith(runQueryKey(RUN_ID))
    expect(invalidate).toHaveBeenCalledTimes(2)

    invalidate.mockClear()
    handlers.get("Attention")!(attention)
    expect(invalidate).toHaveBeenCalledWith(projectsQueryKey)
    expect(invalidate).toHaveBeenCalledTimes(3)
  })
})

describe("runsHubStatusStore", () => {
  it("notifies listeners on a status change and bumps the snapshot version", () => {
    const listener = vi.fn()
    const before = runsHubStatusStore.getSnapshot()

    const unsubscribe = runsHubStatusStore.subscribe(listener)
    updateRunsHubStatus("live")

    const after = runsHubStatusStore.getSnapshot()
    expect(after.status).toBe("live")
    expect(after.version).toBe(before.version + 1)
    expect(listener).toHaveBeenCalledTimes(1)

    unsubscribe()
    resetRunsHubStatus()
    expect(listener).toHaveBeenCalledTimes(1)
  })
})

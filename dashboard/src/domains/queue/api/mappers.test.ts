import { describe, expect, it } from "vitest"

import {
  workerViewToWorker,
  workersPageToWorkers,
  type WorkerViewWire,
} from "@/domains/queue/api/mappers"

/* The wire shapes are typed locally (the OpenAPI spec declares no response
   schema for the workers reads), so the mapper is the one place the claim
   "this is what the host sends" is checked. Fixtures copied from the host's
   `WorkersReadModels.cs` docblocks, camelCased as the serializer writes
   them. */

/** Now, pinned: the mapper derives ages from instants, and a pinned clock
 *  keeps the arithmetic assertable. */
const NOW = Date.parse("2026-09-13T12:00:00Z")

function view(over: Partial<WorkerViewWire> = {}): WorkerViewWire {
  return {
    workerId: "wk_2f8a",
    state: "busy",
    projectId: "b3d8a402-1111-2222-3333-444444444444",
    profileKey: "implementer",
    image: "sha256:9c41ab",
    currentWorkItemId: "wi_0101",
    currentRunId: "8f3c2a91-1111-2222-3333-444444444444",
    leaseUntil: new Date(NOW + 214_000).toISOString(),
    heartbeatAt: new Date(NOW - 3_000).toISOString(),
    attempt: 1,
    lastSeenAt: new Date(NOW - 3_000).toISOString(),
    ...over,
  }
}

describe("the wire worker view onto the screen's worker", () => {
  it("carries what a live lease names: clocks, image, profile, item and run", () => {
    const worker = workerViewToWorker(view(), NOW)

    expect(worker.id).toBe("wk_2f8a")
    expect(worker.state).toBe("busy")
    expect(worker.itemId).toBe("wi_0101")
    expect(worker.digest).toBe("sha256:9c41ab")
    expect(worker.profile).toBe("implementer")
    expect(worker.leaseSec).toBe(214)
    expect(worker.heartbeatAgeSec).toBe(3)
  })

  it("degrades the container facts a derived registry cannot know", () => {
    const worker = workerViewToWorker(view(), NOW)

    // No compute engine is composed, so no provider, no handle, no uptime —
    // the columns draw a dash rather than a guess.
    expect(worker.provider).toBeNull()
    expect(worker.handle).toBeNull()
    expect(worker.upSec).toBeNull()
  })

  it("reads an idle row as holding nothing, with no lease to defend", () => {
    const worker = workerViewToWorker(
      view({
        state: "idle",
        projectId: null,
        profileKey: null,
        image: null,
        currentWorkItemId: null,
        currentRunId: null,
        leaseUntil: null,
        heartbeatAt: null,
        attempt: 0,
      }),
      NOW
    )

    expect(worker.state).toBe("idle")
    expect(worker.projectId).toBeNull()
    expect(worker.profile).toBeNull()
    expect(worker.itemId).toBeNull()
    expect(worker.digest).toBeNull()
    expect(worker.leaseSec).toBeNull()
    expect(worker.heartbeatAgeSec).toBeNull()
  })

  it("keeps an unknown state word honest rather than inventing one", () => {
    // The host adds a state the dashboard has not met: the mapper parks it on
    // `idle` (the least alarming reading) rather than throwing a screen over
    // a vocabulary change. `offline` arrives as itself.
    expect(workerViewToWorker(view({ state: "offline" }), NOW).state).toBe(
      "offline"
    )
    expect(workerViewToWorker(view({ state: "warp-9" }), NOW).state).toBe(
      "idle"
    )
  })

  it("maps a page of rows onto the pool", () => {
    const pool = workersPageToWorkers(
      {
        items: [view(), view({ workerId: "wk_a07e", state: "idle" })],
        page: 1,
        pageSize: 25,
        total: 2,
      },
      NOW
    )

    expect(pool).toHaveLength(2)
    expect(pool.map((worker) => worker.id)).toEqual(["wk_2f8a", "wk_a07e"])
  })
})

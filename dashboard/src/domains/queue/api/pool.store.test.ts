import { afterEach, describe, expect, it } from "vitest"

import {
  drainWorker,
  forceStopWorker,
  listQueueItems,
  listWorkerPools,
  listWorkers,
  resetPool,
} from "./pool.store"

afterEach(resetPool)

describe("the pool as a human leaves it", () => {
  it("serves the seeded shift until somebody touches it", () => {
    const workers = listWorkers()
    expect(workers.length).toBeGreaterThan(0)
    expect(listWorkerPools().length).toBe(3)
    expect(listQueueItems().length).toBeGreaterThan(0)
  })

  it("turns a busy worker to draining and leaves its item alone", () => {
    const busy = listWorkers().find((worker) => worker.state === "busy")!

    drainWorker(busy.id)

    const after = listWorkers().find((worker) => worker.id === busy.id)
    expect(after?.state).toBe("draining")
    // Drain is lossless: the item in hand is still running, still claimed.
    const item = listQueueItems().find((entry) => entry.id === busy.itemId)
    expect(item?.status).toBe("running")
    expect(item?.claimedBy).toBe(busy.id)
  })

  it("simply removes a drained idle worker, which has nothing to finish", () => {
    const idle = listWorkers().find((worker) => worker.state === "idle")!

    drainWorker(idle.id)

    expect(listWorkers().some((worker) => worker.id === idle.id)).toBe(false)
  })

  it("takes the container away on a force stop and requeues what it held", () => {
    const busy = listWorkers().find((worker) => worker.state === "busy")!

    forceStopWorker(busy.id)

    expect(listWorkers().some((worker) => worker.id === busy.id)).toBe(false)

    // The consequence the dialog promised, and the reason the two halves of
    // this screen sit on one page: the work comes back to the queue rather
    // than dying with the container.
    const item = listQueueItems().find((entry) => entry.id === busy.itemId)
    expect(item?.status).toBe("queued")
    expect(item?.claimedBy).toBeNull()
    expect(item?.ageSec).toBe(0)
  })

  it("puts the shift back, so one test cannot bleed into the next", () => {
    const before = listWorkers().length
    forceStopWorker(listWorkers()[0].id)
    expect(listWorkers().length).toBe(before - 1)

    resetPool()
    expect(listWorkers().length).toBe(before)
  })
})

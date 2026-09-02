import { describe, expect, it } from "vitest"

import {
  AGE_STALLED_SEC,
  AGE_WARM_SEC,
  ageHeat,
  ageShare,
  backlogOf,
  compareQueueItems,
  depthReadings,
  leaseHeat,
  lostLeases,
  minIdleFor,
  queueOrder,
  resolveWorkerEmpty,
  unclaimedOver,
  workerCounts,
} from "./queue"
import type { QueueDepthDay, QueueItem, Worker, WorkerPool } from "./types"

function item(
  id: string,
  status: QueueItem["status"],
  ageSec: number,
  extra: Partial<QueueItem> = {}
): QueueItem {
  return {
    id,
    runId: "8f3c2a91",
    projectId: "p_test",
    profile: "implementer",
    label: "do the thing",
    status,
    ageSec,
    claimedBy: status === "running" ? "wk_0001" : null,
    blockedOn: [],
    ...extra,
  }
}

function worker(id: string, extra: Partial<Worker> = {}): Worker {
  return {
    id,
    projectId: "p_test",
    profile: "implementer",
    state: "idle",
    itemId: null,
    provider: "docker",
    handle: `docker/test/${id}`,
    heartbeatAgeSec: 2,
    leaseSec: null,
    upSec: 300,
    digest: "sha256:000000",
    ...extra,
  }
}

describe("age, and where it is allowed to accuse anyone", () => {
  it("reads a queued item's wait in three steps", () => {
    expect(ageHeat(item("a", "queued", 8))).toBe("fresh")
    expect(ageHeat(item("a", "queued", AGE_WARM_SEC))).toBe("warm")
    expect(ageHeat(item("a", "queued", AGE_STALLED_SEC))).toBe("stalled")
    expect(ageHeat(item("a", "queued", 2612))).toBe("stalled")
  })

  it("leaves every other status cold, however old it is", () => {
    // A blocked item waits on its own run, not on the pool: three hours is
    // normal and marking it would train the operator to ignore the marking.
    expect(ageHeat(item("a", "blocked", 13260))).toBe("none")
    expect(ageHeat(item("a", "running", 4000))).toBe("none")
    expect(ageHeat(item("a", "succeeded", 9999))).toBe("none")
    expect(ageHeat(item("a", "failed", 9999))).toBe("none")
    expect(ageHeat(item("a", "cancelled", 9999))).toBe("none")
  })

  it("draws the bar as a share of the wait, and never past full", () => {
    expect(ageShare(item("a", "queued", 0))).toBe(0)
    expect(ageShare(item("a", "queued", AGE_STALLED_SEC / 2))).toBeCloseTo(0.5)
    expect(ageShare(item("a", "queued", AGE_STALLED_SEC * 9))).toBe(1)
    // Nothing to draw where there is nothing to say.
    expect(ageShare(item("a", "blocked", 13260))).toBe(0)
  })
})

describe("the order the list opens in", () => {
  const items = [
    item("succeeded-old", "succeeded", 9000),
    item("blocked-ancient", "blocked", 13260),
    item("queued-fresh", "queued", 8),
    item("running-long", "running", 1186),
    item("queued-stalled", "queued", 2612),
    item("failed-recent", "failed", 252),
    item("queued-warm", "queued", 664),
  ]

  it("puts unclaimed work first, oldest at the top", () => {
    const order = queueOrder(items).map((entry) => entry.id)

    expect(order.slice(0, 3)).toEqual([
      "queued-stalled",
      "queued-warm",
      "queued-fresh",
    ])
  })

  it("does not let an ancient blocked item outrank a fresh queued one", () => {
    // The trap this ordering exists to avoid: blocked items are the oldest
    // rows on the screen and the least interesting, so a plain age sort would
    // bury the reading under them.
    const order = queueOrder(items).map((entry) => entry.id)

    expect(order.indexOf("queued-fresh")).toBeLessThan(
      order.indexOf("blocked-ancient")
    )
  })

  it("ranks the rest by who is owed a decision, then by age", () => {
    const order = queueOrder(items).map((entry) => entry.id)

    expect(order).toEqual([
      "queued-stalled",
      "queued-warm",
      "queued-fresh",
      "failed-recent",
      "running-long",
      "blocked-ancient",
      "succeeded-old",
    ])
  })

  it("is stable: two items of the same status and age keep one order", () => {
    const a = item("wi_a", "queued", 40)
    const b = item("wi_b", "queued", 40)

    expect(compareQueueItems(a, b)).toBeLessThan(0)
    expect(compareQueueItems(b, a)).toBeGreaterThan(0)
  })

  it("does not mutate what it was given", () => {
    const original = [...items]
    queueOrder(items)
    expect(items).toEqual(original)
  })
})

describe("the counts the header quotes", () => {
  const items = [
    item("a", "queued", 8),
    item("b", "queued", 2612),
    item("c", "queued", 664),
    item("d", "running", 400),
    item("e", "blocked", 13260),
  ]

  it("counts what nobody has claimed", () => {
    expect(backlogOf(items)).toBe(3)
  })

  it("counts only the queued ones that have waited too long", () => {
    expect(unclaimedOver(items, AGE_STALLED_SEC)).toBe(2)
    // The blocked item is older than all of them and is not in the number.
    expect(unclaimedOver(items, 10000)).toBe(0)
  })
})

describe("the lease, and the worker that stopped defending it", () => {
  it("says nothing about a worker holding no lease", () => {
    expect(leaseHeat(worker("wk_a"))).toBe("none")
  })

  it("marks a lease that is nearly up", () => {
    expect(
      leaseHeat(worker("wk_a", { leaseSec: 6, heartbeatAgeSec: 3 }))
    ).toBe("expiring")
  })

  it("marks the worker that stopped heartbeating, however much lease is left", () => {
    // This is the pool's half of the same failure the queue's age column
    // shows, and it outranks the countdown: a long lease held by a container
    // nobody has heard from is worse than a short one being defended.
    expect(
      leaseHeat(worker("wk_a", { leaseSec: 220, heartbeatAgeSec: 74 }))
    ).toBe("lost")
    expect(
      lostLeases([
        worker("wk_a", { leaseSec: 220, heartbeatAgeSec: 74 }),
        worker("wk_b", { leaseSec: 220, heartbeatAgeSec: 2 }),
        worker("wk_c"),
      ])
    ).toBe(1)
  })

  it("counts the pool by state", () => {
    expect(
      workerCounts([
        worker("wk_a", { state: "busy" }),
        worker("wk_b", { state: "busy" }),
        worker("wk_c", { state: "draining" }),
        worker("wk_d"),
      ])
    ).toEqual({ total: 4, busy: 2, draining: 1, idle: 1 })
  })
})

describe("an empty pool, and which kind of empty it is", () => {
  it("tells a pool the scaler is about to fill from one that is at rest", () => {
    // The two states the requirements name, and the whole reason this resolves
    // to a kind rather than to a boolean: both are empty, both are correct,
    // and they are not the same sentence.
    const backlog = resolveWorkerEmpty({ poolSize: 0, minIdle: 0, backlog: 4 })
    const atRest = resolveWorkerEmpty({ poolSize: 0, minIdle: 0, backlog: 0 })

    expect(backlog).toBe("backlog")
    expect(atRest).toBe("at-rest")
    expect(backlog).not.toBe(atRest)
  })

  it("calls a pool under its own target what it is, backlog or not", () => {
    // The one case where empty really is wrong. It is answered before the
    // reassuring cases get a chance to swallow it.
    expect(resolveWorkerEmpty({ poolSize: 0, minIdle: 2, backlog: 0 })).toBe(
      "under-target"
    )
    expect(resolveWorkerEmpty({ poolSize: 0, minIdle: 2, backlog: 9 })).toBe(
      "under-target"
    )
  })

  it("blames the filters when the pool is plainly up", () => {
    expect(resolveWorkerEmpty({ poolSize: 6, minIdle: 0, backlog: 4 })).toBe(
      "filtered"
    )
  })

  it("reads the idle target off the project being asked about", () => {
    const pools: WorkerPool[] = [
      { projectId: "p_one", minIdle: 2, maxIdle: 24 },
      { projectId: "p_two", minIdle: 0, maxIdle: 8 },
    ]

    expect(minIdleFor(pools, "p_two")).toBe(0)
    expect(minIdleFor(pools, "p_one")).toBe(2)
    // No project filter: the whole board answers for every pool it can see.
    expect(minIdleFor(pools, "")).toBe(2)
    expect(minIdleFor(pools, "p_missing")).toBe(0)
  })
})

describe("depth over time, and whether today accuses anyone", () => {
  const week: QueueDepthDay[] = [
    { label: "mon", depth: 4 },
    { label: "tue", depth: 6 },
    { label: "wed", depth: 5 },
    { label: "thu", depth: 8 },
    { label: "fri", depth: 6 },
    { label: "sat", depth: 9 },
    { label: "today", depth: 14 },
  ]

  it("reads the week's range off the days before today", () => {
    const readings = depthReadings(week)

    expect(readings).not.toBeNull()
    expect(readings?.today).toBe(14)
    expect(readings?.weekMin).toBe(4)
    expect(readings?.weekMax).toBe(9)
    expect(readings?.todayIsDeepest).toBe(true)
  })

  it("says today is ordinary when it is", () => {
    const readings = depthReadings(
      week.map((day) =>
        day.label === "today" ? { ...day, depth: 5 } : day
      )
    )

    expect(readings?.todayIsDeepest).toBe(false)
  })

  it("has no reading at all for a series with no days", () => {
    expect(depthReadings([])).toBeNull()
  })
})

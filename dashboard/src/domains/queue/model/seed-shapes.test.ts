import { describe, expect, it } from "vitest"

import { PROFILE_CATALOG } from "@/shared/api/mock/runs.seed"
import {
  QUEUE_SEED,
  WORKERS_SEED,
  WORKER_POOLS_SEED,
} from "@/shared/api/mock/queue.seed"

import { backlogOf, unclaimedOver, workerCounts, AGE_STALLED_SEC } from "./queue"

/**
 * The mock's own contract.
 *
 * A seed that drifts teaches the screen a failure the product cannot produce —
 * a running item with no worker, a worker holding an item that finished — and
 * the screen then grows a branch for a state the backend will never send. So
 * the pairing is asserted here rather than trusted.
 */

describe("the seeded queue and pool describe one mechanism", () => {
  it("claims exactly the running items, and nothing else", () => {
    for (const item of QUEUE_SEED) {
      if (item.status === "running") {
        expect(item.claimedBy, `${item.id} is running and unclaimed`).not.toBeNull()
      } else {
        expect(
          item.claimedBy,
          `${item.id} is ${item.status} and still holds a lease`
        ).toBeNull()
      }
    }
  })

  it("points every claim at a worker that exists, and back again", () => {
    const workerById = new Map(WORKERS_SEED.map((worker) => [worker.id, worker]))

    for (const item of QUEUE_SEED) {
      if (!item.claimedBy) {
        continue
      }
      const worker = workerById.get(item.claimedBy)
      expect(worker, `${item.id} names a worker that is not in the pool`).toBeDefined()
      expect(worker?.itemId).toBe(item.id)
      // A worker only claims what its own profile can run — that is the whole
      // matching rule, and an item queued against a profile nobody runs is the
      // failure this screen exists to show.
      expect(worker?.profile).toBe(item.profile)
      expect(worker?.projectId).toBe(item.projectId)
    }
  })

  it("gives every busy or draining worker exactly one live item", () => {
    const itemById = new Map(QUEUE_SEED.map((item) => [item.id, item]))

    for (const worker of WORKERS_SEED) {
      if (worker.state === "idle") {
        expect(worker.itemId, `${worker.id} is idle and holds an item`).toBeNull()
        expect(worker.leaseSec).toBeNull()
        continue
      }
      expect(worker.itemId, `${worker.id} is ${worker.state} and holds nothing`).not.toBeNull()
      const item = itemById.get(worker.itemId as string)
      expect(item?.status).toBe("running")
      expect(item?.claimedBy).toBe(worker.id)
    }
  })

  it("only names profiles the client actually declared", () => {
    const catalog = new Set<string>(PROFILE_CATALOG)
    for (const item of QUEUE_SEED) {
      expect(catalog.has(item.profile), `${item.profile} is not in the catalog`).toBe(true)
    }
    for (const worker of WORKERS_SEED) {
      expect(catalog.has(worker.profile)).toBe(true)
    }
  })
})

describe("the seed carries the cases the screen was built for", () => {
  it("is big enough for virtualization and filtering to be real", () => {
    expect(QUEUE_SEED.length).toBeGreaterThanOrEqual(30)
    expect(QUEUE_SEED.length).toBeLessThanOrEqual(60)
    expect(WORKERS_SEED.length).toBeGreaterThanOrEqual(8)
    expect(WORKERS_SEED.length).toBeLessThanOrEqual(15)
  })

  it("holds an item that has waited far too long on a profile no worker runs", () => {
    const stalled = QUEUE_SEED.filter(
      (item) => item.status === "queued" && item.ageSec >= AGE_STALLED_SEC
    )
    expect(stalled.length).toBeGreaterThan(0)

    const worst = stalled.reduce((a, b) => (a.ageSec > b.ageSec ? a : b))
    const canClaim = WORKERS_SEED.some(
      (worker) =>
        worker.projectId === worst.projectId && worker.profile === worst.profile
    )
    expect(canClaim, "the worst wait would actually be claimable").toBe(false)
  })

  it("holds a worker draining, and one that stopped heartbeating on a live lease", () => {
    const counts = workerCounts(WORKERS_SEED.map((worker) => ({ ...worker })))
    expect(counts.draining).toBeGreaterThan(0)
    expect(counts.idle).toBeGreaterThan(0)
    expect(counts.busy).toBeGreaterThan(0)

    const lost = WORKERS_SEED.filter(
      (worker) => worker.leaseSec !== null && worker.heartbeatAgeSec >= 45
    )
    expect(lost.length).toBeGreaterThan(0)
    expect(lost[0].leaseSec).toBeLessThanOrEqual(30)
  })

  it("leaves one pool genuinely empty, with a backlog waiting on it", () => {
    // The empty state is reachable in the running app, not only in a story:
    // atlas has no containers at all and four items queued against it.
    const atlasWorkers = WORKERS_SEED.filter(
      (worker) => worker.projectId === "p_atlas"
    )
    expect(atlasWorkers).toHaveLength(0)

    const atlas = QUEUE_SEED.filter((item) => item.projectId === "p_atlas")
    expect(backlogOf(atlas)).toBeGreaterThan(0)

    const pool = WORKER_POOLS_SEED.find((entry) => entry.projectId === "p_atlas")
    // `min idle = 0`, which is what makes that emptiness correct rather than
    // an outage — and what the other empty state says out loud.
    expect(pool?.minIdle).toBe(0)
  })

  it("keeps a queued row that is perfectly normal, so the column is not all alarm", () => {
    const fresh = QUEUE_SEED.filter(
      (item) => item.status === "queued" && item.ageSec < 60
    )
    expect(fresh.length).toBeGreaterThan(0)
    expect(unclaimedOver(QUEUE_SEED, AGE_STALLED_SEC)).toBeLessThan(
      backlogOf(QUEUE_SEED)
    )
  })

  it("has no `stalled` status anywhere, because there is no such state", () => {
    const statuses = new Set(QUEUE_SEED.map((item) => item.status))
    expect(statuses.has("stalled" as never)).toBe(false)
    // The stall that happened is a failure, which is the only shape it has.
    expect(statuses.has("failed")).toBe(true)
  })
})

import { describe, expect, it } from "vitest"

import {
  groupAttention,
  needsHuman,
  readAttention,
} from "@/domains/home/model/attention"
import type { RunStatus, RunSummary } from "@/domains/runs/model/types"

function run(
  id: string,
  status: RunStatus,
  durationSec = 60,
  projectId = "p_test"
): RunSummary {
  return {
    id,
    projectId,
    app: "billing-api",
    title: `ticket ${id}`,
    status,
    current: "w1",
    model: "worker",
    cost: 0.4,
    tokens: 8000,
    durationSec,
    done: status === "success",
    workItems: [
      {
        id: "w1",
        profile: "planner",
        label: "decide the retry budget",
        status,
        dependsOn: [],
      },
    ],
  }
}

describe("what counts as needing a person", () => {
  it("takes the three statuses whose next move is not the swarm's", () => {
    expect(needsHuman(run("a", "escalated"))).toBe(true)
    expect(needsHuman(run("b", "failed"))).toBe(true)
    expect(needsHuman(run("c", "waiting"))).toBe(true)
  })

  // The same set `buildProfileFlow` folds into `blockedTotal`, so the figure
  // this screen shouts and the one the duty screen's header whispers are the
  // same figure. A run the swarm is still moving is nobody's decision.
  it("leaves everything the swarm is still moving alone", () => {
    expect(needsHuman(run("d", "running"))).toBe(false)
    expect(needsHuman(run("e", "queued"))).toBe(false)
    expect(needsHuman(run("f", "success"))).toBe(false)
  })
})

describe("the attention reading", () => {
  it("orders worst first, and breaks a tie on the longest wait", () => {
    const reading = readAttention([
      run("waiting-short", "waiting", 30),
      run("failed", "failed", 10),
      run("waiting-long", "waiting", 900),
      run("escalated", "escalated", 5),
      run("running", "running", 4000),
    ])

    // escalated before failed before waiting — `TRIAGE_RANK`, not the alphabet
    // — and inside a bucket the one that has been stuck longest goes first.
    expect(reading.items.map((item) => item.run.id)).toEqual([
      "escalated",
      "failed",
      "waiting-long",
      "waiting-short",
    ])
    expect(reading.worst).toBe("escalated")
  })

  it("splits the total into the statuses behind it, in the same order", () => {
    const reading = readAttention([
      run("w1", "waiting"),
      run("f1", "failed"),
      run("w2", "waiting"),
      run("e1", "escalated"),
    ])

    expect(reading.items).toHaveLength(4)
    expect(reading.mix).toEqual([
      { status: "escalated", count: 1 },
      { status: "failed", count: 1 },
      { status: "waiting", count: 2 },
    ])
  })

  it("offers a decision where the product has one, and only the run where it does not", () => {
    const reading = readAttention([
      run("e", "escalated"),
      run("f", "failed"),
      run("w", "waiting"),
    ])
    const acts = Object.fromEntries(
      reading.items.map((item) => [item.run.id, [...item.acts]])
    )

    // A failed gate has no one-click answer and holds no worker slot, so the
    // honest move is to go and read what the gate said. Inventing a stop or an
    // approve there would be inventing a semantic the duty list does not have.
    expect(acts).toEqual({
      e: ["approve", "stop", "open"],
      f: ["open"],
      w: ["approve", "stop", "open"],
    })
  })

  it("reads a shift where nothing is owed as a shift, not as an absence", () => {
    const reading = readAttention([
      run("r1", "running", 90),
      run("r2", "running", 900),
      run("q1", "queued", 0),
      run("s1", "success", 400),
    ])

    expect(reading.items).toEqual([])
    expect(reading.mix).toEqual([])
    expect(reading.worst).toBeNull()
    // The counts that let the clear verdict prove the data arrived.
    expect(reading.running.map((entry) => entry.id)).toEqual(["r2", "r1"])
    expect(reading.queued).toBe(1)
    expect(reading.total).toBe(4)
  })

  it("reads an empty swarm without inventing anything", () => {
    const reading = readAttention([])

    expect(reading.items).toEqual([])
    expect(reading.worst).toBeNull()
    expect(reading.running).toEqual([])
    expect(reading.queued).toBe(0)
    expect(reading.total).toBe(0)
  })
})

describe("bucketing the list", () => {
  it("keeps the buckets worst-first and names each one once", () => {
    const reading = readAttention([
      run("w1", "waiting"),
      run("e1", "escalated"),
      run("f1", "failed"),
      run("w2", "waiting"),
    ])
    const groups = groupAttention(reading.items)

    expect(groups.map((group) => group.status)).toEqual([
      "escalated",
      "failed",
      "waiting",
    ])
    expect(groups.map((group) => group.items.length)).toEqual([1, 1, 2])
    // The product's own words, so a bucket heading and the duty screen's header
    // are saying the same thing about the same runs.
    expect(groups[2].reason).toBe("waiting on a human")
  })

  it("groups nothing when nothing is owed", () => {
    expect(groupAttention([])).toEqual([])
  })
})

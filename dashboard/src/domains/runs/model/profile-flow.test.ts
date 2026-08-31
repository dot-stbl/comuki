import { describe, expect, it } from "vitest"

import {
  buildProfileFlow,
  triageOrder,
} from "@/domains/runs/model/profile-flow"
import type {
  RunStatus,
  RunSummary,
  WorkItem,
} from "@/domains/runs/model/types"

function work(
  id: string,
  profile: string,
  status: RunStatus,
  dependsOn: string[] = []
): WorkItem {
  return { id, profile, label: `шаг ${id}`, status, dependsOn }
}

function run(
  id: string,
  status: RunStatus,
  current: string,
  workItems: WorkItem[]
): RunSummary {
  return {
    id,
    projectId: "p_test",
    app: "billing-api",
    title: `run ${id}`,
    status,
    current,
    model: "worker",
    cost: 0.1,
    tokens: 1000,
    durationSec: 60,
    done: false,
    workItems,
  }
}

/**
 * A three-item chain: explorer, implementer, verifier. The run stands on the
 * first item that has not finished, the way a real one does.
 */
function chain(id: string, status: RunStatus, ...statuses: RunStatus[]) {
  const profiles = ["explorer", "implementer", "verifier"]
  const items = statuses.map((itemStatus, index) =>
    work(
      `w${index + 1}`,
      profiles[index],
      itemStatus,
      index === 0 ? [] : [`w${index}`]
    )
  )
  const standing = items.find((item) => item.status !== "success")
  return run(id, status, standing?.id ?? items[items.length - 1].id, items)
}

function nodeFor(flow: ReturnType<typeof buildProfileFlow>, profile: string) {
  return flow.columns
    .flatMap((column) => column.nodes)
    .find((node) => node.profile === profile)
}

describe("buildProfileFlow", () => {
  it("splits a profile's pool by status and counts what cleared it", () => {
    const flow = buildProfileFlow([
      chain("a", "running", "success", "success", "running"),
      chain("b", "waiting", "success", "success", "waiting"),
      chain("c", "running", "success", "running", "queued"),
    ])

    const verifier = nodeFor(flow, "verifier")
    const explorer = nodeFor(flow, "explorer")

    expect(verifier?.pool).toBe(2)
    expect(verifier?.poolByStatus.waiting).toBe(1)
    expect(verifier?.cleared).toBe(0)
    expect(verifier?.entered).toBe(2)
    expect(explorer?.cleared).toBe(3)
    expect(explorer?.pool).toBe(0)
  })

  it("counts runs, not items — a plan may invoke one profile many times", () => {
    const flow = buildProfileFlow([
      run("a", "running", "w3", [
        work("w1", "planner", "success"),
        work("w2", "implementer", "running", ["w1"]),
        work("w3", "implementer", "running", ["w1"]),
      ]),
    ])

    expect(nodeFor(flow, "implementer")?.pool).toBe(1)
    expect(nodeFor(flow, "implementer")?.entered).toBe(1)
    expect(flow.total).toBe(1)
  })

  it("counts a run once even where it has both cleared and live work", () => {
    const flow = buildProfileFlow([
      run("a", "running", "w3", [
        work("w1", "planner", "success"),
        work("w2", "implementer", "success", ["w1"]),
        work("w3", "implementer", "running", ["w1"]),
      ]),
    ])

    const implementer = nodeFor(flow, "implementer")
    expect(implementer?.pool).toBe(1)
    expect(implementer?.cleared).toBe(0)
    expect(implementer?.entered).toBe(1)
  })

  it("counts a transition only once the upstream item has finished", () => {
    const planned = buildProfileFlow([
      run("a", "running", "w1", [
        work("w1", "planner", "running"),
        work("w2", "implementer", "queued", ["w1"]),
      ]),
    ])
    expect(planned.edges).toEqual([])

    const flowed = buildProfileFlow([
      run("a", "running", "w2", [
        work("w1", "planner", "success"),
        work("w2", "implementer", "running", ["w1"]),
      ]),
    ])
    expect(flowed.edges).toEqual([
      { from: "planner", to: "implementer", count: 1 },
    ])
  })

  it("adds up the transitions it sees across runs", () => {
    const flow = buildProfileFlow([
      chain("a", "running", "success", "success", "running"),
      chain("b", "running", "success", "success", "running"),
      chain("c", "running", "success", "running", "queued"),
    ])

    expect(flow.edges).toEqual([
      { from: "explorer", to: "implementer", count: 3 },
      { from: "implementer", to: "verifier", count: 2 },
    ])
  })

  it("orders columns by median depth, not by a catalog", () => {
    // `docs` is only ever the last item, `explorer` only ever the first, and
    // the board has never been told which is which.
    const flow = buildProfileFlow([
      run("a", "running", "w4", [
        work("w1", "explorer", "success"),
        work("w2", "implementer", "success", ["w1"]),
        work("w3", "tester", "success", ["w2"]),
        work("w4", "docs", "running", ["w3"]),
      ]),
    ])

    expect(flow.order).toEqual(["explorer", "implementer", "tester", "docs"])
    expect(flow.columns.map((column) => column.depth)).toEqual([0, 1, 2, 3])
  })

  it("puts profiles observed at the same depth in one column", () => {
    const flow = buildProfileFlow([
      run("a", "running", "w2", [
        work("w1", "planner", "success"),
        work("w2", "implementer", "running", ["w1"]),
        work("w3", "docs", "running", ["w1"]),
      ]),
    ])

    const parallel = flow.columns.find((column) => column.parallel)
    expect(parallel?.nodes.map((node) => node.profile)).toEqual([
      "docs",
      "implementer",
    ])
  })

  it("measures the flow through each gap between columns", () => {
    const flow = buildProfileFlow([
      chain("a", "running", "success", "success", "running"),
      chain("b", "running", "success", "success", "running"),
      chain("c", "running", "success", "running", "queued"),
    ])

    // Three runs left the explorer column; only two of them reached the last.
    expect(flow.crossings).toEqual([3, 2])
  })

  it("points the pinch at the profile holding the most blocked items", () => {
    const flow = buildProfileFlow([
      chain("a", "waiting", "success", "success", "waiting"),
      chain("b", "escalated", "success", "success", "escalated"),
      chain("c", "failed", "success", "failed", "queued"),
    ])

    expect(flow.pinchProfile).toBe("verifier")
    expect(nodeFor(flow, "verifier")?.blocked).toBe(2)
  })

  it("counts runs, not items, for the two figures the header shows", () => {
    const flow = buildProfileFlow([
      chain("a", "running", "success", "running", "queued"),
      chain("b", "waiting", "success", "success", "waiting"),
    ])

    expect(flow.runningTotal).toBe(1)
    expect(flow.blockedTotal).toBe(1)
    expect(flow.total).toBe(2)
  })

  it("counts a run that is merely running as unblocked", () => {
    const flow = buildProfileFlow([
      chain("a", "running", "success", "running", "queued"),
    ])

    expect(flow.pinchProfile).toBeNull()
    expect(flow.blockedTotal).toBe(0)
    expect(flow.runningTotal).toBe(1)
  })

  it("survives an empty swarm", () => {
    const flow = buildProfileFlow([])
    expect(flow.columns).toEqual([])
    expect(flow.order).toEqual([])
    expect(flow.edges).toEqual([])
    expect(flow.total).toBe(0)
    expect(flow.pinchProfile).toBeNull()
  })

  it("draws a board from graphs that share no shape at all", () => {
    const flow = buildProfileFlow([
      // Three items, closed without a plan step.
      chain("a", "running", "success", "running", "queued"),
      // A branch of two, joined by a reviewer.
      run("b", "running", "w3", [
        work("w1", "planner", "success"),
        work("w2", "implementer", "success", ["w1"]),
        work("w3", "implementer", "running", ["w1"]),
        work("w4", "reviewer", "queued", ["w2", "w3"]),
      ]),
    ])

    expect(flow.order.length).toBeGreaterThan(3)
    expect(flow.crossings).toHaveLength(flow.columns.length - 1)
  })
})

describe("triageOrder", () => {
  it("orders runs worst-first, longest in step first inside a status", () => {
    const runs = [
      chain("a", "running", "success", "success", "running"),
      chain("b", "escalated", "success", "success", "escalated"),
      chain("c", "waiting", "success", "waiting", "queued"),
      chain("d", "waiting", "success", "success", "waiting"),
    ]

    expect(triageOrder(runs).map((item) => item.id)).toEqual([
      "b",
      "c",
      "d",
      "a",
    ])
  })

  it("keeps every run — ordering is not filtering", () => {
    const runs = [
      chain("a", "running", "success", "running", "queued"),
      chain("b", "waiting", "success", "success", "waiting"),
    ]

    expect(triageOrder(runs)).toHaveLength(2)
    expect(runs.map((item) => item.id)).toEqual(["a", "b"])
  })
})

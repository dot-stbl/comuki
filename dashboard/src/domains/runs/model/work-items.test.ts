import { describe, expect, it } from "vitest"

import type { RunSummary, WorkItem } from "@/domains/runs/model/types"
import {
  currentItem,
  currentLabel,
  currentProfile,
  dependenciesOf,
  isLongEdge,
  itemColumns,
  itemDepths,
  orderedItems,
  planGraph,
} from "@/domains/runs/model/work-items"

function work(
  id: string,
  profile: string,
  dependsOn: string[] = [],
  label = `шаг ${id}`
): WorkItem {
  return { id, profile, label, status: "queued", dependsOn }
}

function run(current: string, workItems: WorkItem[]): RunSummary {
  return {
    id: "r1",
    projectId: "p_test",
    app: "billing-api",
    title: "run",
    status: "running",
    current,
    model: "worker",
    cost: 0,
    tokens: 0,
    durationSec: 0,
    done: false,
    workItems,
  }
}

describe("itemDepths", () => {
  it("takes the longest path, so nothing is drawn before what it waits on", () => {
    const depths = itemDepths([
      work("a", "explorer"),
      work("b", "implementer", ["a"]),
      work("c", "implementer", ["b"]),
      // Depends on both the root and the far end of the chain: the long path wins.
      work("d", "reviewer", ["a", "c"]),
    ])

    expect(depths.get("a")).toBe(0)
    expect(depths.get("b")).toBe(1)
    expect(depths.get("c")).toBe(2)
    expect(depths.get("d")).toBe(3)
  })

  it("ignores a dependency that is not in the run", () => {
    const depths = itemDepths([work("a", "explorer", ["gone"])])
    expect(depths.get("a")).toBe(0)
  })

  it("cuts a cycle instead of recursing into it", () => {
    const depths = itemDepths([
      work("a", "explorer", ["b"]),
      work("b", "implementer", ["a"]),
    ])

    expect(depths.size).toBe(2)
  })
})

describe("orderedItems", () => {
  it("reads in dependency order whatever order the payload arrived in", () => {
    const ordered = orderedItems([
      work("c", "tester", ["b"]),
      work("a", "explorer"),
      work("b", "implementer", ["a"]),
    ])

    expect(ordered.map((item) => item.id)).toEqual(["a", "b", "c"])
  })
})

describe("itemColumns", () => {
  it("bands the graph by depth and marks the branches", () => {
    const columns = itemColumns([
      work("a", "explorer"),
      work("b", "implementer", ["a"]),
      work("c", "implementer", ["a"]),
      work("d", "reviewer", ["b", "c"]),
    ])

    expect(
      columns.map((column) => column.items.map((item) => item.id))
    ).toEqual([["a"], ["b", "c"], ["d"]])
    expect(columns.map((column) => column.parallel)).toEqual([
      false,
      true,
      false,
    ])
  })

  it("handles a plan the brain closed in three items", () => {
    const columns = itemColumns([
      work("a", "explorer"),
      work("b", "implementer", ["a"]),
      work("c", "verifier", ["b"]),
    ])

    expect(columns).toHaveLength(3)
    expect(columns.every((column) => !column.parallel)).toBe(true)
  })
})

describe("currentItem", () => {
  const items = [
    work("a", "explorer", [], "прочитать обработчик вебхуков"),
    work("b", "implementer", ["a"], "переписать обработчик"),
  ]

  it("reads the profile and the brain's label off the item the run is on", () => {
    const subject = run("b", items)
    expect(currentItem(subject)?.id).toBe("b")
    expect(currentProfile(subject)).toBe("implementer")
    expect(currentLabel(subject)).toBe("переписать обработчик")
  })

  it("falls back to the first item rather than showing a blank row", () => {
    expect(currentItem(run("gone", items))?.id).toBe("a")
  })

  it("has nothing to show for a run with no plan yet", () => {
    expect(currentItem(run("", []))).toBeUndefined()
    expect(currentProfile(run("", []))).toBe("")
  })
})

describe("planGraph", () => {
  it("measures how many columns a dependency crosses", () => {
    // `d` joins two lanes: one beside it, one two bands back. The layered form
    // draws the first for free and cannot draw the second at all.
    const graph = planGraph([
      work("a", "explorer"),
      work("b", "implementer", ["a"]),
      work("c", "implementer", ["b"]),
      work("d", "reviewer", ["b", "c"]),
    ])

    expect(
      graph.dependencies.get("d")?.map((entry) => [entry.item.id, entry.span])
    ).toEqual([
      ["c", 1],
      ["b", 2],
    ])
  })

  it("drops a dependency that is not in the run rather than inventing a node", () => {
    const graph = planGraph([work("a", "explorer", ["gone"])])
    expect(graph.dependencies.get("a")).toEqual([])
  })

  it("calls a span of one near and anything further a long edge", () => {
    const graph = planGraph([
      work("a", "explorer"),
      work("b", "implementer", ["a"]),
      work("c", "reviewer", ["a", "b"]),
    ])

    const [near, far] = graph.dependencies.get("c") ?? []
    expect(isLongEdge(near)).toBe(false)
    expect(isLongEdge(far)).toBe(true)
  })
})

describe("planGraph blocked", () => {
  function withStatus(
    id: string,
    status: WorkItem["status"],
    dependsOn: string[] = []
  ): WorkItem {
    return { ...work(id, "implementer", dependsOn), status }
  }

  it("marks everything queued behind a failure", () => {
    const graph = planGraph([
      withStatus("a", "success"),
      withStatus("b", "failed", ["a"]),
      withStatus("c", "queued", ["b"]),
      withStatus("d", "queued", ["c"]),
    ])

    expect(graph.blocked.get("c")).toBe("failed")
    expect(graph.blocked.get("d")).toBe("failed")
    // The failed item is not blocked by itself — its own status already says so.
    expect(graph.blocked.has("b")).toBe(false)
    expect(graph.blocked.has("a")).toBe(false)
  })

  it("marks everything queued behind an escalation, and says which it was", () => {
    const graph = planGraph([
      withStatus("a", "escalated"),
      withStatus("b", "queued", ["a"]),
    ])

    expect(graph.blocked.get("b")).toBe("escalated")
  })

  it("leaves a human gate alone — waiting is the normal state, not a fault", () => {
    const graph = planGraph([
      withStatus("a", "waiting"),
      withStatus("b", "queued", ["a"]),
    ])

    expect(graph.blocked.size).toBe(0)
  })

  it("stops propagating at a lane that cleared the failure", () => {
    // `b` failed, but `c` ran anyway on the other lane, so `d` is merely queued.
    const graph = planGraph([
      withStatus("a", "success"),
      withStatus("b", "failed", ["a"]),
      withStatus("c", "success", ["a"]),
      withStatus("d", "queued", ["c"]),
    ])

    expect(graph.blocked.has("d")).toBe(false)
  })

  it("cuts a cycle instead of recursing into it", () => {
    const graph = planGraph([
      withStatus("a", "queued", ["b"]),
      withStatus("b", "queued", ["a"]),
    ])

    expect(graph.blocked.size).toBe(0)
  })
})

describe("dependenciesOf", () => {
  it("reads one item's dependencies nearest column first", () => {
    const items = [
      work("a", "explorer"),
      work("b", "implementer", ["a"]),
      work("c", "reviewer", ["a", "b"]),
    ]

    expect(dependenciesOf(items, "c").map((entry) => entry.item.id)).toEqual([
      "b",
      "a",
    ])
    expect(dependenciesOf(items, "a")).toEqual([])
  })
})

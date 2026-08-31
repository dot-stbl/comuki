import { describe, expect, it, vi } from "vitest"
import { fireEvent, render, screen } from "@testing-library/react"

import type { RunStatus, WorkItem } from "@/domains/runs/model/types"
import { orderedItems } from "@/domains/runs/model/work-items"

import { RunGraph } from "./run-graph"

function work(
  id: string,
  profile: string,
  status: RunStatus,
  dependsOn: string[] = [],
  label = `шаг ${id}`
): WorkItem {
  return { id, profile, label, status, dependsOn }
}

function nodes(container: HTMLElement): HTMLElement[] {
  return [
    ...container.querySelectorAll<HTMLElement>('[data-test="work-item-node"]'),
  ]
}

function nodeFor(container: HTMLElement, id: string): HTMLElement {
  const found = container.querySelector<HTMLElement>(`[data-item="${id}"]`)
  if (!found) {
    throw new Error(`no node for ${id}`)
  }
  return found
}

/** Three items, one lane: the plan the brain closed without planning. */
const chain = orderedItems([
  work("w1", "explorer", "success"),
  work("w2", "implementer", "running", ["w1"]),
  work("w3", "verifier", "queued", ["w2"]),
])

/** A failure at depth 2, with two items queued behind it. */
const failing = orderedItems([
  work("w1", "explorer", "success"),
  work("w2", "planner", "success", ["w1"]),
  work("w3", "implementer", "failed", ["w2"]),
  work("w4", "reviewer", "queued", ["w3"]),
  work("w5", "verifier", "queued", ["w4"]),
])

/** A reviewer joining a lane beside it and a lane two columns back. */
const longEdge = orderedItems([
  work("w1", "explorer", "success"),
  work("w2", "planner", "success", ["w1"]),
  work("w3", "implementer", "success", ["w2"]),
  work("w4", "implementer", "success", ["w2"]),
  work("w5", "implementer", "running", ["w3"]),
  work("w6", "reviewer", "queued", ["w4", "w5"]),
])

describe("RunGraph", () => {
  it("bands the plan into one column per depth, with a connector between", () => {
    const { container } = render(
      <RunGraph items={chain} current="w2" onSelect={() => {}} />
    )

    expect(
      container.querySelectorAll('[data-test="run-graph-column"]')
    ).toHaveLength(3)
    // One fewer connector than columns: the last band has nothing after it.
    expect(container.querySelectorAll('[aria-hidden="true"]').length).toBeGreaterThan(0)
    expect(nodes(container).map((node) => node.dataset.item)).toEqual([
      "w1",
      "w2",
      "w3",
    ])
  })

  it("names the item, its profile and its state — never hue alone", () => {
    const { container } = render(
      <RunGraph items={chain} current="w2" onSelect={() => {}} />
    )

    expect(nodeFor(container, "w2").getAttribute("aria-label")).toBe(
      "шаг w2. implementer, running. the run is standing here."
    )
    // And the same three facts survive with every colour channel switched off.
    expect(nodeFor(container, "w2").textContent).toContain("implementer")
    expect(nodeFor(container, "w2").textContent).toContain("running")
  })

  it("marks a branching column with how wide it is", () => {
    const { container } = render(
      <RunGraph items={longEdge} onSelect={() => {}} />
    )

    const parallel = container.querySelector("[data-parallel]")
    expect(parallel?.textContent).toContain("2 parallel")
  })

  it("says which items are going nowhere behind a failure", () => {
    const { container } = render(
      <RunGraph items={failing} current="w3" onSelect={() => {}} />
    )

    // The failed item states its own status; the two behind it are `queued`,
    // which is true and misleading, so they say `blocked` as well.
    expect(nodeFor(container, "w3").dataset.blocked).toBeUndefined()
    expect(nodeFor(container, "w4").dataset.blocked).toBe("failed")
    expect(nodeFor(container, "w5").dataset.blocked).toBe("failed")
    expect(nodeFor(container, "w4").getAttribute("aria-label")).toContain(
      "blocked by a failure upstream"
    )
    expect(nodeFor(container, "w4").textContent).toContain("blocked")
  })

  it("never hides a dependency that skips a column", () => {
    const { container } = render(
      <RunGraph items={longEdge} onSelect={() => {}} />
    )

    // w6 waits on w5 (the column beside it) and on w4 (two bands back). The
    // near one is drawn by adjacency; the far one has to be said out loud.
    const node = nodeFor(container, "w6")
    expect(node.textContent).toContain("depends on 1 earlier")
    expect(node.getAttribute("aria-label")).toContain(
      "depends on 1 item more than one column back: шаг w4 (implementer)"
    )
    expect(node.getAttribute("title")).toContain("depends on 1 earlier")
  })

  it("lights what a node waits on when it is focused, not only hovered", () => {
    const { container } = render(
      <RunGraph items={longEdge} onSelect={() => {}} />
    )

    fireEvent.focus(nodeFor(container, "w6"))

    expect(nodeFor(container, "w6").dataset.trace).toBe("from")
    expect(nodeFor(container, "w5").dataset.trace).toBe("to")
    // The distant one is marked more strongly — it is the one you cannot find
    // by looking at the column beside it.
    expect(nodeFor(container, "w4").dataset.trace).toBe("to")
    expect(nodeFor(container, "w4").dataset.long).toBe("")
    expect(nodeFor(container, "w1").dataset.trace).toBeUndefined()
  })

  it("reports the item a click means", () => {
    const onSelect = vi.fn()
    const { container } = render(
      <RunGraph items={chain} selected="w1" onSelect={onSelect} />
    )

    expect(nodeFor(container, "w1").getAttribute("aria-pressed")).toBe("true")
    fireEvent.click(nodeFor(container, "w3"))
    expect(onSelect).toHaveBeenCalledWith("w3")
  })

  it("keeps one tab stop and walks the rest with arrow keys", () => {
    const onSelect = vi.fn()
    const { container } = render(
      <RunGraph items={longEdge} selected="w3" onSelect={onSelect} />
    )

    // Forty-two tab stops is not navigation. Only the selection is tabbable.
    expect(
      nodes(container).filter((node) => node.getAttribute("tabindex") === "0")
    ).toHaveLength(1)
    expect(nodeFor(container, "w3").getAttribute("tabindex")).toBe("0")

    nodeFor(container, "w3").focus()
    fireEvent.keyDown(container.firstChild as HTMLElement, { key: "ArrowDown" })
    expect(onSelect).toHaveBeenLastCalledWith("w4")

    nodeFor(container, "w3").focus()
    fireEvent.keyDown(container.firstChild as HTMLElement, {
      key: "ArrowRight",
    })
    expect(onSelect).toHaveBeenLastCalledWith("w5")
  })

  it("draws a plan preview as static content rather than as dead controls", () => {
    const { container } = render(<RunGraph items={chain} fit="content" />)

    expect(container.querySelectorAll("button")).toHaveLength(0)
    // Nothing to override the accessible name, so the node reads its own text.
    const node = nodeFor(container, "w2")
    expect(node.getAttribute("aria-label")).toBeNull()
    expect(node.textContent).toContain("шаг w2")
    expect(node.textContent).toContain("implementer")
  })

  it("says so rather than rendering a blank box when there is no plan", () => {
    render(<RunGraph items={[]} onSelect={() => {}} />)
    expect(screen.getByText("No work items in this plan.")).toBeTruthy()
  })
})

import { describe, expect, it, vi } from "vitest"
import { fireEvent, render, screen } from "@testing-library/react"

import type {
  WorkItem,
  WorkItemInspector,
} from "@/domains/runs/model/types"
import { dependenciesOf, orderedItems } from "@/domains/runs/model/work-items"

import { WorkItemInspectorPanel } from "./work-item-inspector"

function work(
  id: string,
  profile: string,
  dependsOn: string[] = [],
  label = `шаг ${id}`
): WorkItem {
  return { id, profile, label, status: "queued", dependsOn }
}

const items = orderedItems([
  work("w1", "explorer"),
  work("w2", "planner", ["w1"]),
  work("w3", "implementer", ["w2"]),
  work("w4", "implementer", ["w2"]),
  work("w5", "implementer", ["w3"]),
  // Beside it and two bands back.
  work("w6", "reviewer", ["w4", "w5"]),
])

const info: WorkItemInspector = {
  role: "lead",
  env: "env_9f21",
  tokens: "4.2k",
  cost: "0.31",
  inputs: [{ icon: "box", label: "upstream diff" }],
  outputs: [{ icon: "box", label: "review notes", detail: "blocking" }],
  files: null,
  gate: [{ name: "types", status: "success" }],
  events: [{ time: "00:12", status: "running", text: "read diff" }],
}

function renderPanel(itemId: string, onSelect?: (id: string) => void) {
  const item = items.find((entry) => entry.id === itemId) ?? items[0]
  return render(
    <WorkItemInspectorPanel
      item={item}
      index={items.indexOf(item) + 1}
      total={items.length}
      info={info}
      waitsOn={dependenciesOf(items, item.id)}
      onSelect={onSelect}
    />
  )
}

describe("WorkItemInspectorPanel", () => {
  it("leads with the brain's own name for the step", () => {
    renderPanel("w6")
    expect(
      screen.getByRole("heading", { level: 2, name: "шаг w6" })
    ).toBeTruthy()
    expect(screen.getByText("reviewer")).toBeTruthy()
    expect(screen.getByText("item 6 of 6")).toBeTruthy()
  })

  it("lists every dependency with its distance, near ones included", () => {
    const { container } = renderPanel("w6")
    const deps = [
      ...container.querySelectorAll<HTMLElement>(
        '[data-test="work-item-dependency"]'
      ),
    ]

    // Nearest first, and the near one says "previous column" rather than
    // leaving a reader to infer that a missing distance means adjacent.
    expect(deps.map((node) => node.dataset.item)).toEqual(["w5", "w4"])
    expect(deps[0].textContent).toContain("previous column")
    expect(deps[1].textContent).toContain("2 columns back")
  })

  it("makes a long dependency reachable instead of hover-only", () => {
    const onSelect = vi.fn()
    const { container } = renderPanel("w6", onSelect)

    const far = container.querySelector<HTMLElement>('[data-item="w4"]')
    expect(far?.getAttribute("aria-label")).toBe(
      "Show шаг w4, implementer, queued, 2 columns back."
    )
    fireEvent.click(far as HTMLElement)
    expect(onSelect).toHaveBeenCalledWith("w4")
  })

  it("counts the long edges in the section head", () => {
    renderPanel("w6")
    expect(
      screen.getByText("1 more than one column back")
    ).toBeTruthy()
  })

  it("says an item starts the plan rather than showing an empty list", () => {
    renderPanel("w1")
    expect(
      screen.getByText("Nothing — this item starts the plan.")
    ).toBeTruthy()
  })

  it("renders a diff with the sign, not only the tint", () => {
    render(
      <WorkItemInspectorPanel
        item={items[2]}
        index={3}
        total={6}
        info={{
          ...info,
          outputs: [],
          files: [
            {
              path: "src/webhooks/stripe.ts",
              added: 2,
              deleted: 1,
              lines: [
                { kind: "ctx", line: "42", text: "const a = 1" },
                { kind: "add", line: "43", text: "const b = 2" },
                { kind: "del", line: "44", text: "const c = 3" },
              ],
            },
          ],
        }}
      />
    )

    expect(screen.getByText("src/webhooks/stripe.ts")).toBeTruthy()
    expect(screen.getByText("+2")).toBeTruthy()
    expect(screen.getByText("−1")).toBeTruthy()
    expect(screen.getByText("+")).toBeTruthy()
    expect(screen.getByText("−")).toBeTruthy()
  })
})

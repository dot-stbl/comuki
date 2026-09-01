import { render, screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { describe, expect, it, vi } from "vitest"

import { TaskPriorityField } from "@/domains/tasks/ui/task-priority-field"

function mountField(
  value: "low" | "normal" | "high",
  onValueChange: (next: "low" | "normal" | "high") => void = () => {}
) {
  return render(
    <TaskPriorityField
      value={value}
      onValueChange={onValueChange}
      data-test="task-priority"
    />
  )
}

const segments = () =>
  Array.from(document.querySelectorAll('[data-test="task-priority-segment"]'))

describe("TaskPriorityField — the segmented priority picker", () => {
  it("renders three segments in a fixed order", () => {
    mountField("normal")
    expect(segments().map((s) => s.getAttribute("data-value"))).toEqual([
      "low",
      "normal",
      "high",
    ])
  })

  it("marks exactly one segment selected", () => {
    mountField("high")
    const selected = segments().filter(
      (s) => s.getAttribute("data-selected") === "true"
    )
    expect(selected).toHaveLength(1)
    expect(selected[0].getAttribute("data-value")).toBe("high")
  })

  it("hands the picked value back", async () => {
    const user = userEvent.setup()
    const onValueChange = vi.fn()
    mountField("normal", onValueChange)

    await user.click(screen.getByText("high"))
    expect(onValueChange).toHaveBeenCalledWith("high")

    await user.click(screen.getByText("low"))
    expect(onValueChange).toHaveBeenCalledWith("low")
  })

  it("keeps the radio group reachable by keyboard", () => {
    mountField("normal")
    // Real radios under the segments: the platform's arrow-key group and
    // single tab stop, not a div with aria-checked.
    const radios = Array.from(
      document.querySelectorAll('input[name="task-priority"]')
    ).filter((r) => (r as HTMLInputElement).type === "radio")
    expect(radios).toHaveLength(3)
    const checked = radios.filter(
      (r) => (r as HTMLInputElement).checked
    ) as HTMLInputElement[]
    expect(checked).toHaveLength(1)
    expect(checked[0].value).toBe("normal")
  })
})
import { render, screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { describe, expect, it } from "vitest"

import { Button } from "./button"
import { Tooltip } from "./tooltip"

/* Focus rather than hover: React Aria opens on focus with no dwell, while a
   pointer has to clear the shared warmup timer first. What is under test is
   the wiring, not the timer. */

describe("Tooltip", () => {
  it("hands the name back on keyboard focus", async () => {
    const user = userEvent.setup()
    render(
      <Tooltip content="Live runs">
        <button type="button" aria-label="Live runs">
          ▸
        </button>
      </Tooltip>
    )

    expect(screen.queryByRole("tooltip")).toBeNull()

    await user.tab()

    expect((await screen.findByRole("tooltip")).textContent).toBe("Live runs")
  })

  it("describes the control without becoming its name", async () => {
    const user = userEvent.setup()
    render(
      <Tooltip content="Runs waiting on a human">
        <button type="button" aria-label="Approvals">
          ▸
        </button>
      </Tooltip>
    )

    await user.tab()
    await screen.findByRole("tooltip")

    // The name survives the tooltip: a control described as one thing and named
    // another is still reachable by the name a person was told to look for.
    const trigger = screen.getByRole("button", { name: "Approvals" })
    expect(trigger.getAttribute("aria-describedby")).not.toBeNull()
    expect(trigger.getAttribute("aria-label")).toBe("Approvals")
  })

  it("stays out of the way when the control already says it", async () => {
    const user = userEvent.setup()
    render(
      <Tooltip content="Attention" disabled>
        <button type="button">Attention</button>
      </Tooltip>
    )

    await user.tab()

    expect(screen.getByRole("button", { name: "Attention" })).not.toBeNull()
    expect(screen.queryByRole("tooltip")).toBeNull()
  })

  it("leaves the trigger's own props alone", async () => {
    const user = userEvent.setup()
    render(
      <Tooltip content="Queue">
        <button type="button" className="own-class" data-test="own-hook">
          Queue
        </button>
      </Tooltip>
    )

    const trigger = screen.getByRole("button", { name: "Queue" })
    expect(trigger.className).toBe("own-class")
    expect(trigger.getAttribute("data-test")).toBe("own-hook")

    await user.tab()
    expect((await screen.findByRole("tooltip")).textContent).toBe("Queue")
  })
})

describe("a kit control inside a tooltip", () => {
  it("drops its native title, so the sentence arrives once", () => {
    render(
      <Tooltip content="needs approver on plexor">
        <Button denied="needs approver on plexor" aria-label="Approve r_1" />
      </Tooltip>
    )

    const control = screen.getByRole("button", { name: "Approve r_1" })
    // The refusal itself is untouched — only the second delivery of it goes.
    expect(control.getAttribute("aria-disabled")).toBe("true")
    expect(control.getAttribute("title")).toBeNull()
    // The reason is still on the element — it just is not a second tooltip.
    expect(control.getAttribute("data-denied")).toBe("needs approver on plexor")
  })

  it("keeps the native title when nothing else is carrying it", () => {
    render(<Button denied="needs approver" aria-label="Approve r_2" />)

    expect(
      screen.getByRole("button", { name: "Approve r_2" }).getAttribute("title")
    ).toBe("needs approver")
  })
})

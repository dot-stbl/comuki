import { useState } from "react"
import { render, screen, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { describe, expect, it, vi } from "vitest"

import { Tabs, TabList, Tab, TabPanel } from "./tabs"

/**
 * The kit's tabs. React Aria composes the keyboard grammar (arrow keys
 * move focus, Home/End jump to the ends) and the aria semantics
 * (`role="tablist"`, `role="tab"`, `role="tabpanel"`); the kit owns the
 * look.
 */

interface TabSpec {
  id: string
  label: string
  isDisabled?: boolean
  body: string
}

const TABS: TabSpec[] = [
  { id: "library", label: "library", body: "library content" },
  { id: "gate", label: "gate", body: "gate content" },
  {
    id: "settings",
    label: "settings",
    body: "settings content",
    isDisabled: true,
  },
]

function Harness({
  tabs = TABS,
  initial,
  onSelectionChange,
}: {
  tabs?: TabSpec[]
  initial?: string
  onSelectionChange?: (next: string) => void
}) {
  const [selected, setSelected] = useState(initial ?? tabs[0]?.id ?? "")
  return (
    <Tabs
      selectedKey={selected}
      onSelectionChange={(next) => {
        const value = String(next)
        setSelected(value)
        onSelectionChange?.(value)
      }}
    >
      <TabList aria-label="Sections">
        {tabs.map((tab) => (
          <Tab
            key={tab.id}
            id={tab.id}
            isDisabled={tab.isDisabled}
            data-test={`tab-${tab.id}`}
          >
            {tab.label}
          </Tab>
        ))}
      </TabList>
      {tabs.map((tab) => (
        <TabPanel key={tab.id} id={tab.id} data-test={`panel-${tab.id}`}>
          {tab.body}
        </TabPanel>
      ))}
    </Tabs>
  )
}

const tablist = () => screen.getByRole("tablist")
const tabs = () => within(tablist()).getAllByRole("tab")

describe("the tabs strip", () => {
  it("renders N tabs in the order given", () => {
    render(<Harness />)
    expect(tabs()).toHaveLength(3)
  })

  it("marks the selected tab and shows only its panel", () => {
    render(<Harness initial="gate" />)

    const library = screen.getByRole("tab", { name: "library" })
    const gate = screen.getByRole("tab", { name: "gate" })

    expect(library.getAttribute("aria-selected")).toBe("false")
    expect(gate.getAttribute("aria-selected")).toBe("true")

    // Only the selected tab's panel is in the tree — the others
    // unmount on switch, so a screen pays only for the one it is
    // showing.
    expect(screen.queryByText("library content")).toBeNull()
    expect(screen.getByText("gate content")).toBeTruthy()
  })

  it("moves focus with the arrow keys and skips a disabled tab", async () => {
    const user = userEvent.setup()
    render(<Harness />)

    const first = screen.getByRole("tab", { name: "library" })
    first.focus()
    expect(document.activeElement).toBe(first)

    await user.keyboard("{ArrowRight}")
    // The middle tab is disabled — focus lands on the last one.
    expect(document.activeElement).toBe(
      screen.getByRole("tab", { name: "gate" })
    )

    await user.keyboard("{ArrowLeft}")
    // Back to the first one — focus skips the disabled middle tab.
    expect(document.activeElement).toBe(
      screen.getByRole("tab", { name: "library" })
    )
  })

  it("Home and End jump to the ends", async () => {
    const user = userEvent.setup()
    render(<Harness />)

    const first = screen.getByRole("tab", { name: "library" })
    first.focus()

    await user.keyboard("{End}")
    expect(document.activeElement).toBe(
      screen.getByRole("tab", { name: "gate" })
    )

    await user.keyboard("{Home}")
    expect(document.activeElement).toBe(first)
  })

  it("selects the focused tab on Enter", async () => {
    const onSelectionChange = vi.fn()
    const user = userEvent.setup()
    render(<Harness onSelectionChange={onSelectionChange} />)

    const library = screen.getByRole("tab", { name: "library" })
    library.focus()
    await user.keyboard("{ArrowRight}")
    await user.keyboard("{Enter}")

    // The disabled middle tab was skipped on arrow-right; focus landed
    // on `gate`, and Enter selected it.
    expect(onSelectionChange).toHaveBeenLastCalledWith("gate")
  })

  it("skips a disabled tab on activation", async () => {
    const onSelectionChange = vi.fn()
    const user = userEvent.setup()
    render(<Harness onSelectionChange={onSelectionChange} />)

    // Focus the disabled tab directly. React Aria lets the disabled tab
    // take focus so a screen reader can still announce its name — but
    // pressing `Enter` on it does not select it.
    const disabled = screen.getByRole("tab", { name: "settings" })
    disabled.focus()
    await user.keyboard("{Enter}")
    expect(onSelectionChange).not.toHaveBeenCalled()
  })
})

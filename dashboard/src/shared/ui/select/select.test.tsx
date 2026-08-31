import { useState } from "react"
import { render, screen, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { describe, expect, it, vi } from "vitest"

import {
  DataTableToolbar,
  type DataColumn,
  type DataTableFilterValues,
} from "../data-table"
import { SelectField } from "../form"
import { Select } from "./select"
import { nativeSelect, pickOption, selectValues } from "./test-select"

/* What a native `<select>` gave away for free, asserted rather than assumed.
 *
 * The kit had two selects — a native one on forms and a React Aria listbox in
 * the data table's toolbar — and the promotion kept the second. Everything the
 * first used to hand over without being asked (keyboard, type-ahead, a name,
 * something a thumb can hit, a form element a browser can fill) is a case in
 * this file, because "React Aria does that" is a claim and not a test.
 *
 * A React Aria popover renders into a portal, so the open list is found on
 * `screen` and never inside the container a test rendered into. */

const ROLES = [
  { value: "viewer", label: "viewer" },
  { value: "member", label: "member" },
  { value: "approver", label: "approver" },
  { value: "operator", label: "operator" },
]

function Harness({
  initial = "viewer",
  onChange,
  hint,
  error,
  disabled,
  placeholder,
}: {
  initial?: string
  onChange?: (next: string) => void
  hint?: string
  error?: string
  disabled?: boolean
  placeholder?: string
}) {
  const [value, setValue] = useState(initial)
  return (
    <SelectField
      id="role"
      label="role"
      value={value}
      options={ROLES}
      hint={hint}
      error={error}
      disabled={disabled}
      placeholder={placeholder}
      onValueChange={(next) => {
        setValue(next)
        onChange?.(next)
      }}
    />
  )
}

const trigger = () => screen.getByLabelText("role")

describe("the label names the control, and there is only one of it", () => {
  it("hands the trigger back from the field's own label", () => {
    render(<Harness />)

    // One match, not two: the field's `<label for>` points at the trigger, and
    // the hidden form element React Aria keeps beside it is not labelled a
    // second time. A test that has to disambiguate a label is a test written
    // against a control that announces itself twice.
    const control = trigger()
    expect(control.tagName).toBe("BUTTON")
    expect(control.getAttribute("aria-haspopup")).toBe("listbox")
  })

  it("carries the label in its accessible name, not only the value", () => {
    render(<Harness />)

    // React Aria composes the name out of the value node and the label, in
    // that order — the listbox pattern's own convention. What matters is that
    // the field's word is in there: a control announced as "viewer" alone
    // tells a screen reader nothing about what it is for.
    const labelled = trigger().getAttribute("aria-labelledby") ?? ""
    const ids = labelled.split(" ")
    const words = ids
      .map((id) => document.getElementById(id)?.textContent?.trim())
      .filter(Boolean)
    expect(words).toContain("role")
    expect(words).toContain("viewer")
  })

  it("points at whichever line is under the control", () => {
    const { rerender } = render(<Harness hint="six, and no seventh" />)
    expect(trigger().getAttribute("aria-describedby")).toBe("role-description")
    expect(document.getElementById("role-description")?.textContent).toBe(
      "six, and no seventh"
    )

    rerender(<Harness hint="six, and no seventh" error="pick one" />)
    expect(document.getElementById("role-description")?.textContent).toBe(
      "pick one"
    )
    // The state is on `data-invalid`: React Aria's `Button` filters unknown
    // ARIA attributes off the element, so the reading is carried by the
    // described-by line — which is `role="alert"` — rather than by the control.
    expect(trigger().hasAttribute("data-invalid")).toBe(true)
  })
})

describe("the keyboard, which is what the native control was kept for", () => {
  it("opens on the down arrow and commits on enter", async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()
    render(<Harness onChange={onChange} />)

    await user.tab()
    expect(document.activeElement).toBe(trigger())

    await user.keyboard("{ArrowDown}")
    const list = await screen.findByRole("listbox")
    expect(within(list).getAllByRole("option")).toHaveLength(4)

    await user.keyboard("{ArrowDown}{Enter}")

    expect(onChange).toHaveBeenCalledWith("member")
    expect(trigger().textContent).toContain("member")
  })

  it("opens on enter as well, because a native select did", async () => {
    const user = userEvent.setup()
    render(<Harness />)

    await user.tab()
    await user.keyboard("{Enter}")

    expect(await screen.findByRole("listbox")).toBeTruthy()
  })

  it("abandons on escape with the value it arrived with", async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()
    render(<Harness onChange={onChange} />)

    await user.tab()
    await user.keyboard("{ArrowDown}")
    await screen.findByRole("listbox")
    await user.keyboard("{ArrowDown}{Escape}")

    expect(screen.queryByRole("listbox")).toBeNull()
    expect(onChange).not.toHaveBeenCalled()
    expect(trigger().textContent).toContain("viewer")
  })

  it("reaches the last option with end", async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()
    render(<Harness onChange={onChange} />)

    await user.tab()
    await user.keyboard("{ArrowDown}")
    await screen.findByRole("listbox")
    await user.keyboard("{End}{Enter}")

    expect(onChange).toHaveBeenCalledWith("operator")
  })
})

describe("type-ahead, closed and open", () => {
  it("jumps to a value by typing at the shut control", async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()
    render(<Harness onChange={onChange} />)

    await user.tab()
    await user.keyboard("o")

    // Shut, exactly like the native control: typing at a closed select moves
    // the value, it does not open a list to move it in.
    expect(screen.queryByRole("listbox")).toBeNull()
    expect(onChange).toHaveBeenCalledWith("operator")
  })

  it("matches on more than the first letter", async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()
    render(<Harness onChange={onChange} />)

    await user.tab()
    // "m" alone would land on member; "ap" has to reach approver.
    await user.keyboard("ap")

    expect(onChange).toHaveBeenLastCalledWith("approver")
  })

  it("moves the focused row when the list is open", async () => {
    const user = userEvent.setup()
    render(<Harness />)

    await user.tab()
    await user.keyboard("{ArrowDown}")
    const list = await screen.findByRole("listbox")
    await user.keyboard("app")

    expect(
      within(list)
        .getByRole("option", { name: "approver" })
        .hasAttribute("data-focused")
    ).toBe(true)
  })
})

describe("something a thumb can hit", () => {
  it("opens on a press and closes on an outside press", async () => {
    const user = userEvent.setup()
    render(
      <>
        <Harness />
        <button type="button">elsewhere</button>
      </>
    )

    // Held before the list opens: React Aria hides the rest of the document
    // from assistive tech while an overlay is up, so a role query would not
    // find this button once it is — which is itself the behaviour under test.
    const elsewhere = screen.getByRole("button", { name: "elsewhere" })

    await user.click(trigger())
    const list = await screen.findByRole("listbox")
    // Real rows in a popover, not a hover menu: each option is its own press
    // target and says what it is.
    expect(
      within(list)
        .getAllByRole("option")
        .map((option) => option.textContent)
    ).toEqual(["viewer", "member", "approver", "operator"])

    await user.click(elsewhere)
    expect(screen.queryByRole("listbox")).toBeNull()
  })

  it("chooses by pressing a row", async () => {
    const onChange = vi.fn()
    render(<Harness onChange={onChange} />)

    await pickOption(trigger(), "approver")

    expect(onChange).toHaveBeenCalledWith("approver")
    expect(screen.queryByRole("listbox")).toBeNull()
  })
})

describe("the form element a browser can still fill", () => {
  it("keeps every option on a native select beside the trigger", () => {
    render(<Harness />)

    // This is what autofill and a plain `<form>` submit see, and it is the
    // seam `test-select.ts` reads and writes through.
    expect(selectValues(trigger())).toEqual([
      "viewer",
      "member",
      "approver",
      "operator",
    ])
    expect(nativeSelect(trigger()).value).toBe("viewer")
  })

  it("agrees with the list: both paths write the same value", async () => {
    const onChange = vi.fn()
    render(<Harness onChange={onChange} />)

    await pickOption(trigger(), "member")

    expect(onChange).toHaveBeenCalledWith("member")
    expect(nativeSelect(trigger()).value).toBe("member")
  })
})

describe("what a filter needs that a form does not", () => {
  function Filter() {
    const [value, setValue] = useState("")
    return (
      <>
        <span id="app-label">app</span>
        <Select
          id="app"
          size="sm"
          clearable
          value={value}
          onValueChange={setValue}
          options={[
            { value: "plexor", label: "plexor" },
            { value: "auth-svc", label: "auth-svc" },
          ]}
          placeholder="all apps"
          active={value !== ""}
          aria-labelledby="app-label"
          data-test="app-filter"
        />
      </>
    )
  }

  it("offers a row that puts the value back to nothing", async () => {
    const user = userEvent.setup()
    render(<Filter />)

    const control = document.querySelector<HTMLElement>(
      '[data-test="app-filter"]'
    )!

    expect(control.textContent).toContain("all apps")
    expect(control.hasAttribute("data-active")).toBe(false)

    await pickOption(control, "plexor")
    expect(control.textContent).toContain("plexor")
    expect(control.hasAttribute("data-active")).toBe(true)

    await user.click(control)
    await user.click(await screen.findByRole("option", { name: "all apps" }))
    expect(control.textContent).toContain("all apps")
    expect(control.hasAttribute("data-active")).toBe(false)
  })

  it("marks the empty reading the same way whichever shape it takes", () => {
    render(
      <>
        <Filter />
        <Harness initial="" placeholder="pick a role" />
      </>
    )

    // A filter's "all apps" and a form's placeholder are the same statement —
    // this control is not narrowing anything — so they read the same.
    expect(
      document
        .querySelector('[data-test="app-filter"]')!
        .hasAttribute("data-empty")
    ).toBe(true)
    expect(trigger().hasAttribute("data-empty")).toBe(true)
    expect(trigger().textContent).toContain("pick a role")
  })

  it("refuses the press while it is disabled", async () => {
    const user = userEvent.setup()
    render(<Harness disabled />)

    await user.click(trigger())
    expect(screen.queryByRole("listbox")).toBeNull()
    // And the values are still readable without opening anything, which is
    // why the test seam goes through the form element.
    expect(selectValues(trigger())).toHaveLength(4)
  })
})

/* One control, two call sites -------------------------------------------- */

const appColumn: DataColumn<{ app: string }> = {
  accessorKey: "app",
  header: "app",
  meta: {
    filter: {
      kind: "select",
      placeholder: "all apps",
      options: [
        { value: "plexor", label: "plexor" },
        { value: "auth-svc", label: "auth-svc" },
      ],
    },
  },
}

function Bar() {
  const [filters, setFilters] = useState<DataTableFilterValues>({})
  return (
    <DataTableToolbar
      columns={[appColumn]}
      filters={filters}
      onFiltersChange={setFilters}
    />
  )
}

describe("a form and a toolbar now wear the same control", () => {
  it("renders one component in both places", async () => {
    const user = userEvent.setup()
    render(
      <>
        <Harness />
        <Bar />
      </>
    )

    const field = trigger()
    await user.click(screen.getByRole("button", { name: /^Filters/ }))
    const filter = document.querySelector<HTMLElement>(
      '[data-test="data-table-filter-app"]'
    )!

    // Same element, same role, same chrome. The class carries a per-module
    // hash, so an identical token on both is the strongest statement jsdom can
    // make that these two boxes are painted by one stylesheet — a second
    // select coming back would carry a different hash and fail here.
    expect(filter.tagName).toBe(field.tagName)
    expect(filter.getAttribute("aria-haspopup")).toBe(
      field.getAttribute("aria-haspopup")
    )
    expect(filter.className.split(" ")[0]).toMatch(/^_trigger_/)
    expect(filter.className.split(" ")[0]).toBe(field.className.split(" ")[0])

    // And the same list underneath it, option rows and all.
    await user.click(filter)
    const list = await screen.findByRole("listbox")
    expect(within(list).getAllByRole("option").length).toBeGreaterThan(0)
    expect(within(list).getAllByRole("option")[0]?.className).toMatch(
      /^_option_/
    )
  })

  it("differs only by the props the toolbar hands it", async () => {
    const user = userEvent.setup()
    render(
      <>
        <Harness />
        <Bar />
      </>
    )

    await user.click(screen.getByRole("button", { name: /^Filters/ }))
    const filter = document.querySelector<HTMLElement>(
      '[data-test="data-table-filter-app"]'
    )!

    // Density and the "all" row are the toolbar's two needs, and both are
    // props: a denser class on the same trigger, and one extra option that a
    // form's closed list does not get.
    expect(filter.className).toMatch(/_sm_/)
    expect(trigger().className).not.toMatch(/_sm_/)
    expect(selectValues(filter)).toEqual(["plexor", "auth-svc"])

    await user.click(filter)
    const list = await screen.findByRole("listbox")
    expect(
      within(list)
        .getAllByRole("option")
        .map((option) => option.textContent)
    ).toEqual(["all apps", "plexor", "auth-svc"])
  })
})

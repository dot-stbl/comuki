import { useState } from "react"
import { render, screen, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { describe, expect, it, vi } from "vitest"

import { ComboboxField } from "./combobox-field"

/* A combobox is a string the operator types with a list to filter it. The
 * three behaviours the primitive buys over `TextField` are: a focused value
 * survives the popover being closed, the list shrinks to what matches, and a
 * key outside the list can still be accepted when `allowsCustomValue` says so.
 *
 * What `TextField` already does — keyboard, a label, a hint, an error — is
 * not retested here; it is retested in `form/`. The cases below are the
 * diff between the two. */

const MODELS = [
  { value: "lead-xl-2", label: "lead-xl-2" },
  { value: "lead-mid-2", label: "lead-mid-2" },
  { value: "worker-sm-4", label: "worker-sm-4" },
]

function Harness({
  initial = "",
  onChange,
  allowsCustomValue = false,
  disabled = false,
}: {
  initial?: string
  onChange?: (next: string) => void
  allowsCustomValue?: boolean
  disabled?: boolean
}) {
  const [value, setValue] = useState(initial)
  return (
    <ComboboxField
      id="model"
      label="model"
      value={value}
      options={MODELS}
      allowsCustomValue={allowsCustomValue}
      disabled={disabled}
      onValueChange={(next) => {
        setValue(next)
        onChange?.(next)
      }}
    />
  )
}

// React Aria gives the input `role="combobox"`. `getByLabelText("model")` would
// also match the chevron button — both carry the same accessible name, since
// `aria-labelledby` is shared by the parent group — so the role is the
// unambiguous seam.
const input = () =>
  screen.getByRole("combobox", { name: "model" }) as HTMLInputElement

describe("the combobox is a string the operator types, with a list to filter", () => {
  it("starts with the value the harness passed", () => {
    render(<Harness initial="lead-xl-2" />)
    expect(input().value).toBe("lead-xl-2")
  })

  it("opens the list to every option when the operator types nothing", async () => {
    const user = userEvent.setup()
    render(<Harness />)

    // React Aria's ComboBox opens on focus + ArrowDown — the same key the
    // Select opens on, and the key the platform's native `<select>` used to
    // open on. A click alone focuses but does not open, which is the same
    // shape `<select>` had.
    input().focus()
    await user.keyboard("{ArrowDown}")
    const list = await screen.findByRole("listbox")
    expect(within(list).getAllByRole("option")).toHaveLength(3)
  })

  it("narrows the list to what matches the typed query, case-insensitively", async () => {
    const user = userEvent.setup()
    render(<Harness />)

    input().focus()
    // The popover opens on focus + ArrowDown — the same key `Select` opens
    // on, and the key the platform's native `<select>` used to open on.
    await user.keyboard("{ArrowDown}")
    await user.keyboard("LEAD")

    const list = await screen.findByRole("listbox")
    const rows = within(list).getAllByRole("option")
    expect(rows).toHaveLength(2)
    expect(rows.map((node) => node.textContent)).toEqual([
      "lead-xl-2",
      "lead-mid-2",
    ])
  })

  it("writes every keystroke through `onValueChange`, not only the chosen one", async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()
    render(<Harness onChange={onChange} />)

    input().focus()
    await user.keyboard("lead")

    // `onInputChange` fires per keystroke — a controlled combobox stays in
    // sync with the parent's state as the operator types, the way a TextField
    // does. The list filtering happens off the live value, not off what was
    // committed.
    expect(onChange).toHaveBeenLastCalledWith("lead")
  })

  it("commits a clicked option, and writes its value", async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()
    render(<Harness onChange={onChange} />)

    input().focus()
    // The popover opens on the same key `<select>` opened on.
    await user.keyboard("{ArrowDown}")
    const list = await screen.findByRole("listbox")
    await user.click(within(list).getByRole("option", { name: "worker-sm-4" }))

    expect(onChange).toHaveBeenLastCalledWith("worker-sm-4")
    expect(input().value).toBe("worker-sm-4")
  })

  it("closes on Escape and keeps the value it arrived with", async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()
    render(<Harness initial="lead-xl-2" onChange={onChange} />)

    input().focus()
    await user.keyboard("{ArrowDown}")
    expect(await screen.findByRole("listbox")).toBeTruthy()

    await user.keyboard("{Escape}")
    expect(screen.queryByRole("listbox")).toBeNull()
    expect(input().value).toBe("lead-xl-2")
  })
})

describe("`allowsCustomValue` is the seam that lets the list fall behind the answer", () => {
  it("rejects a typed value not in the list when the seam is closed", async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()
    render(<Harness onChange={onChange} />)

    input().focus()
    await user.keyboard("{ArrowDown}")
    await user.keyboard("brand-new-model")
    await user.keyboard("{Enter}")

    // Closed by default: the value the operator typed is not in the seed,
    // and on Enter the field reverts to the previous selection (here, empty).
    // The text in the input clears back to "" — the rejection is visible.
    expect(onChange).toHaveBeenLastCalledWith("")
    expect(input().value).toBe("")
  })

  it("accepts a typed value not in the list when the seam is open", async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()
    render(<Harness onChange={onChange} allowsCustomValue />)

    input().focus()
    await user.keyboard("{ArrowDown}")
    await user.keyboard("brand-new-model")
    await user.keyboard("{Enter}")

    // Open seam: the typed value the list does not know is the new value.
    expect(onChange).toHaveBeenLastCalledWith("brand-new-model")
    expect(input().value).toBe("brand-new-model")
  })
})

describe("disabled is the same voice every other disabled field wears", () => {
  it("refuses typing — the input is not focusable, the chevron is faint", () => {
    render(<Harness disabled />)

    const control = input()
    expect(screen.queryByRole("listbox")).toBeNull()
    expect(control.disabled).toBe(true)
  })
})
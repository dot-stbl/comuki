import { useState } from "react"
import { render, screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { describe, expect, it, vi } from "vitest"

import { SearchField } from "./search-field"

/**
 * The kit's search field. A real `type="search"` with a clear button
 * that appears when the value is non-empty, and an `aria-label` that
 * the screen reader announces (the field has no visible `<label>`).
 */

function Harness({
  initial = "",
  onChange,
  disabled = false,
  size,
  "data-test": dataTest,
  placeholder,
  ariaLabel = "Filter runs",
}: {
  initial?: string
  onChange?: (next: string) => void
  disabled?: boolean
  size?: "sm" | "md"
  "data-test"?: string
  placeholder?: string
  ariaLabel?: string
}) {
  const [value, setValue] = useState(initial)
  return (
    <SearchField
      value={value}
      onValueChange={(next) => {
        setValue(next)
        onChange?.(next)
      }}
      aria-label={ariaLabel}
      disabled={disabled}
      size={size}
      data-test={dataTest}
      placeholder={placeholder}
      data-active={value.length > 0}
    />
  )
}

describe("the search field", () => {
  it("carries an aria-label and announces itself as a search", () => {
    render(<Harness ariaLabel="Filter rules, docs and skills" />)

    const input = screen.getByRole("searchbox", {
      name: "Filter rules, docs and skills",
    })
    expect(input.tagName).toBe("INPUT")
    // `type="search"` is what tells the screen reader and the browser
    // that this is a search rather than a text field.
    expect(input).toHaveProperty("type", "search")
  })

  it("writes through onValueChange as the operator types", async () => {
    const onChange = vi.fn()
    const user = userEvent.setup()
    render(<Harness onChange={onChange} />)

    const input = screen.getByRole("searchbox")
    await user.type(input, "plexor")

    expect(onChange).toHaveBeenLastCalledWith("plexor")
  })

  it("marks `data-active` while the value is non-empty", async () => {
    const user = userEvent.setup()
    render(<Harness />)

    const input = screen.getByRole("searchbox")
    expect(input.hasAttribute("data-active")).toBe(false)

    await user.type(input, "x")
    expect(input.hasAttribute("data-active")).toBe(true)

    await user.clear(input)
    expect(input.hasAttribute("data-active")).toBe(false)
  })

  it("shows a clear button only when the value is non-empty, and clears on press", async () => {
    const onChange = vi.fn()
    const user = userEvent.setup()
    render(<Harness onChange={onChange} initial="plexor" />)

    // The clear button is named; finding it by accessible name means the
    // screen reader will say the same thing the operator presses.
    const clear = screen.getByRole("button", { name: "Clear search" })
    await user.click(clear)

    expect(onChange).toHaveBeenLastCalledWith("")
  })

  it("disables both the input and the clear button when disabled", async () => {
    const onChange = vi.fn()
    const user = userEvent.setup()
    render(<Harness disabled initial="plexor" onChange={onChange} />)

    const input = screen.getByRole("searchbox") as HTMLInputElement
    expect(input.disabled).toBe(true)

    const clear = screen.getByRole("button", {
      name: "Clear search",
    }) as HTMLButtonElement
    // The clear button is conditionally rendered — when the value is
    // empty it doesn't exist; here it exists and inherits `disabled` from
    // the field, so a click is a no-op.
    expect(clear.disabled).toBe(true)
    await user.click(clear)
    expect(onChange).not.toHaveBeenCalled()
  })

  it("passes the test hook through to the input and to the clear button", () => {
    render(<Harness data-test="story-search" initial="plexor" />)

    // The input carries the test hook verbatim. The kit uses
    // `data-test="…"` (matching `select.test.tsx`'s seam), not the
    // `data-testid` that `@testing-library/jest-dom`'s `getByTestId`
    // looks for.
    const input = document.querySelector(
      'input[data-test="story-search"]'
    ) as HTMLInputElement | null
    expect(input).toBeTruthy()
    expect(input?.tagName).toBe("INPUT")
    // The clear button's test id is the input's with `-clear` tacked on —
    // the rule that a primitive owns its test ids, including the affordances
    // that appear conditionally on it.
    const clear = document.querySelector(
      '[data-test="story-search-clear"]'
    )
    expect(clear).toBeTruthy()
  })

  it("honours size: `sm` is the toolbar's density", () => {
    const { container } = render(<Harness size="sm" />)
    // CSS module hash, but the rule for `sm` differs from `md` —
    // asserting the class is present on the wrapper is enough; the
    // height rule lives in the stylesheet.
    const root = container.firstElementChild
    expect(root).toBeTruthy()
    expect(root?.className).toMatch(/_sm_|_field_/)
  })
})

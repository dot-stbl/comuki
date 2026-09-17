import { readFileSync } from "node:fs"
import { dirname, join } from "node:path"
import { fileURLToPath } from "node:url"
import { render, screen } from "@testing-library/react"
import { describe, expect, it } from "vitest"

import { SelectField } from "./select-field"
import { TextField } from "./text-field"

/* Read off disk, because jsdom computes no layout and the defect this guards
   against was a size: the switch's track had been taking its height from
   `--h-meter`, the 8px unit meant for swatches and meter bars. That made the
   drawn control ten pixels tall — and, because the invisible input lies exactly
   on the track, made the click target ten pixels too. */
const SHEET = readFileSync(
  join(dirname(fileURLToPath(import.meta.url)), "form.module.css"),
  "utf8"
)

/** The value of one property inside the rule named by `selector`. */
function declared(selector: string, property: string): string | undefined {
  const start = SHEET.indexOf(selector + " {")
  if (start === -1) return undefined
  const body = SHEET.slice(start, SHEET.indexOf("}", start))
  const line = body
    // Newline by code point: this file is written by a script, and a literal
    // escape inside one is one round of escaping away from a parse error.
    .split(String.fromCharCode(10))
    .find((entry) => entry.trim().startsWith(property + ":"))
  return line?.split(":")[1]?.replace(";", "").trim()
}

describe("the switch is a control, not a meter", () => {
  it("never sizes itself from the meter unit", () => {
    expect(declared(".switch", "block-size")).not.toContain("--h-meter")
  })

  it("draws a track a person can actually see", () => {
    expect(declared(".switch", "block-size")).toBe("1.25rem")
  })

  it("gives the input a hit area larger than the track it sits on", () => {
    // The size of the thing you press is not the size of the thing you see, and
    // only one of the two has a floor: 24px, WCAG 2.2 target size — the same
    // floor `--h-button-sm` refuses to go below.
    expect(declared(".switchInput", "inset")).toBe("-2px")
  })
})

describe("the switch is one row tall", () => {
  it("puts the label at the start and the track at the end", () => {
    // A switch is a settings row — label, then a toggle at the end — and the
    // row it sits in must not stretch to fit it.
    expect(declared(".switchRow", "justify-content")).toBe("space-between")
  })

  it("carries no state word beside the track", () => {
    // The word repeated what the thumb and the fill already said, added a
    // second line to the control, and stretched every row it sat in. The
    // state itself still travels: `role="switch"` plus the input's own
    // checked state, asserted where the component renders.
    expect(SHEET.includes(".switchState")).toBe(false)
    expect(SHEET.includes(".switchText")).toBe(false)
  })
})

/** The marker the label wears, queried the way the product queries anything. */
function requiredMark(): HTMLElement | null {
  return document.querySelector<HTMLElement>('[data-test="field-required"]')
}

describe("a required field says so where a screen reader can hear it", () => {
  it("adds nothing to the accessible name when it is not required", () => {
    render(
      <TextField
        id="remote"
        label="git remote"
        value=""
        onValueChange={() => {}}
      />
    )
    expect(screen.getByLabelText("git remote")).toBe(
      screen.getByRole("textbox")
    )
    expect(requiredMark()).toBeNull()
  })

  it("separates the word from the name with a real space", () => {
    // The whole finding, in one assertion. Without the text node between them
    // the accessible name composes to "git remoterequired": one word to a
    // screen reader, and no `getByLabelText(/^git remote/)` in the product
    // matches it any more. A margin cannot do this job — the accessibility
    // tree never sees the stylesheet.
    render(
      <TextField
        id="remote"
        label="git remote"
        required
        value=""
        onValueChange={() => {}}
      />
    )
    expect(requiredMark()?.closest("label")?.textContent).toBe(
      "git remote required"
    )
    expect(screen.getByLabelText("git remote required")).toBe(
      screen.getByRole("textbox")
    )
  })

  it("keeps the word visible to assistive tech", () => {
    // Hiding it is the obvious move and it is wrong: React Aria rebuilds a
    // select trigger's accessible name from its own value node, dropping
    // `aria-required` with the association, so on `SelectField` the name is
    // the only channel the fact has. `aria-hidden` here would take the marker
    // away from exactly the control that cannot do without it.
    render(
      <TextField
        id="remote"
        label="git remote"
        required
        value=""
        onValueChange={() => {}}
      />
    )
    const mark = requiredMark()
    expect(mark?.textContent).toBe("required")
    expect(mark?.closest("[aria-hidden]")).toBeNull()
  })

  it("marks the control itself required, without the native attribute", () => {
    // `aria-required` and not `required`: this is a signal, and the form's own
    // validation stays the only thing that refuses a submit.
    render(
      <TextField
        id="remote"
        label="git remote"
        required
        value=""
        onValueChange={() => {}}
      />
    )
    const input = screen.getByRole("textbox")
    expect(input.getAttribute("aria-required")).toBe("true")
    expect(input.hasAttribute("required")).toBe(false)
  })

  it("reaches the select through its label, which is all a select has", () => {
    render(
      <SelectField
        id="provider"
        label="provider"
        required
        value=""
        options={[{ value: "openai", label: "OpenAI" }]}
        onValueChange={() => {}}
      />
    )
    expect(requiredMark()?.closest("label")?.textContent).toBe(
      "provider required"
    )
  })
})

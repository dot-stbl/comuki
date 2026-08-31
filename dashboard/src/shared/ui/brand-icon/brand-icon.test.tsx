import { render, screen } from "@testing-library/react"
import { describe, expect, it } from "vitest"

import { BrandIcon } from "./brand-icon"
import { BRAND_IDS, BRAND_MARKS, isBrandId } from "./brand-marks"

/* A monochrome mark is a recognition cue and nothing else: drained of its brand
   colour and set at eleven pixels in a table row, it is worth exactly as much
   as the reader's memory of it. Everything below is about the half that has to
   survive that — the name. */

describe("BrandIcon", () => {
  it("gives every mark in the registry an accessible name", () => {
    // Not a sample: the loop is the point. A mark added to the registry without
    // a name is a blank cell to a screen reader, and this is where that fails.
    for (const brand of BRAND_IDS) {
      const { unmount } = render(<BrandIcon brand={brand} />)
      expect(
        screen.getByRole("img", { name: BRAND_MARKS[brand].title })
      ).not.toBeNull()
      unmount()
    }
  })

  it("says it in the surface's own words when the surface has words", () => {
    // The sources table calls GitHub `github`, because that is the word its
    // filter, its form and its column heading all use. The mark's own spelling
    // is the default, not the rule.
    render(<BrandIcon brand="github" label="github" />)

    expect(screen.getByRole("img", { name: "github" })).not.toBeNull()
    expect(screen.queryByRole("img", { name: "GitHub" })).toBeNull()
  })

  it("goes decorative only when it is asked to, and then completely", () => {
    render(<BrandIcon brand="docker" label={null} />)

    // No role, no name, no announcement — correct exactly when something else
    // in the same control already names the provider, and wrong otherwise.
    expect(screen.queryByRole("img")).toBeNull()
    const mark = document.querySelector('[data-test="brand-icon"]')
    expect(mark?.getAttribute("aria-hidden")).toBe("true")
    expect(mark?.getAttribute("aria-label")).toBeNull()
  })

  it("sizes from the scale and never from a length", () => {
    // The same contract `button.module.css` enforces: a call site picks a step,
    // not a number, so a mark cannot end up taller than the type beside it.
    render(<BrandIcon brand="jira" size="xs" />)

    const mark = document.querySelector('[data-test="brand-icon"]')
    expect(mark?.getAttribute("width")).toBeNull()
    expect(mark?.getAttribute("height")).toBeNull()
    expect(mark?.getAttribute("class")).toContain("xs")
  })

  it("draws the product's own mark through the component that owns it", () => {
    // `comuki` carries no path of its own. If this viewBox ever stops being the
    // cropped one, the registry has grown a second copy of the artwork.
    render(<BrandIcon brand="comuki" label="native" />)

    const mark = screen.getByRole("img", { name: "native" })
    expect(mark.getAttribute("viewBox")).toBe("39 95 422 310")
    expect(mark.getAttribute("class")).toContain("wide")
  })

  it("carries no colour of its own", () => {
    // The chrome rule: colour belongs to the data. A mark that arrived with a
    // brand hex would be the one saturated thing in a drained table.
    for (const brand of BRAND_IDS) {
      const mark = BRAND_MARKS[brand]
      expect(JSON.stringify(mark.art)).not.toMatch(/#[0-9a-f]{3,8}/i)
    }
    render(<BrandIcon brand="gitlab" />)
    expect(
      document.querySelector('[data-test="brand-icon"]')?.getAttribute("fill")
    ).toBe("currentColor")
  })

  it("knows which providers it has no mark for", () => {
    // The registry is a closed list on purpose: a provider that is not in it —
    // Yandex Tracker, and every model vendor — renders its name instead of a
    // mark somebody drew from memory.
    expect(isBrandId("github")).toBe(true)
    expect(isBrandId("yandex-tracker")).toBe(false)
    expect(isBrandId("openai")).toBe(false)
    expect(isBrandId("anthropic")).toBe(false)
  })
})

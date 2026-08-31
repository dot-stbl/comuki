import { readFileSync } from "node:fs"
import { dirname, join } from "node:path"
import { fileURLToPath } from "node:url"
import { describe, expect, it } from "vitest"

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

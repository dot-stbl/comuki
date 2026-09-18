/**
 * The ASCII mark: dimensions, printable-ASCII only, and paintMark
 * wrapping every line in the given color.
 */
import { describe, expect, it } from "bun:test"
import { colors, stripAnsi } from "../theme"
import { MARK_SMALL, paintMark } from "./mark"

const PRINTABLE_ASCII = /^[\x20-\x7e]*$/

describe("MARK_SMALL", () => {
  it("is 11 cols by 5 rows of printable ASCII", () => {
    expect(MARK_SMALL).toHaveLength(5)
    for (const line of MARK_SMALL) {
      expect(line).toHaveLength(11)
      expect(line).toMatch(PRINTABLE_ASCII)
    }
  })

  it("reads as a left slab plus two right pillars with a crossbar", () => {
    expect(MARK_SMALL[0]).toBe("+---+  |  |")
    expect(MARK_SMALL[2]).toBe("|   |  +--+")
    expect(MARK_SMALL[4]).toBe("+---+  |  |")
  })
})

describe("paintMark", () => {
  it("wraps every line in the given color and resets after", () => {
    const painted = paintMark(MARK_SMALL, colors.accent)
    expect(painted).toHaveLength(MARK_SMALL.length)
    for (const [index, line] of painted.entries()) {
      expect(line.startsWith(colors.accent)).toBe(true)
      expect(line.endsWith("\x1b[0m")).toBe(true)
      expect(stripAnsi(line)).toBe(MARK_SMALL[index])
    }
  })
})

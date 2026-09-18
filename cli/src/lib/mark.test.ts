/**
 * The ASCII mark: dimensions, printable-ASCII only, and paintMark
 * wrapping every line in the given color.
 */
import { describe, expect, it } from "bun:test"
import { colors, stripAnsi } from "../theme"
import { MARK_SMALL, MARK_TINY_FRAMES, paintMark } from "./mark"

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

describe("MARK_TINY_FRAMES", () => {
  it("is 3–5 frames of 3×3 printable ASCII", () => {
    expect(MARK_TINY_FRAMES.length).toBeGreaterThanOrEqual(3)
    expect(MARK_TINY_FRAMES.length).toBeLessThanOrEqual(5)
    for (const frame of MARK_TINY_FRAMES) {
      expect(frame).toHaveLength(3)
      for (const line of frame) {
        expect(line).toHaveLength(3)
        expect(line).toMatch(PRINTABLE_ASCII)
      }
    }
  })

  it("breathes the crossbar from empty through full", () => {
    expect(MARK_TINY_FRAMES[0]?.[1]).toBe("   ")
    expect(MARK_TINY_FRAMES[1]?.[1]).toBe(" - ")
    expect(MARK_TINY_FRAMES[2]?.[1]).toBe("===")
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

/**
 * The ASCII mark: dimensions, printable-ASCII only, and paintMark
 * wrapping every line in the given color.
 */
import { describe, expect, it } from "bun:test"
import { colors, stripAnsi } from "../theme"
import { MARK_COMPACT, MARK_WELCOME, paintMark } from "./mark"

const PRINTABLE_ASCII = /^[\x20-\x7e]*$/

describe("Comuki ASCII marks", () => {
  it("keeps the compact mark on one practical chrome row", () => {
    expect(MARK_COMPACT).toMatch(PRINTABLE_ASCII)
    expect(MARK_COMPACT.length).toBeLessThanOrEqual(18)
    expect(MARK_COMPACT).toContain("## ##")
  })

  it("normalizes the welcome silhouette to one printable grid", () => {
    expect(MARK_WELCOME).toHaveLength(9)
    const width = MARK_WELCOME[0]?.length
    expect(width).toBeGreaterThanOrEqual(28)
    for (const line of MARK_WELCOME) {
      expect(line).toHaveLength(width ?? 0)
      expect(line).toMatch(PRINTABLE_ASCII)
    }
  })

  it("preserves the source container perspective and door gap", () => {
    expect(MARK_WELCOME.some((line) => line.includes("#####--##--#####"))).toBe(true)
    expect(MARK_WELCOME[0]?.trimStart()).toStartWith("/")
    expect(MARK_WELCOME.at(-1)?.trimStart()).toStartWith("\\")
  })
})

describe("paintMark", () => {
  it("wraps every line in the given color and resets after", () => {
    const painted = paintMark(MARK_WELCOME, colors.accent)
    expect(painted).toHaveLength(MARK_WELCOME.length)
    for (const [index, line] of painted.entries()) {
      expect(line.startsWith(colors.accent)).toBe(true)
      expect(line.endsWith("\x1b[0m")).toBe(true)
      expect(stripAnsi(line)).toBe(MARK_WELCOME[index])
    }
  })
})

import { describe, expect, it } from "bun:test"
import { CLI_THEMES } from "../themes"
import { stripAnsi, symbols } from "../theme"
import {
  themeListingLines,
  themeSwitchedLine,
  themeUnknownLines,
} from "./theme-command"

describe("themeListingLines", () => {
  it("lists every palette with the current choice marked", () => {
    const lines = themeListingLines("dockside-dark").map(stripAnsi)
    expect(lines[0]).toBe("themes")
    expect(lines).toHaveLength(1 + CLI_THEMES.length)
    for (const theme of CLI_THEMES) {
      expect(lines.some((line) => line.includes(theme.id))).toBe(true)
    }
    const dockside = lines.find((line) => line.includes("dockside"))
    expect(dockside).toContain("dark *")
    expect(dockside).toContain("light")
    const graphite = lines.find((line) => line.includes("graphite"))
    expect(graphite).not.toContain("*")
  })

  it("marks the light reading when that is current", () => {
    const line = themeListingLines("graphite-light")
      .map(stripAnsi)
      .find((row) => row.includes("graphite"))
    expect(line).toContain("light *")
    expect(line).not.toContain("dark *")
  })
})

describe("themeSwitchedLine", () => {
  it("is a dim event notice pointing at the new choice", () => {
    expect(stripAnsi(themeSwitchedLine("dockside-dark"))).toBe(
      `  ${symbols.event} theme → dockside-dark`
    )
  })
})

describe("themeUnknownLines", () => {
  it("leads with the error then reprints the listing", () => {
    const lines = themeUnknownLines("not-a-theme", "dichromat-dark").map(
      stripAnsi
    )
    expect(lines[0]).toContain("unknown theme: not-a-theme")
    expect(lines[1]).toBe("themes")
    expect(lines.some((line) => line.includes("dichromat"))).toBe(true)
  })
})

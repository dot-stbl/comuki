import { describe, expect, it } from "bun:test"
import { stripAnsi } from "../theme"
import {
  DEFAULT_KEYBINDINGS,
  keybindingsListingLines,
  matchesBinding,
  resolveKeybindings,
} from "./keybindings"

describe("resolveKeybindings", () => {
  it("returns defaults when the overlay is missing or not an object", () => {
    expect(resolveKeybindings(undefined)).toEqual(DEFAULT_KEYBINDINGS)
    expect(resolveKeybindings(null)).toEqual(DEFAULT_KEYBINDINGS)
    expect(resolveKeybindings("ctrl+n")).toEqual(DEFAULT_KEYBINDINGS)
  })

  it("overlays known actions and ignores unknown keys", () => {
    expect(
      resolveKeybindings({
        copy: "ctrl+k",
        search: "ctrl+/",
        mystery: "ctrl+z",
        new: 12,
      })
    ).toEqual({
      ...DEFAULT_KEYBINDINGS,
      copy: "ctrl+k",
    })
  })

  it("normalises case and trims; escape aliases to esc", () => {
    expect(
      resolveKeybindings({
        copy: "  CTRL+Y  ",
        overview: "Escape",
      })
    ).toEqual({
      ...DEFAULT_KEYBINDINGS,
      copy: "ctrl+y",
      overview: "esc",
    })
  })

  it("drops malformed chords so the default stays", () => {
    expect(
      resolveKeybindings({
        copy: "alt+y",
        close: "ctrl+",
        verbose: "ctrl+enter",
      })
    ).toEqual(DEFAULT_KEYBINDINGS)
  })
})

describe("matchesBinding", () => {
  it("matches ctrl+letter against the key flags", () => {
    expect(
      matchesBinding("ctrl+n", "n", { ctrl: true, escape: false })
    ).toBe(true)
    expect(
      matchesBinding("ctrl+n", "N", { ctrl: true, escape: false })
    ).toBe(true)
    expect(
      matchesBinding("ctrl+n", "n", { ctrl: false, escape: false })
    ).toBe(false)
    expect(
      matchesBinding("ctrl+n", "w", { ctrl: true, escape: false })
    ).toBe(false)
  })

  it("matches esc against the escape flag", () => {
    expect(matchesBinding("esc", "", { ctrl: false, escape: true })).toBe(true)
    expect(matchesBinding("esc", "", { ctrl: false, escape: false })).toBe(
      false
    )
  })
})

describe("keybindingsListingLines", () => {
  it("lists every default chord with its label", () => {
    const help = keybindingsListingLines(DEFAULT_KEYBINDINGS)
      .map(stripAnsi)
      .join("\n")
    expect(help).toContain("ctrl+n")
    expect(help).toContain("new session")
    expect(help).toContain("ctrl+w")
    expect(help).toContain("ctrl+o")
    expect(help).toContain("ctrl+y")
    expect(help).toContain("ctrl+f")
    expect(help).toContain("esc")
    expect(help).toContain("session overview")
  })
})

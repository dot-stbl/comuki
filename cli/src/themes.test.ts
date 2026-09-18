/**
 * Theme registry tests: all seven dashboard palettes ported with the
 * full primitive set, every `<theme>-<mode>` choice parseable, and
 * `dichromat-dark` resolving byte-identical to the palette the CLI
 * shipped before themes existed.
 */
import { describe, expect, it } from "bun:test"
import {
  CLI_THEMES,
  DEFAULT_THEME_CHOICE,
  THEME_CHOICE_IDS,
  findCliThemeChoice,
  isThemeChoice,
} from "./themes"
import { colors, currentThemeId, palette, resolveTheme } from "./theme"

const HEX = /^#[0-9a-f]{6}$/i

const PRIMITIVE_KEYS = [
  "text",
  "muted",
  "faint",
  "rule",
  "running",
  "success",
  "failed",
  "waiting",
] as const

describe("cli theme registry", () => {
  it("ports all seven dashboard themes in picker order", () => {
    expect(CLI_THEMES.map((theme) => theme.id)).toEqual([
      "dichromat",
      "graphite",
      "dockside",
      "blueprint",
      "bureau",
      "aperture",
      "dispatcher",
    ])
  })

  it("every theme × mode carries the full eight-hex primitive set", () => {
    expect(CLI_THEMES).toHaveLength(7)
    for (const theme of CLI_THEMES) {
      for (const mode of ["dark", "light"] as const) {
        for (const key of PRIMITIVE_KEYS) {
          expect(
            HEX.test(theme[mode][key]),
            `${theme.id}-${mode}.${key} = ${theme[mode][key]}`
          ).toBe(true)
        }
      }
    }
  })

  it("offers 14 choices and defaults to dichromat-dark", () => {
    expect(THEME_CHOICE_IDS).toHaveLength(14)
    expect(THEME_CHOICE_IDS[0]).toBe("dichromat-dark")
    expect(THEME_CHOICE_IDS).toContain("dispatcher-light")
    expect(DEFAULT_THEME_CHOICE).toBe("dichromat-dark")
  })

  it("parses choice ids and rejects the rest", () => {
    expect(findCliThemeChoice("graphite-light")?.theme.id).toBe("graphite")
    expect(findCliThemeChoice("graphite-light")?.mode).toBe("light")
    expect(findCliThemeChoice("dichromat-dark")?.theme.id).toBe("dichromat")
    expect(findCliThemeChoice("graphite")).toBeUndefined()
    expect(findCliThemeChoice("graphite-dusk")).toBeUndefined()
    expect(findCliThemeChoice("nope-dark")).toBeUndefined()
    expect(isThemeChoice("bureau-dark")).toBe(true)
    expect(isThemeChoice("bureau")).toBe(false)
    expect(isThemeChoice(42)).toBe(false)
  })
})

describe("resolveTheme", () => {
  it("dichromat-dark is byte-identical to the palette that shipped", () => {
    const tokens = resolveTheme("dichromat-dark")
    expect(tokens.id).toBe("dichromat-dark")
    expect(tokens.hex.accent).toBe("#8787f3") // running, not the bone chrome accent
    expect(tokens.ansi.text).toBe("\x1b[38;2;232;232;238m") // #e8e8ee
    expect(tokens.ansi.dim).toBe("\x1b[38;2;184;184;189m") // #b8b8bd
    expect(tokens.ansi.faint).toBe("\x1b[38;2;138;138;143m") // #8a8a8f
    expect(tokens.ansi.accent).toBe("\x1b[38;2;135;135;243m") // #8787f3
    expect(tokens.ansi.ok).toBe("\x1b[38;2;215;215;255m") // #d7d7ff
    expect(tokens.ansi.error).toBe("\x1b[38;2;210;210;40m") // #d2d228
    expect(tokens.ansi.waiting).toBe("\x1b[38;2;180;180;66m") // #b4b442
    expect(tokens.ansi.rule).toBe("\x1b[38;2;55;55;60m") // #37373c
  })

  it("defaults to dichromat-dark for undefined and unknown choices", () => {
    expect(resolveTheme().id).toBe("dichromat-dark")
    expect(resolveTheme("not-a-theme").id).toBe("dichromat-dark")
  })

  it("resolves light readings and maps status hexes onto cli tokens", () => {
    const tokens = resolveTheme("dockside-light")
    expect(tokens.mode).toBe("light")
    expect(tokens.hex.text).toBe("#25211c")
    expect(tokens.hex.dim).toBe("#4a443e") // muted
    expect(tokens.hex.accent).toBe("#2e549c") // running
    expect(tokens.hex.ok).toBe("#3d6b33") // success
    expect(tokens.hex.error).toBe("#921e05") // failed
    expect(tokens.hex.waiting).toBe("#512f00")
    expect(tokens.ansi.accent).toBe("\x1b[38;2;46;84;156m")
  })

  it("installs the resolved tokens into colors and palette", () => {
    resolveTheme("graphite-light")
    expect(currentThemeId()).toBe("graphite-light")
    expect(colors.accent).toBe("\x1b[38;2;0;87;158m") // #00579e
    expect(colors.dim).toBe("\x1b[38;2;66;70;74m") // #42464a
    expect(colors.muted).toBe(colors.dim)
    expect(palette.brand).toBe("#00579e")
    expect(palette.error).toBe("#951720")
    resolveTheme("dichromat-dark") // restore module state for other files
    expect(currentThemeId()).toBe("dichromat-dark")
  })

  it("round-trips every registry choice", () => {
    for (const id of THEME_CHOICE_IDS) {
      expect(resolveTheme(id).id).toBe(id)
    }
    resolveTheme("dichromat-dark")
  })
})

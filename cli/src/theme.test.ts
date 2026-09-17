/**
 * Pure token tests: the message-identity marks (role → glyph + color
 * token) and the spinner frame stepping that drives TypingIndicator.
 */
import { describe, expect, it } from "bun:test"
import {
  colors,
  gutter,
  messageMark,
  nextSpinnerFrame,
  palette,
  symbols,
} from "./theme"

describe("messageMark", () => {
  it("marks user rows with bare bold deck-text — no glyph, no label", () => {
    const mark = messageMark("user")
    expect(mark.glyph).toBe("")
    expect(mark.label).toBe("")
    expect(mark.textColor).toBe(colors.bright + colors.text)
  })

  it("marks assistant rows with the brand glyph, accent and dim label", () => {
    const mark = messageMark("assistant")
    expect(mark.glyph).toBe(symbols.brandMark)
    expect(mark.glyphColor).toBe(colors.accent)
    expect(mark.label).toBe("comuki")
    expect(mark.labelColor).toBe(colors.dim)
  })

  it("marks system and tool rows with the quiet muted bullet", () => {
    for (const role of ["system", "tool", "anything-else"]) {
      const mark = messageMark(role)
      expect(mark.glyph).toBe(symbols.bullet)
      expect(mark.glyphColor).toBe(colors.muted)
      expect(mark.textColor).toBe(colors.muted)
      expect(mark.label).toBe("")
    }
  })

  it("keeps the periwinkle brand hex for ink color props", () => {
    expect(palette.brand).toBe("#8787f3")
  })

  it("speaks the dichromat deck in truecolor escapes", () => {
    expect(colors.text).toBe("\x1b[38;2;232;232;238m") // #e8e8ee
    expect(colors.dim).toBe("\x1b[38;2;184;184;189m") // #b8b8bd text-muted
    expect(colors.faint).toBe("\x1b[38;2;138;138;143m") // #8a8a8f text-faint
    expect(colors.accent).toBe("\x1b[38;2;135;135;243m") // #8787f3 running
    expect(colors.ok).toBe("\x1b[38;2;215;215;255m") // #d7d7ff success
    expect(colors.error).toBe("\x1b[38;2;210;210;40m") // #d2d228 failed
    expect(colors.waiting).toBe("\x1b[38;2;180;180;66m") // #b4b442 waiting
    expect(colors.rule).toBe("\x1b[38;2;55;55;60m") // #37373c rule
  })

  it("dim and muted are one quiet tier — the deck has a single text-muted", () => {
    expect(colors.muted).toBe(colors.dim)
  })

  it("carries no green or red anywhere — success is lavender, failed is yellow", () => {
    expect("green" in colors).toBe(false)
    expect("red" in colors).toBe(false)
  })

  it("collapses every event line onto the single ⏺ bullet", () => {
    expect(symbols.event).toBe("⏺")
  })
})

describe("nextSpinnerFrame", () => {
  it("walks the braille cycle in order and wraps to zero", () => {
    const seen: string[] = []
    let frame = 0
    seen.push(symbols.spinnerFrames[frame]!)
    for (let step = 0; step < symbols.spinnerFrames.length; step += 1) {
      frame = nextSpinnerFrame(frame)
      seen.push(symbols.spinnerFrames[frame]!)
    }
    expect(seen).toEqual([
      "⠋",
      "⠙",
      "⠹",
      "⠸",
      "⠼",
      "⠴",
      "⠦",
      "⠧",
      "⠇",
      "⠏",
      "⠋",
    ])
  })

  it("supports a custom frame count", () => {
    expect(nextSpinnerFrame(0, 3)).toBe(1)
    expect(nextSpinnerFrame(2, 3)).toBe(0)
  })
})

describe("gutter", () => {
  it("is exactly one space", () => {
    expect(gutter).toBe(" ")
  })
})

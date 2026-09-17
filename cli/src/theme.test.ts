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
  it("marks user rows with the dim prompt glyph and bright text", () => {
    const mark = messageMark("user")
    expect(mark.glyph).toBe(symbols.prompt)
    expect(mark.glyphColor).toBe(colors.dim)
    expect(mark.textColor).toBe(colors.bright)
    expect(mark.label).toBe("")
  })

  it("marks assistant rows with the brand glyph, accent color and dim label", () => {
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

  it("keeps the brand hex in the palette for ink color props", () => {
    expect(palette.brand).toBe("#8787f3")
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

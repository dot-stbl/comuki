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
  it("marks user rows with bare bright text — no glyph, no label", () => {
    const mark = messageMark("user")
    expect(mark.glyph).toBe("")
    expect(mark.label).toBe("")
    expect(mark.textColor).toBe(colors.bright)
  })

  it("marks assistant rows with the brand glyph, warm accent and dim label", () => {
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

  it("keeps the warm terracotta brand hex for ink color props", () => {
    expect(palette.brand).toBe("#d08770")
  })

  it("uses the warm 256-color codes — terracotta accent, warm green/red", () => {
    expect(colors.accent).toBe("\x1b[38;5;173m")
    expect(colors.green).toBe("\x1b[38;5;114m")
    expect(colors.red).toBe("\x1b[38;5;167m")
    expect(colors.muted).toBe("\x1b[38;5;245m")
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

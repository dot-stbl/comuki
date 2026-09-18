import { describe, expect, test } from "bun:test"
import { render } from "ink-testing-library"
import React from "react"
import stringWidth from "string-width"
import { stripAnsi } from "../theme"
import {
  SurfaceLine,
  padSegmentsToWidth,
  type SurfaceTextSegment,
} from "./SurfaceLine"

describe("padSegmentsToWidth", () => {
  test.each([
    ["ASCII", 5],
    ["Привет", 6],
    ["漢字", 4],
    ["e\u0301", 1],
    ["🧑‍💻", 2],
  ])("fills Unicode text %s to exact visible width", (text, contentWidth) => {
    const segments = padSegmentsToWidth([{ text }], contentWidth + 3)
    const visible = segments.map((segment) => segment.text).join("")

    expect(stringWidth(visible)).toBe(contentWidth + 3)
    expect(visible.endsWith("   ")).toBe(true)
  })

  test("clips at grapheme boundaries", () => {
    const visible = padSegmentsToWidth([{ text: "漢字abc" }], 5)
      .map((segment) => segment.text)
      .join("")

    expect(visible).toBe("漢字a")
    expect(stringWidth(visible)).toBe(5)
  })
})

describe("SurfaceLine", () => {
  test("fills every visible cell and reapplies background to nested segments", () => {
    const segments: readonly SurfaceTextSegment[] = [
      { text: "[ok]", color: "#d7d7ff", bold: true },
      { text: " ready", dim: true },
    ]
    const padded = padSegmentsToWidth(segments, 18)
    const element = SurfaceLine({
      width: 18,
      background: "#313136",
      segments,
    })
    const children = React.Children.toArray(element.props.children) as React.ReactElement[]
    const { lastFrame, unmount } = render(
      <SurfaceLine width={18} background="#313136" segments={segments} />
    )
    const frame = lastFrame() ?? ""

    expect(stringWidth(padded.map((segment) => segment.text).join(""))).toBe(18)
    expect(children).toHaveLength(3)
    expect(children.every((child) => child.props.backgroundColor === "#313136")).toBe(true)
    expect(stripAnsi(frame)).toBe("[ok] ready")
    unmount()
  })
})

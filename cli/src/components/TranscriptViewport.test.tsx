/**
 * TranscriptViewport smoke tests through ink-testing-library (fixed
 * 100-col stdout): the bottom window renders, scrolling up surfaces
 * older lines, the `↓ new messages` indicator reserves the last row
 * and only appears for fresh output while suspended, and the ctrl+f
 * highlight prop wraps matches in inverse video.
 */
import { describe, expect, test } from "bun:test"
import React from "react"
import { render } from "ink-testing-library"
import {
  TranscriptViewport,
} from "./TranscriptViewport"
import { NEW_MESSAGES_INDICATOR } from "../lib/viewport"

const LINES = Array.from({ length: 30 }, (_, index) => `line-${index}`)

describe("TranscriptViewport", () => {
  test("follow mode shows the last `height` lines only", () => {
    const { lastFrame, unmount } = render(
      <TranscriptViewport lines={LINES} height={5} offset={0} newBelow={false} />
    )
    const frame = lastFrame() ?? ""
    expect(frame).toContain("line-29")
    expect(frame).toContain("line-25")
    expect(frame).not.toContain("line-24")
    expect(frame).not.toContain(NEW_MESSAGES_INDICATOR)
    unmount()
  })

  test("scrolled-up offset shows the older window", () => {
    const { lastFrame, unmount } = render(
      <TranscriptViewport lines={LINES} height={5} offset={10} newBelow={false} />
    )
    const frame = lastFrame() ?? ""
    expect(frame).toContain("line-15")
    expect(frame).toContain("line-19")
    expect(frame).not.toContain("line-20")
    expect(frame).not.toContain("line-29")
    unmount()
  })

  test("indicator shows on suspended follow with new output", () => {
    const { lastFrame, unmount } = render(
      <TranscriptViewport lines={LINES} height={5} offset={3} newBelow={true} />
    )
    const frame = lastFrame() ?? ""
    expect(frame).toContain(NEW_MESSAGES_INDICATOR)
    // The indicator reserves a row: only 4 transcript lines show.
    expect(frame).toContain("line-26")
    expect(frame).not.toContain("line-27")
    unmount()
  })

  test("no indicator while merely scrolled (nothing new below)", () => {
    const { lastFrame, unmount } = render(
      <TranscriptViewport lines={LINES} height={5} offset={8} newBelow={false} />
    )
    expect(lastFrame() ?? "").not.toContain(NEW_MESSAGES_INDICATOR)
    unmount()
  })

  test("no indicator while following even if newBelow lagged", () => {
    const { lastFrame, unmount } = render(
      <TranscriptViewport lines={LINES} height={5} offset={0} newBelow={true} />
    )
    expect(lastFrame() ?? "").not.toContain(NEW_MESSAGES_INDICATOR)
    unmount()
  })

  test("empty transcript renders nothing without crashing", () => {
    const { lastFrame, unmount } = render(
      <TranscriptViewport lines={[]} height={5} offset={0} newBelow={false} />
    )
    expect((lastFrame() ?? "").trim().length).toBe(0)
    unmount()
  })

  test("the expand hint pins as the first row and reserves it", () => {
    const hint = "  * press ctrl+o to expand thinking"
    const { lastFrame, unmount } = render(
      <TranscriptViewport
        lines={LINES}
        height={5}
        offset={0}
        newBelow={false}
        hint={hint}
      />
    )
    const frame = lastFrame() ?? ""
    expect(frame.split("\n")[0]).toBe(hint)
    // The hint reserves a row: only 4 transcript lines show.
    expect(frame).toContain("line-29")
    expect(frame).toContain("line-26")
    expect(frame).not.toContain("line-25")
    unmount()
  })

  test("hint and indicator can reserve rows together", () => {
    const hint = "  * press ctrl+o to expand thinking"
    const { lastFrame, unmount } = render(
      <TranscriptViewport
        lines={LINES}
        height={6}
        offset={3}
        newBelow={true}
        hint={hint}
      />
    )
    const frame = lastFrame() ?? ""
    const rows = frame.split("\n")
    expect(rows[0]).toBe(hint)
    expect(rows[rows.length - 1]).toContain(NEW_MESSAGES_INDICATOR)
    // 6 rows total − hint − indicator = 4 transcript lines (23–26:
    // the window ends at 30 − offset 3, minus the 4-line budget).
    expect(frame).toContain("line-26")
    expect(frame).toContain("line-23")
    expect(frame).not.toContain("line-27")
    expect(frame).not.toContain("line-22")
    expect(frame).not.toContain("line-29")
    unmount()
  })

  test("no hint row when the hint prop is absent", () => {
    const { lastFrame, unmount } = render(
      <TranscriptViewport lines={LINES} height={5} offset={0} newBelow={false} />
    )
    expect(lastFrame() ?? "").not.toContain("ctrl+o")
    unmount()
  })
})

describe("TranscriptViewport — ctrl+f highlight", () => {
  test("matching lines render with inverse-video wraps", () => {
    const { lastFrame, unmount } = render(
      <TranscriptViewport
        lines={LINES}
        height={5}
        offset={0}
        newBelow={false}
        highlight={{ query: "line-2", activeLine: null }}
      />
    )
    const frame = lastFrame() ?? ""
    // The window shows lines 25–29; the "line-2" prefix of every row
    // wraps in inverse video, the row tail stays plain.
    expect(frame).toContain("\x1b[7mline-2\x1b[27m5")
    expect(frame).toContain("\x1b[7mline-2\x1b[27m9")
    // Five matching rows, none underlined (no active line).
    expect(frame.split("\x1b[7m").length - 1).toBe(5)
    expect(frame).not.toContain("\x1b[4m")
    unmount()
  })

  test("the active match line is the one that also underlines", () => {
    // Window shows lines 20–24 (offset 5): active line 22.
    const { lastFrame, unmount } = render(
      <TranscriptViewport
        lines={LINES}
        height={5}
        offset={5}
        newBelow={false}
        highlight={{ query: "line-2", activeLine: 22 }}
      />
    )
    const frame = lastFrame() ?? ""
    const rows = frame.split("\n")
    const active = rows.find((row) => row.includes("\x1b[4m"))
    expect(active).toBeDefined()
    // The underlined row is the active line, not a neighbouring match.
    expect(active).toContain("\x1b[7m\x1b[4mline-2\x1b[24m\x1b[27m2")
    // Other matching rows carry inverse but not the underline.
    const others = rows.filter(
      (row) => row.includes("\x1b[7m") && !row.includes("\x1b[4m")
    )
    expect(others.length).toBe(4)
    unmount()
  })

  test("a query with no visible matches changes nothing", () => {
    const { lastFrame, unmount } = render(
      <TranscriptViewport
        lines={LINES}
        height={5}
        offset={0}
        newBelow={false}
        highlight={{ query: "absent-needle", activeLine: null }}
      />
    )
    const frame = lastFrame() ?? ""
    expect(frame).not.toContain("\x1b[7m")
    expect(frame).toContain("line-29")
    unmount()
  })

  test("no highlight prop renders raw lines", () => {
    const { lastFrame, unmount } = render(
      <TranscriptViewport lines={LINES} height={5} offset={0} newBelow={false} />
    )
    expect(lastFrame() ?? "").not.toContain("\x1b[7m")
    unmount()
  })
})

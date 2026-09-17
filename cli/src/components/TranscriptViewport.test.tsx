/**
 * TranscriptViewport smoke tests through ink-testing-library (fixed
 * 100-col stdout): the bottom window renders, scrolling up surfaces
 * older lines, the `↓ new messages` indicator reserves the last row
 * and only appears for fresh output while suspended.
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
})

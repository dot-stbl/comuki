/**
 * useTranscriptScroll tests through a probe component rendered with
 * ink-testing-library: follow-bottom default, page/line scrolling,
 * the anchored-append behaviour (offset tracks growth, `newBelow`
 * lights), shrink → back to follow, resetKey (tab switch) → reset,
 * and clamping when the viewport outgrows the transcript.
 */
import { describe, expect, test } from "bun:test"
import React from "react"
import { Text } from "ink"
import { render } from "ink-testing-library"
import {
  useTranscriptScroll,
  type TranscriptScroll,
} from "./useTranscriptScroll"

interface ProbeProps {
  readonly lineCount: number
  readonly height: number
  readonly resetKey?: string
}

let handle: TranscriptScroll | undefined

function ScrollProbe({ lineCount, height, resetKey = "s1" }: ProbeProps) {
  const scroll = useTranscriptScroll(lineCount, height, resetKey)
  handle = scroll
  return (
    <Text>
      offset={scroll.offset} newBelow={String(scroll.newBelow)} scrolled=
      {String(scroll.scrolledUp)}
    </Text>
  )
}

function settle(ms = 30): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms))
}

async function renderProbe(
  lineCount: number,
  height: number
): Promise<ReturnType<typeof render>> {
  const instance = render(<ScrollProbe lineCount={lineCount} height={height} />)
  await settle()
  return instance
}

describe("useTranscriptScroll — follow mode", () => {
  test("starts at the bottom (offset 0, no indicator)", async () => {
    const { unmount } = await renderProbe(100, 10)
    expect(handle?.offset).toBe(0)
    expect(handle?.newBelow).toBe(false)
    expect(handle?.scrolledUp).toBe(false)
    unmount()
  })

  test("appending while following keeps offset 0 (auto-scroll)", async () => {
    const { rerender, unmount } = await renderProbe(100, 10)
    rerender(<ScrollProbe lineCount={115} height={10} />)
    await settle()
    expect(handle?.offset).toBe(0)
    expect(handle?.newBelow).toBe(false)
    unmount()
  })
})

describe("useTranscriptScroll — scrolling", () => {
  test("pageUp suspends follow by one viewport; clamped at the top", async () => {
    const { unmount } = await renderProbe(100, 10)
    handle?.pageUp()
    await settle()
    expect(handle?.offset).toBe(10)
    handle?.pageUp()
    await settle()
    expect(handle?.offset).toBe(20)
    // …to the top and no further.
    for (let page = 0; page < 12; page++) {
      handle?.pageUp()
    }
    await settle()
    expect(handle?.offset).toBe(90)
    expect(handle?.scrolledUp).toBe(true)
    unmount()
  })

  test("pageDown returns to the bottom and resumes follow", async () => {
    const { unmount } = await renderProbe(100, 10)
    handle?.pageUp()
    await settle()
    expect(handle?.offset).toBe(10)
    handle?.pageDown()
    await settle()
    expect(handle?.offset).toBe(0)
    expect(handle?.scrolledUp).toBe(false)
    unmount()
  })

  test("lineUp/lineDown scroll by one; lineDown stops at the bottom", async () => {
    const { unmount } = await renderProbe(100, 10)
    handle?.lineUp()
    await settle()
    expect(handle?.offset).toBe(1)
    handle?.lineUp()
    await settle()
    expect(handle?.offset).toBe(2)
    handle?.lineDown()
    await settle()
    expect(handle?.offset).toBe(1)
    handle?.lineDown()
    await settle()
    handle?.lineDown()
    await settle()
    expect(handle?.offset).toBe(0)
    unmount()
  })

  test("Home jumps to the top, End jumps to the bottom", async () => {
    const { unmount } = await renderProbe(100, 10)
    handle?.toTop()
    await settle()
    expect(handle?.offset).toBe(90)
    handle?.toBottom()
    await settle()
    expect(handle?.offset).toBe(0)
    expect(handle?.scrolledUp).toBe(false)
    unmount()
  })

  test("scrollToLine centers the searched line in the window", async () => {
    const { unmount } = await renderProbe(100, 10)
    handle?.scrollToLine(40)
    await settle()
    // height 10 → the window ends at 40 + 1 + 4 = 45, offset 100−45 = 55;
    // the window [35,45) has line 40 dead center.
    expect(handle?.offset).toBe(55)
    expect(handle?.scrolledUp).toBe(true)
    unmount()
  })

  test("scrollToLine near the top clamps to the first window", async () => {
    const { unmount } = await renderProbe(100, 10)
    handle?.scrollToLine(1)
    await settle()
    // Centering would end the window before `height` — clamped to the
    // first full window: offset = maxOffset = 90.
    expect(handle?.offset).toBe(90)
    unmount()
  })

  test("scrollToLine near the bottom lands on follow (offset 0)", async () => {
    const { unmount } = await renderProbe(100, 10)
    handle?.pageUp()
    await settle()
    handle?.scrollToLine(99)
    await settle()
    expect(handle?.offset).toBe(0)
    expect(handle?.newBelow).toBe(false)
    unmount()
  })

  test("scrollToLine on a short transcript is a no-op", async () => {
    const { unmount } = await renderProbe(5, 10)
    handle?.scrollToLine(2)
    await settle()
    expect(handle?.offset).toBe(0)
    unmount()
  })

  test("scrolling is a no-op on a short transcript (nothing to hide)", async () => {
    const { unmount } = await renderProbe(5, 10)
    handle?.pageUp()
    await settle()
    handle?.toTop()
    await settle()
    expect(handle?.offset).toBe(0)
    expect(handle?.scrolledUp).toBe(false)
    unmount()
  })
})

describe("useTranscriptScroll — suspended follow", () => {
  test("new output while scrolled up anchors the view and lights the indicator", async () => {
    const { rerender, unmount } = await renderProbe(100, 10)
    handle?.pageUp()
    await settle()
    expect(handle?.offset).toBe(10)
    // +5 lines while suspended: same content stays in view.
    rerender(<ScrollProbe lineCount={105} height={10} />)
    await settle()
    expect(handle?.offset).toBe(15)
    expect(handle?.newBelow).toBe(true)
    expect(handle?.scrolledUp).toBe(true)
    unmount()
  })

  test("indicator survives further scrolling up, clears at the bottom", async () => {
    const { rerender, unmount } = await renderProbe(100, 10)
    handle?.pageUp()
    await settle()
    rerender(<ScrollProbe lineCount={105} height={10} />)
    await settle()
    handle?.lineUp()
    await settle()
    expect(handle?.newBelow).toBe(true)
    handle?.toBottom()
    await settle()
    expect(handle?.newBelow).toBe(false)
    // New output while following: no indicator.
    rerender(<ScrollProbe lineCount={120} height={10} />)
    await settle()
    expect(handle?.offset).toBe(0)
    expect(handle?.newBelow).toBe(false)
    unmount()
  })

  test("scrolled to the very top, appended output keeps the top anchored", async () => {
    const { rerender, unmount } = await renderProbe(100, 10)
    handle?.toTop()
    await settle()
    expect(handle?.offset).toBe(90)
    rerender(<ScrollProbe lineCount={110} height={10} />)
    await settle()
    expect(handle?.offset).toBe(100)
    unmount()
  })

  test("shrinking transcript (/clear) returns to follow", async () => {
    const { rerender, unmount } = await renderProbe(100, 10)
    handle?.pageUp()
    await settle()
    rerender(<ScrollProbe lineCount={0} height={10} />)
    await settle()
    expect(handle?.offset).toBe(0)
    expect(handle?.newBelow).toBe(false)
    unmount()
  })

  test("resetKey change (tab switch) resets to follow without indicator", async () => {
    const { rerender, unmount } = await renderProbe(100, 10)
    handle?.pageUp()
    await settle()
    rerender(<ScrollProbe lineCount={40} height={10} resetKey="s2" />)
    await settle()
    expect(handle?.offset).toBe(0)
    expect(handle?.newBelow).toBe(false)
    unmount()
  })

  test("viewport growth clamps the exposed offset (no phantom scroll-back)", async () => {
    const { rerender, unmount } = await renderProbe(100, 10)
    handle?.pageUp()
    await settle()
    expect(handle?.offset).toBe(10)
    // Terminal grew: only 5 lines still hide below the window.
    rerender(<ScrollProbe lineCount={100} height={95} />)
    await settle()
    expect(handle?.offset).toBe(5)
    expect(handle?.scrolledUp).toBe(true)
    unmount()
  })
})

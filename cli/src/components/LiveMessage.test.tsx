/**
 * Streaming state transitions for the live assistant block: chunks
 * grow the rendered tail, the `_` cursor rides the write head,
 * an over-long tail clips to the last lines, and the empty pre-chunk
 * state renders nothing.
 */
import { describe, expect, test } from "bun:test"
import React from "react"
import { render } from "ink-testing-library"
import {
  LIVE_CURSOR,
  LIVE_MAX_LINES,
  LiveMessage,
} from "./LiveMessage"
import { stripAnsi } from "../theme"

describe("LiveMessage — streaming transitions", () => {
  test("renders nothing before the first chunk arrives", () => {
    const { lastFrame, unmount } = render(
      <LiveMessage liveText="" width={60} />
    )
    expect(lastFrame()).not.toContain(LIVE_CURSOR)
    unmount()
  })

  test("grows as chunks arrive, cursor on the write head", () => {
    const { lastFrame, rerender, unmount } = render(
      <LiveMessage liveText="" width={60} />
    )

    rerender(<LiveMessage liveText="План ре" width={60} />)
    const first = lastFrame() ?? ""
    expect(stripAnsi(first)).toContain("План ре")
    expect(first).toContain(LIVE_CURSOR)

    rerender(<LiveMessage liveText={"План рефакторинга:\n- шаг один"} width={60} />)
    const second = lastFrame() ?? ""
    expect(stripAnsi(second)).toContain("План рефакторинга:")
    expect(stripAnsi(second)).toContain(". шаг один")
    expect(second).toContain(LIVE_CURSOR)
    // the cursor moved to the new write head — it is on the last line
    const lines = second.split("\n")
    expect(lines[lines.length - 1]).toContain(LIVE_CURSOR)
    unmount()
  })

  test("renders partial markdown mid-stream (unterminated fence)", () => {
    const { lastFrame, rerender, unmount } = render(
      <LiveMessage liveText="" width={60} />
    )
    rerender(
      <LiveMessage liveText={"Вот код:\n```ts\nconst partial ="} width={60} />
    )
    const frame = stripAnsi(lastFrame() ?? "")
    expect(frame).toContain("│ const partial =")
    expect(lastFrame()).toContain(LIVE_CURSOR)
    unmount()
  })

  test("clips a long tail to the last lines with a leader", () => {
    const text = Array.from(
      { length: 40 },
      (_, index) => `строка номер ${index}`
    ).join("\n\n")
    const { lastFrame, unmount } = render(
      <LiveMessage liveText={text} width={60} />
    )
    const frame = lastFrame() ?? ""
    const lines = frame.split("\n").filter((line) => line.trim().length > 0)
    // leader + maxLines content lines (+ wrapped overflow margin of zero here)
    expect(lines.length).toBeLessThanOrEqual(LIVE_MAX_LINES + 1)
    expect(frame).toContain("…")
    expect(stripAnsi(frame)).toContain("строка номер 39")
    // the cursor survives clipping — still on the final visible line
    expect(lines[lines.length - 1]).toContain(LIVE_CURSOR)
    unmount()
  })
})

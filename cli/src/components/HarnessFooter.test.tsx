import { describe, expect, test } from "bun:test"
import { render } from "ink-testing-library"
import React from "react"
import stringWidth from "string-width"
import { stripAnsi } from "../theme"
import { HarnessFooter } from "./HarnessFooter"
import { footerSegments } from "./HarnessFooter"
import { padSegmentsToWidth } from "./SurfaceLine"

describe("HarnessFooter", () => {
  test("renders standard contextual metadata on one filled row", () => {
    const { lastFrame, unmount } = render(
      <HarnessFooter
        width={100}
        mode="wide"
        model="sonnet"
        project="comuki"
        workers={3}
        context="42%"
        queue={2}
        busy="thinking"
      />
    )
    const frame = stripAnsi(lastFrame() ?? "")

    expect(frame).toContain("mode wide / model sonnet / project comuki")
    expect(frame).toContain("workers 3 / context 42% / queue 2 / thinking")
    expect(stringWidth(padSegmentsToWidth(footerSegments({
      width: 100,
      mode: "wide",
      model: "sonnet",
      project: "comuki",
      workers: 3,
      context: "42%",
      queue: 2,
      busy: "thinking",
    }), 100).map((segment) => segment.text).join(""))).toBe(100)
    unmount()
  })

  test("compacts labels at narrow widths without a permanent tutorial", () => {
    const { lastFrame, unmount } = render(
      <HarnessFooter
        width={48}
        mode="compact"
        model="sonnet"
        project="comuki"
        workers={1}
        queue={0}
      />
    )
    const frame = stripAnsi(lastFrame() ?? "")

    expect(frame).toContain("compact / sonnet / comuki / 1w")
    expect(frame).not.toContain("ctrl+")
    expect(stringWidth(padSegmentsToWidth(footerSegments({
      width: 48,
      mode: "compact",
      model: "sonnet",
      project: "comuki",
      workers: 1,
      queue: 0,
    }), 48).map((segment) => segment.text).join(""))).toBe(48)
    unmount()
  })
})

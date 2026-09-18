import { describe, expect, test } from "bun:test"
import { render } from "ink-testing-library"
import { Text } from "ink"
import React from "react"
import { stripAnsi } from "../theme"
import { OverlaySheet } from "./OverlaySheet"
import { TopBar } from "./TopBar"
import { TranscriptViewport } from "./TranscriptViewport"

describe("TopBar responsive composition", () => {
  test("keeps compact chrome inside a narrow terminal", () => {
    const { lastFrame, unmount } = render(
      <TopBar
        width={58}
        mode="compact"
        session="repair streamed approvals"
        identity="operator@example.net"
        project="comuki"
        profile="implement"
        connection="live"
        serverUrl="https://api.example.net"
        latencyMs={42}
      />
    )
    const lines = stripAnsi(lastFrame() ?? "").split("\n")

    expect(lines).toHaveLength(2)
    expect(lines.every((line) => line.length <= 58)).toBe(true)
    expect(lines[0]).toContain("comuki")
    expect(lines[0]).toContain("[live] 42ms")
    unmount()
  })
})

describe("OverlaySheet family", () => {
  test("uses one title and key-hint placement", () => {
    const { lastFrame, unmount } = render(
      <OverlaySheet
        title="session overview"
        hint="esc close / enter select"
        width={42}
        height={7}
      >
        <Text>body</Text>
      </OverlaySheet>
    )
    const frame = stripAnsi(lastFrame() ?? "")

    expect(frame.split("\n")[0]).toContain("session overview")
    expect(frame).toContain("body")
    expect(frame.split("\n").at(-1)).toContain("esc close / enter select")
    unmount()
  })
})

describe("active streaming card", () => {
  test("renders the one-line pulse on rail and the live tail on lane", () => {
    const lines = ["[|] thinking / ctrl+c interrupts", " streamed reply_"]
    const { lastFrame, unmount } = render(
      <TranscriptViewport
        lines={lines}
        height={4}
        offset={0}
        newBelow={false}
        width={50}
      />
    )
    const frame = stripAnsi(lastFrame() ?? "")

    expect(frame).toContain("[|] thinking / ctrl+c interrupts")
    expect(frame).toContain("streamed reply_")
    unmount()
  })
})

/**
 * The first-run hint: one dim `comuki setup` pointer under the hints
 * row while `firstRun` is on, never otherwise. Props are explicit here
 * so the test never depends on the developer's real config file.
 */
import { describe, expect, test } from "bun:test"
import React from "react"
import { render } from "ink-testing-library"
import { Welcome } from "./Welcome"

function frameWith(props: Parameters<typeof Welcome>[0]): string {
  const { lastFrame, unmount } = render(<Welcome {...props} />)
  const frame = lastFrame() ?? ""
  unmount()
  return frame
}

describe("Welcome — first-run hint", () => {
  test("shows the setup pointer on first run", () => {
    const frame = frameWith({ firstRun: true })
    expect(frame).toContain("first run? try: comuki setup")
    expect(frame).toContain("agent orchestration platform")
  })

  test("hides the setup pointer once config exists", () => {
    const frame = frameWith({ firstRun: false })
    expect(frame).not.toContain("first run?")
  })

  test("stats row renders beside the hint without overlap", () => {
    const frame = frameWith({ firstRun: true, stats: { workers: 3, memory: 5 } })
    expect(frame).toContain("workers 3 / knowledge 5")
    expect(frame).toContain("first run? try: comuki setup")
  })
})

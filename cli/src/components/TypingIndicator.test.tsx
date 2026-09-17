/**
 * TypingIndicator: the first braille frame renders with the dim label
 * on the transcript gutter, frames advance on the injected fake clock
 * (deterministic — `bun:test` has no fake timers), and the interval is
 * cleared on unmount.
 */
import { describe, expect, test } from "bun:test"
import React from "react"
import { render } from "ink-testing-library"
import { TypingIndicator } from "./TypingIndicator"
import { symbols } from "../theme"
import type { IntervalClock } from "../hooks/useBlink"

/** Deterministic clock: records registrations, fires on advance(). */
class FakeClock implements IntervalClock {
  private nextHandle = 0
  private readonly timers = new Map<
    number,
    { handler: () => void; interval: number; elapsed: number }
  >()
  readonly cleared: number[] = []

  setInterval(handler: () => void, ms: number): number {
    const handle = ++this.nextHandle
    this.timers.set(handle, { handler, interval: ms, elapsed: 0 })
    return handle
  }

  clearInterval(handle: unknown): void {
    if (typeof handle === "number") {
      this.cleared.push(handle)
      this.timers.delete(handle)
    }
  }

  /** Advances every live timer, firing handlers per elapsed interval. */
  advance(ms: number): void {
    for (const timer of this.timers.values()) {
      timer.elapsed += ms
      while (timer.elapsed >= timer.interval) {
        timer.elapsed -= timer.interval
        timer.handler()
      }
    }
  }

  get liveCount(): number {
    return this.timers.size
  }
}

/** Lets React flush the state update a clock tick schedules. */
function settle(ms = 60): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms))
}

describe("TypingIndicator", () => {
  test("renders the first frame, dim label and live tail", () => {
    const { lastFrame, unmount } = render(
      <TypingIndicator liveText={"drafting\nthe plan"} />
    )
    const frame = lastFrame() ?? ""
    expect(frame).toContain(symbols.spinnerFrames[0])
    expect(frame).toContain("comuki is thinking…")
    expect(frame).toContain("drafting the plan")
    unmount()
  })

  test("walks the braille frames on the clock", async () => {
    const clock = new FakeClock()
    const { lastFrame, unmount } = render(
      <TypingIndicator clock={clock} frameIntervalMs={80} />
    )
    // Let the effect subscribe before driving the clock.
    await settle()
    expect(lastFrame()).toContain("⠋")

    clock.advance(80)
    await settle()
    expect(lastFrame()).toContain("⠙")

    clock.advance(80)
    await settle()
    expect(lastFrame()).toContain("⠹")

    // A full cycle minus one lands back on the last frame.
    for (let tick = 0; tick < 7; tick += 1) {
      clock.advance(80)
    }
    await settle()
    expect(lastFrame()).toContain("⠏")
    unmount()
  })

  test("clears its interval on unmount", async () => {
    const clock = new FakeClock()
    const { unmount } = render(
      <TypingIndicator clock={clock} frameIntervalMs={80} />
    )
    await settle()
    expect(clock.liveCount).toBe(1)

    unmount()
    await settle()
    expect(clock.liveCount).toBe(0)
    expect(clock.cleared.length).toBe(1)
  })
})

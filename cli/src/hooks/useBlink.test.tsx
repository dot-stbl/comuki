/**
 * `useBlink` behavior with a hand-rolled fake clock — `bun:test` ships
 * no fake-timer API, so the hook takes an injectable `IntervalClock`
 * and these tests advance time synchronously: the toggle cadence, the
 * steady-visible rest state, and the timer cleanup on deactivate and
 * unmount.
 */
import { describe, expect, test } from "bun:test"
import React from "react"
import { Text } from "ink"
import { render } from "ink-testing-library"
import {
  useBlink,
  type IntervalClock,
} from "./useBlink"

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

interface ProbeProps {
  readonly active: boolean
  readonly clock: IntervalClock
  readonly intervalMs?: number
}

function BlinkProbe({ active, clock, intervalMs }: ProbeProps) {
  const visible = useBlink(active, intervalMs, clock)
  return <Text>{visible ? "on" : "off"}</Text>
}

/** Lets React flush the state update a clock tick schedules. */
function settle(ms = 60): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms))
}

describe("useBlink", () => {
  test("toggles on each interval while active", async () => {
    const clock = new FakeClock()
    const { lastFrame, unmount } = render(
      <BlinkProbe active={true} clock={clock} intervalMs={500} />
    )
    await settle()
    expect(lastFrame()).toContain("on")

    clock.advance(500)
    await settle()
    expect(lastFrame()).toContain("off")

    clock.advance(500)
    await settle()
    expect(lastFrame()).toContain("on")
    unmount()
  })

  test("is steady-visible and timer-free while inactive", async () => {
    const clock = new FakeClock()
    const { lastFrame, unmount, rerender } = render(
      <BlinkProbe active={false} clock={clock} intervalMs={500} />
    )
    await settle()
    expect(lastFrame()).toContain("on")
    expect(clock.liveCount).toBe(0)

    clock.advance(5_000)
    await settle()
    expect(lastFrame()).toContain("on")

    rerender(<BlinkProbe active={false} clock={clock} intervalMs={500} />)
    await settle()
    expect(clock.liveCount).toBe(0)
    unmount()
  })

  test("clears the timer when active flips to false", async () => {
    const clock = new FakeClock()
    const { rerender, unmount } = render(
      <BlinkProbe active={true} clock={clock} intervalMs={500} />
    )
    await settle()
    expect(clock.liveCount).toBe(1)

    rerender(<BlinkProbe active={false} clock={clock} intervalMs={500} />)
    await settle()
    expect(clock.liveCount).toBe(0)
    expect(clock.cleared.length).toBe(1)
    unmount()
  })

  test("clears the timer on unmount", async () => {
    const clock = new FakeClock()
    const { unmount } = render(
      <BlinkProbe active={true} clock={clock} intervalMs={500} />
    )
    await settle()
    expect(clock.liveCount).toBe(1)

    unmount()
    await settle()
    expect(clock.liveCount).toBe(0)
    expect(clock.cleared.length).toBe(1)
  })

  test("restarting from hidden resets to the visible phase", async () => {
    const clock = new FakeClock()
    const { lastFrame, rerender, unmount } = render(
      <BlinkProbe active={true} clock={clock} intervalMs={500} />
    )
    await settle()
    clock.advance(500)
    await settle()
    expect(lastFrame()).toContain("off")

    // Deactivate mid-off-phase, reactivate: must come back visible.
    rerender(<BlinkProbe active={false} clock={clock} intervalMs={500} />)
    await settle()
    rerender(<BlinkProbe active={true} clock={clock} intervalMs={500} />)
    await settle()
    expect(lastFrame()).toContain("on")
    unmount()
  })
})

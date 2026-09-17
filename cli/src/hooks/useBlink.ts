/**
 * A text-only blink: true/false toggling on an interval while `active`,
 * steady true while not. Built for the streaming write-head cursor
 * (`▌`) — LiveMessage can adopt it without owning a timer.
 *
 * The clock is injectable because `bun:test` has no fake-timer API:
 * tests pass a synchronous `IntervalClock` and advance it by hand, so
 * the toggle sequence and the cleanup are asserted deterministically.
 */
import { useEffect, useState } from "react"

export interface IntervalClock {
  setInterval(handler: () => void, ms: number): unknown
  clearInterval(handle: unknown): void
}

export const BLINK_INTERVAL_MS = 500

export const systemIntervalClock: IntervalClock = {
  setInterval: (handler, ms) => setInterval(handler, ms),
  clearInterval: (handle) => clearInterval(handle as Parameters<typeof clearInterval>[0]),
}

/**
 * While `active`, toggles a boolean every `intervalMs` (starting from
 * true). While inactive, returns steady true and holds no timer. The
 * timer is cleared on unmount and on every dependency change.
 */
export function useBlink(
  active: boolean,
  intervalMs: number = BLINK_INTERVAL_MS,
  clock: IntervalClock = systemIntervalClock
): boolean {
  const [visible, setVisible] = useState(true)

  useEffect(() => {
    if (!active) {
      return
    }
    // A cursor that comes back from hidden must not spend half a blink
    // invisible — every activation restarts from the visible phase.
    setVisible(true)
    const handle = clock.setInterval(() => setVisible((current) => !current), intervalMs)
    return () => clock.clearInterval(handle)
  }, [active, intervalMs, clock])

  return active ? visible : true
}

import { act, render, screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import { RUNS_POLL_INTERVAL_MS } from "@/shared/api/polling"
import type { RunsHubStatus } from "@/shared/realtime/runs-hub"

import { LiveBadge } from "./live-badge"

/* The merged badge is a presentational component: both inputs (`useMock`
   and `status`) are props, so each test just renders with the pair under
   inspection. No env mock, no hook mock — the topbar test owns the
   composition of these two values; this file owns the truth table. */

const find = (selector: string) => document.querySelector<HTMLElement>(selector)

function renderBadge(useMock: boolean, status: RunsHubStatus) {
  return render(<LiveBadge useMock={useMock} status={status} />)
}

/** One whole poll cycle, the grace window the polling pill waits out. */
async function passOnePollCycle() {
  await act(async () => {
    await vi.advanceTimersByTimeAsync(RUNS_POLL_INTERVAL_MS)
  })
}

describe("LiveBadge — the demo pill", () => {
  it("renders the pill in mock mode regardless of hub status", () => {
    // Env mock dominates: even when the hub somehow reports live (a test
    // can pin it), the pill is still showing because the build is on
    // seeds.
    renderBadge(true, "live")

    const pill = find('[data-test="demo-badge"]')
    expect(pill).not.toBeNull()
    expect(pill?.textContent).toBe("Demo")
  })

  it("renders the pill when real mode loses the hub", () => {
    renderBadge(false, "demo")

    const pill = find('[data-test="demo-badge"]')
    expect(pill).not.toBeNull()
    expect(pill?.textContent).toBe("Demo")
  })

  it("renders nothing when real mode is live — the absence is the confirmation", () => {
    const { container } = renderBadge(false, "live")

    expect(find('[data-test="demo-badge"]')).toBeNull()
    expect(container.firstChild).toBeNull()
  })
})

/* The second reading, and the one the bar was missing: `polling` is real
   data arriving late rather than synthetic data, so it gets its own word
   and its own hue — and a grace window, because `polling` is also the
   status the hub starts on and nobody needs a pill that blinks on every
   cold load. */
describe("LiveBadge — the polling pill", () => {
  beforeEach(() => {
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it("stays quiet for the first poll cycle — `polling` is also the starting status", () => {
    renderBadge(false, "polling")

    expect(find('[data-test="polling-badge"]')).toBeNull()

    act(() => {
      vi.advanceTimersByTime(RUNS_POLL_INTERVAL_MS - 1)
    })

    expect(find('[data-test="polling-badge"]')).toBeNull()
  })

  it("says so once a whole poll cycle has passed with no socket", async () => {
    renderBadge(false, "polling")

    await passOnePollCycle()

    const pill = find('[data-test="polling-badge"]')
    expect(pill).not.toBeNull()
    expect(pill?.textContent).toBe("Polling")
  })

  it("takes the queued hue, not the amber the demo pill owns", async () => {
    renderBadge(false, "polling")

    await passOnePollCycle()

    // Amber (`waiting`) is spoken for by "these are not the real numbers".
    // This pill says the opposite — real numbers, one cadence behind.
    const badge = find('[data-test="polling-badge"] [data-status]')
    expect(badge?.getAttribute("data-status")).toBe("queued")
  })

  it("never appears when the socket comes up inside the grace window", async () => {
    const { rerender } = renderBadge(false, "polling")

    act(() => {
      vi.advanceTimersByTime(Math.floor(RUNS_POLL_INTERVAL_MS / 2))
    })
    rerender(<LiveBadge useMock={false} status="live" />)
    await passOnePollCycle()

    expect(find('[data-test="polling-badge"]')).toBeNull()
  })

  it("clears the moment the socket comes back", async () => {
    const { container, rerender } = renderBadge(false, "polling")

    await passOnePollCycle()
    expect(find('[data-test="polling-badge"]')).not.toBeNull()

    rerender(<LiveBadge useMock={false} status="live" />)

    expect(find('[data-test="polling-badge"]')).toBeNull()
    expect(container.firstChild).toBeNull()
  })

  it("starts the count over after a reconnect that fails again", async () => {
    const { rerender } = renderBadge(false, "polling")

    await passOnePollCycle()
    rerender(<LiveBadge useMock={false} status="live" />)
    rerender(<LiveBadge useMock={false} status="polling" />)

    // A socket that flapped is a fresh outage, not a continuation of the
    // old one — the window is waited out again rather than carried over.
    expect(find('[data-test="polling-badge"]')).toBeNull()

    await passOnePollCycle()
    expect(find('[data-test="polling-badge"]')).not.toBeNull()
  })

  it("stays out of the way in mock mode — the demo pill outranks it", async () => {
    renderBadge(true, "polling")

    await passOnePollCycle()

    expect(find('[data-test="polling-badge"]')).toBeNull()
    expect(find('[data-test="demo-badge"]')).not.toBeNull()
  })

  it("names the cadence it fell back to, read off the same constant", async () => {
    renderBadge(false, "polling")

    await passOnePollCycle()

    // The pill is up; hand the clock back before touching the tooltip.
    // React Aria runs the overlay on its own timers and a faked clock
    // driven from the test never lets them finish.
    vi.useRealTimers()
    const user = userEvent.setup()

    // Focus rather than hover, for the reason the demo pill's tooltip test
    // gives: React Aria opens on focus with no dwell.
    await user.tab()

    const tooltip = await screen.findByRole("tooltip")
    expect(tooltip.textContent).toContain("Live updates are down")
    expect(tooltip.textContent).toContain(`${RUNS_POLL_INTERVAL_MS / 1000}s`)
  })
})

describe("LiveBadge — the tooltip", () => {
  it("names how to leave mock mode when only the env says mock", async () => {
    const user = userEvent.setup()
    renderBadge(true, "live")

    // Focus rather than hover: React Aria opens the tooltip on focus with
    // no dwell; the shared warmup timer a pointer has to clear first is
    // what we are not testing.
    await user.tab()

    const tooltip = await screen.findByRole("tooltip")
    expect(tooltip.textContent).toContain("synthetic data")
    expect(tooltip.textContent).toContain("bun run dev:real")

    // The pill is described by the tooltip, not named by it — the visible
    // word survives as the accessible name when the pointer leaves.
    const pill = find('[data-test="demo-badge"]')
    expect(pill?.getAttribute("aria-describedby")).not.toBeNull()
    expect(pill?.getAttribute("aria-label")).toBeNull()
  })

  it("names the backend when real mode loses the hub", async () => {
    const user = userEvent.setup()
    renderBadge(false, "demo")

    await user.tab()

    const tooltip = await screen.findByRole("tooltip")
    expect(tooltip.textContent).toContain("Backend unreachable")
  })

  it("names both reasons when mock mode coincides with a degraded hub", async () => {
    const user = userEvent.setup()
    renderBadge(true, "demo")

    await user.tab()

    const tooltip = await screen.findByRole("tooltip")
    expect(tooltip.textContent).toContain("synthetic data")
    expect(tooltip.textContent).toContain("backend unreachable")
  })
})

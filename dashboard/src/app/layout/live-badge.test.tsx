import { render, screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { describe, expect, it } from "vitest"

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

  it("renders nothing in real mode while polling — the polling layer owns refresh", () => {
    const { container } = renderBadge(false, "polling")

    expect(find('[data-test="demo-badge"]')).toBeNull()
    expect(container.firstChild).toBeNull()
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

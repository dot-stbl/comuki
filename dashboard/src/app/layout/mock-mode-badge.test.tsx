import { render, screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { afterEach, describe, expect, it, vi } from "vitest"

import { MockModeBadge } from "./mock-mode-badge"

/* The badge gates on `env.useMock` at render time; the module is mocked so
   each case can flip the gate without touching the real env schema. The
   hoisted holder is the same shape the topbar test uses — a vi.mock
   factory runs before file body, so a direct `vi.mocked(env)` would
   resolve to the real module. */

const envState = vi.hoisted(() => ({ useMock: true }))

vi.mock("@/shared/config/env", () => ({ env: envState }))

const find = (selector: string) =>
  document.querySelector<HTMLElement>(selector)

afterEach(() => {
  envState.useMock = true
})

describe("MockModeBadge", () => {
  it("renders the pill in mock mode with the data-test handle", () => {
    envState.useMock = true
    render(<MockModeBadge />)

    // `data-test` is the project's convention (see live-badge.tsx and the
    // topbar's `[data-test="repo-link"]` test); the contract for E2E and
    // screenshot tooling is the attribute, not a `data-testid` alias.
    const badge = find('[data-test="mock-mode-badge"]')
    expect(badge).not.toBeNull()
    expect(badge?.textContent).toBe("mock")
    expect(badge?.getAttribute("data-mock-mode")).toBe("true")
    // A button by accessibility — the tooltip wrapper needs an interactive
    // ARIA role for React Aria's focus machinery, and the accessible name
    // is the visible word.
    expect(badge?.tagName).toBe("BUTTON")
    expect(
      screen.getByRole("button", { name: "mock" })
    ).not.toBeNull()
  })

  it("renders nothing in real mode — a mock pill above a live backend would be a lie", () => {
    envState.useMock = false
    const { container } = render(<MockModeBadge />)

    expect(find('[data-test="mock-mode-badge"]')).toBeNull()
    expect(container.firstChild).toBeNull()
  })

  it("names how to leave mock mode in the tooltip", async () => {
    envState.useMock = true
    const user = userEvent.setup()
    render(<MockModeBadge />)

    // Focus rather than hover: React Aria opens the tooltip on focus with
    // no dwell; the shared warmup timer a pointer has to clear first is
    // what we are not testing.
    await user.tab()

    const tooltip = await screen.findByRole("tooltip")
    expect(tooltip.textContent).toContain("synthetic data")
    expect(tooltip.textContent).toContain("bun run dev:real")

    // The badge is described by the tooltip, not named by it — the visible
    // word survives as the accessible name when the pointer leaves.
    const badge = find('[data-test="mock-mode-badge"]')
    expect(badge?.getAttribute("aria-describedby")).not.toBeNull()
    expect(badge?.getAttribute("aria-label")).toBeNull()
  })
})
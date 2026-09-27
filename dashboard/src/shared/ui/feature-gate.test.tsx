import { render, screen } from "@testing-library/react"
import { describe, expect, it } from "vitest"

import { FeatureGate } from "./feature-gate"

/* The kit spells its test handle `data-test` (see AGENTS.md §2), so the
   queries go through the DOM rather than testing-library's `getByTestId`,
   which only knows the `data-testid` spelling. */
function lockedPanel(container: HTMLElement): HTMLElement | null {
  return container.querySelector('[data-test="feature-gate-locked"]')
}

describe("FeatureGate", () => {
  it("Renders children when available is true", () => {
    const { container } = render(
      <FeatureGate feature="multi-repo" available={true}>
        <p>open affordance</p>
      </FeatureGate>,
    )

    expect(screen.getByText("open affordance")).toBeTruthy()
    expect(lockedPanel(container)).toBeNull()
  })

  it("Renders the locked fallback when available is false", () => {
    const { container } = render(
      <FeatureGate feature="multi-repo" available={false}>
        <p>open affordance</p>
      </FeatureGate>,
    )

    const locked = lockedPanel(container)
    expect(locked).not.toBeNull()
    expect(locked?.getAttribute("data-feature")).toBe("multi-repo")
    expect(locked?.textContent).toContain("multi-repo")
    expect(locked?.textContent).toContain("not in this edition")
    expect(screen.queryByText("open affordance")).toBeNull()
  })

  it("Renders children optimistically when available is undefined", () => {
    const { container } = render(
      <FeatureGate feature="multi-repo" available={undefined}>
        <p>still loading</p>
      </FeatureGate>,
    )

    expect(screen.getByText("still loading")).toBeTruthy()
    expect(lockedPanel(container)).toBeNull()
  })

  it("Honours a caller-supplied fallback over the kit's locked affordance", () => {
    const { container } = render(
      <FeatureGate
        feature="enterprise-sso"
        available={false}
        fallback={<a href="mailto:sales">contact sales</a>}
      >
        <p>should not render</p>
      </FeatureGate>,
    )

    const custom = screen.getByText("contact sales")
    expect(custom.tagName).toBe("A")
    expect(screen.queryByText("should not render")).toBeNull()
    expect(lockedPanel(container)).toBeNull()
  })
})

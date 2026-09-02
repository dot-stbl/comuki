import { render } from "@testing-library/react"
import { describe, expect, it } from "vitest"

import { Sparkline } from "./sparkline"

function points(container: HTMLElement): Array<[number, number]> {
  const raw = container
    .querySelector('[data-test="sparkline-line"]')
    ?.getAttribute("points")
  if (!raw) {
    return []
  }
  return raw
    .split(" ")
    .map((pair) => pair.split(",").map(Number) as [number, number])
}

/** jsdom lays nothing out; the polyline's own `points` string is the geometry. */
describe("Sparkline", () => {
  it("draws one vertex per value, oldest first", () => {
    const { container } = render(
      <Sparkline values={[1, 2, 4, 8]} label="Burn by hour." />
    )

    const drawn = points(container)
    expect(drawn).toHaveLength(4)
    // First vertex at the start edge, last at the far edge.
    expect(drawn[0][0]).toBe(0)
    expect(drawn[3][0]).toBe(100)
  })

  it("measures against zero, so a bigger value is a higher point", () => {
    const { container } = render(
      <Sparkline values={[1, 8, 2]} label="Burn by hour." />
    )

    const drawn = points(container)
    expect(drawn[1][1]).toBeLessThan(drawn[0][1])
    expect(drawn[1][1]).toBeLessThan(drawn[2][1])
    // The peak keeps its headroom from the top edge.
    expect(drawn[1][1]).toBeGreaterThanOrEqual(4)
  })

  it("keeps the line off both edges so a peak is not clipped", () => {
    const { container } = render(
      <Sparkline values={[0, 5, 0]} label="Burn by hour." />
    )

    for (const [, y] of points(container)) {
      expect(y).toBeGreaterThan(0)
      expect(y).toBeLessThan(100)
    }
  })

  it("carries the reading as its accessible name", () => {
    const { getByRole } = render(
      <Sparkline
        values={[1, 2, 3]}
        label="Spend by hour: $6 total, peak $3 at 14:00."
      />
    )

    expect(getByRole("img").getAttribute("aria-label")).toBe(
      "Spend by hour: $6 total, peak $3 at 14:00."
    )
  })

  it("draws no line for a flat zero day, and stays named", () => {
    const { container, getByRole } = render(
      <Sparkline
        values={[0, 0, 0, 0]}
        label="Spend by hour: nothing metered."
      />
    )

    expect(
      container.querySelector('[data-test="sparkline-line"]')
    ).toBeNull()
    expect(getByRole("img").getAttribute("aria-label")).toContain("nothing")
  })

  it("draws no line for a single value — one value has no shape", () => {
    const { container } = render(
      <Sparkline values={[2]} label="Spend by hour: $2 so far." />
    )

    expect(
      container.querySelector('[data-test="sparkline-line"]')
    ).toBeNull()
  })
})

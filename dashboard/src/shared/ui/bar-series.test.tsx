import { render } from "@testing-library/react"
import { describe, expect, it } from "vitest"

import { BarSeries, barSeriesAxis } from "./bar-series"

function bars(container: HTMLElement): SVGRectElement[] {
  return [
    ...container.querySelectorAll<SVGRectElement>('[data-test="bar-series-bar"]'),
  ]
}

/** Heights are percentage strings ("62.5%") — the share, read off the DOM. */
function heightOf(bar: SVGRectElement): number {
  return parseFloat(bar.getAttribute("height") ?? "0")
}

/** Positions are percentages too. */
function xOf(bar: SVGRectElement): number {
  return parseFloat(bar.getAttribute("x") ?? "0")
}

function yOf(bar: SVGRectElement): number {
  return parseFloat(bar.getAttribute("y") ?? "0")
}

/** jsdom lays nothing out, so the contract is the generated attributes: every
 *  geometry fact below is read straight off the SVG DOM the way a browser
 *  would resolve it. */
describe("BarSeries", () => {
  const week = [
    { key: "6", label: "sat", segments: [{ value: 90 }] },
    { key: "5", label: "sun", segments: [{ value: 45 }] },
    { key: "4", label: "mon", segments: [{ value: 120 }] },
    { key: "3", label: "tue", segments: [{ value: 180 }] },
    { key: "2", label: "wed", segments: [{ value: 130 }] },
    { key: "1", label: "thu", segments: [{ value: 140 }] },
    { key: "0", label: "today", segments: [{ value: 148 }] },
  ]

  it("draws one bar per day with a value, on a shared axis", () => {
    const { container } = render(
      <BarSeries points={week} label="Spend by day: $853 this week." />
    )

    expect(bars(container)).toHaveLength(7)
    // The axis is the largest total: the heaviest day is the full plot height,
    // and every other bar is its exact share of that same axis.
    expect(
      Math.round(heightOf(bars(container)[3]))
    ).toBe(100)
    expect(Math.round(heightOf(bars(container)[1]))).toBe(25)
  })

  it("positions bars inside one slot per day, left to right in order", () => {
    const { container } = render(
      <BarSeries points={week} label="Spend by day." />
    )

    const xs = bars(container).map(xOf)
    expect(xs).toEqual([...xs].sort((a, b) => a - b))
    // Seven equal slots; the first bar starts inside the first slot, not at 0.
    expect(xs[0]).toBeGreaterThan(0)
    expect(xs[0]).toBeLessThan(100 / 7)
  })

  it("carries the reading as its accessible name, and nothing else announces", () => {
    const { container, getByRole } = render(
      <BarSeries points={week} label="Spend by day: $853 over the last 7 days." />
    )

    expect(getByRole("img").getAttribute("aria-label")).toBe(
      "Spend by day: $853 over the last 7 days."
    )
    // The drawn geometry is decoration on top of that sentence — the svg and
    // the tick row stay out of the tree the sentence already covers.
    expect(
      container.querySelector('[data-test="bar-series-plot"]')?.getAttribute("aria-hidden")
    ).toBe("true")
    expect(
      container.querySelector('[data-test="bar-series-axis"]')?.getAttribute("aria-hidden")
    ).toBe("true")
  })

  it("labels a tick per day under the plot", () => {
    const { container } = render(
      <BarSeries points={week} label="Spend by day." />
    )

    const ticks = [
      ...container.querySelectorAll<HTMLElement>('[data-test="bar-series-tick"]'),
    ]
    expect(ticks.map((tick) => tick.textContent)).toEqual([
      "sat",
      "sun",
      "mon",
      "tue",
      "wed",
      "thu",
      "today",
    ])
  })

  it("stacks segments from the baseline in order, each with its status", () => {
    const { container } = render(
      <BarSeries
        points={[
          {
            key: "0",
            label: "today",
            segments: [
              { value: 60, status: "success" },
              { value: 30, status: "failed" },
              { value: 10, status: "escalated" },
            ],
          },
        ]}
        label="Run outcomes: 100 finished today."
      />
    )

    const [success, failed, escalated] = bars(container)
    // Shares of the day's own total, stacked bottom-up in array order.
    expect(success.getAttribute("data-status")).toBe("success")
    expect(Math.round(heightOf(success))).toBe(60)
    expect(yOf(success)).toBe(40)

    expect(failed.getAttribute("data-status")).toBe("failed")
    expect(Math.round(heightOf(failed))).toBe(30)
    expect(yOf(failed)).toBe(10)

    expect(escalated.getAttribute("data-status")).toBe("escalated")
    expect(Math.round(heightOf(escalated))).toBe(10)
    expect(yOf(escalated)).toBe(0)
  })

  it("keeps a neutral bar chrome-toned and a status bar hue-carrying", () => {
    const neutral = render(
      <BarSeries
        points={[{ key: "0", label: "today", segments: [{ value: 5 }] }]}
        label="Queue depth."
      />
    )
    expect(
      neutral.container.querySelector('[data-test="bar-series-bar"]')?.hasAttribute("data-status")
    ).toBe(false)

    const stacked = render(
      <BarSeries
        points={[
          {
            key: "0",
            label: "today",
            segments: [
              { value: 5, status: "failed" },
              { value: 1 },
            ],
          },
        ]}
        label="Outcomes."
      />
    )
    expect(
      stacked.container.querySelector('[data-status="failed"]')
    ).not.toBeNull()
    expect(
      stacked.container.querySelector('[data-test="bar-series-bar"]:not([data-status])')
    ).not.toBeNull()
  })

  it("draws nothing for a day with no value, and keeps the tick", () => {
    const { container } = render(
      <BarSeries
        points={[
          { key: "1", label: "mon", segments: [{ value: 0 }] },
          { key: "0", label: "today", segments: [{ value: 4 }] },
        ]}
        label="Queue depth."
      />
    )

    expect(bars(container)).toHaveLength(1)
    expect(bars(container)[0].getAttribute("data-key")).toBe("0")
    expect(
      container.querySelectorAll('[data-test="bar-series-tick"]')
    ).toHaveLength(2)
  })

  it("keeps a value that is barely there visible", () => {
    const { container } = render(
      <BarSeries
        points={[
          { key: "1", label: "mon", segments: [{ value: 1 }] },
          { key: "0", label: "today", segments: [{ value: 90 }] },
        ]}
        label="Queue depth."
      />
    )

    // One run of ninety is not zero — the river's rule, kept here.
    expect(heightOf(bars(container)[0])).toBeGreaterThanOrEqual(2)
  })

  it("says the empty series rather than drawing a blank box", () => {
    const { container, getByRole } = render(
      <BarSeries
        points={[{ key: "0", label: "today", segments: [{ value: 0 }] }]}
        label="Queue depth: the queue is empty."
      />
    )

    expect(bars(container)).toHaveLength(0)
    expect(getByRole("img").getAttribute("aria-label")).toContain("empty")
  })

  it("exposes the axis so a screen's figure and its chart cannot disagree", () => {
    expect(barSeriesAxis(week)).toBe(180)
    // A quiet series still has an axis — floored, never zero.
    expect(
      barSeriesAxis([{ key: "0", label: "today", segments: [{ value: 0 }] }])
    ).toBe(1)
  })
})

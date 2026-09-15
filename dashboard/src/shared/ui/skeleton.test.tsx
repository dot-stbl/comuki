import { render } from "@testing-library/react"
import { describe, expect, it } from "vitest"

import { Skeleton } from "./skeleton"

describe("the skeleton draws the shape it was asked for", () => {
  it("takes the widths a screen already keeps beside its page", () => {
    const { container } = render(<Skeleton lines={["58%", "42%", "71%"]} />)
    const bars = container.querySelectorAll("span")
    expect(bars).toHaveLength(3)
    expect(bars[0]?.getAttribute("style")).toContain("58%")
    expect(bars[2]?.getAttribute("style")).toContain("71%")
  })

  it("falls back to its own rhythm when asked only for a count", () => {
    const { container } = render(<Skeleton lines={9} />)
    const bars = container.querySelectorAll("span")
    expect(bars).toHaveLength(9)
    // Uneven on purpose: a column of equal bars reads as a table that has
    // finished loading and is full of blanks.
    const widths = new Set(Array.from(bars, (bar) => bar.getAttribute("style")))
    expect(widths.size).toBe(9)
  })
})

describe("the skeleton announces itself once, politely", () => {
  it("is a status region, not an alert", () => {
    // Nothing has gone wrong yet. The polite region says "Loading" without
    // talking over whatever the operator was reading when they navigated here.
    render(<Skeleton data-test="tasks-loading" />)
    const region = document.querySelector("[data-test='tasks-loading']")
    expect(region?.getAttribute("role")).toBe("status")
    expect(region?.getAttribute("aria-label")).toBe("Loading")
  })

  it("keeps the bars themselves out of the reading", () => {
    const { container } = render(<Skeleton lines={4} />)
    for (const bar of container.querySelectorAll("span")) {
      expect(bar.getAttribute("aria-hidden")).toBe("true")
    }
  })
})

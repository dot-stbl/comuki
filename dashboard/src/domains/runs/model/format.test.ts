import { describe, expect, it } from "vitest"

import {
  briefSegments,
  formatCost,
  formatDuration,
  formatTokens,
} from "@/domains/runs/model/format"

describe("formatDuration", () => {
  it("reads as a run clock, zero-padded", () => {
    expect(formatDuration(0)).toBe("00:00")
    expect(formatDuration(75)).toBe("01:15")
    expect(formatDuration(-4)).toBe("00:00")
  })
})

describe("formatCost / formatTokens", () => {
  it("keeps money at two places and tokens in thousands", () => {
    expect(formatCost(0.4)).toBe("$0.40")
    expect(formatTokens(18400)).toBe("18.4k")
    expect(formatTokens(0)).toBe("0")
  })
})

describe("briefSegments", () => {
  it("splits the values a brief quotes out of its prose", () => {
    expect(briefSegments("call `POST /hooks` twice")).toEqual([
      { text: "call ", code: false },
      { text: "POST /hooks", code: true },
      { text: " twice", code: false },
    ])
  })

  it("treats an unmatched backtick as prose rather than opening a span", () => {
    expect(briefSegments("a ` b")).toEqual([
      { text: "a ", code: false },
      { text: " b", code: false },
    ])
  })

  it("has nothing to render for an empty brief", () => {
    expect(briefSegments("")).toEqual([])
  })
})

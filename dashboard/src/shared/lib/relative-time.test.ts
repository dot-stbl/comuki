import { describe, expect, it } from "vitest"

import {
  formatRelativeInstant,
  formatRelativeTime,
} from "@/shared/lib/relative-time"

const MINUTE = 60_000
const HOUR = 60 * MINUTE
const DAY = 24 * HOUR

describe("the console's one relative-time reading", () => {
  it("says 'just now' for anything inside a minute, either side of now", () => {
    expect(formatRelativeTime(0)).toBe("just now")
    expect(formatRelativeTime(59_999)).toBe("just now")
    expect(formatRelativeTime(-59_999)).toBe("just now")
  })

  it("climbs minutes, hours and days, and stops there", () => {
    expect(formatRelativeTime(8 * MINUTE)).toBe("8 min")
    expect(formatRelativeTime(59 * MINUTE)).toBe("59 min")
    expect(formatRelativeTime(HOUR)).toBe("1 h")
    expect(formatRelativeTime(23 * HOUR)).toBe("23 h")
    expect(formatRelativeTime(DAY)).toBe("1 d")
    // No week bucket: a key issued 74 days ago reads as 74 days, not as
    // "10 w" — the figure is what the operator compares against a policy.
    expect(formatRelativeTime(74 * DAY)).toBe("74 d")
  })

  it("marks the future with a word, because nothing else would", () => {
    // `12 d` and `in 12 d` are the only two readings a TTL column can make,
    // and a key that has lapsed must not look like one that has not.
    expect(formatRelativeTime(-12 * DAY)).toBe("in 12 d")
    expect(formatRelativeTime(-90 * MINUTE)).toBe("in 1 h")
  })

  it("floors rather than rounds, so a reading never runs ahead of the fact", () => {
    // 119 minutes is one hour *and some*, not two hours: an age that rounds up
    // claims time that has not passed.
    expect(formatRelativeTime(119 * MINUTE)).toBe("1 h")
    expect(formatRelativeTime(47 * HOUR)).toBe("1 d")
  })
})

describe("an ISO instant as the same reading", () => {
  const NOW = Date.parse("2026-09-13T12:00:00Z")

  it("measures from the given now rather than from the wall clock", () => {
    expect(formatRelativeInstant("2026-09-13T11:48:00Z", NOW)).toBe("12 min")
    expect(formatRelativeInstant("2026-09-13T08:20:00Z", NOW)).toBe("3 h")
  })

  it("answers null for an instant it cannot read, and lets the screen decide", () => {
    // The chat rail renders nothing for it, the decision card renders an em
    // dash. Neither of those is a fact about the wire, so neither is decided
    // here.
    expect(formatRelativeInstant("not a date", NOW)).toBeNull()
    expect(formatRelativeInstant("", NOW)).toBeNull()
  })
})

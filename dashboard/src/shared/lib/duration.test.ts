import { describe, expect, it } from "vitest"

import { formatDuration, formatDurationMs } from "@/shared/lib/duration"

describe("formatDuration — a run clock, in seconds", () => {
  it("reads as a clock, zero-padded", () => {
    expect(formatDuration(0)).toBe("00:00")
    expect(formatDuration(75)).toBe("01:15")
    expect(formatDuration(-4)).toBe("00:00")
  })
})

describe("formatDurationMs — a call's latency, in milliseconds", () => {
  it("keeps milliseconds under a second and seconds above it", () => {
    expect(formatDurationMs(420)).toBe("420ms")
    expect(formatDurationMs(999)).toBe("999ms")
    expect(formatDurationMs(1000)).toBe("1.0s")
    expect(formatDurationMs(31_420)).toBe("31.4s")
  })
})

describe("the two never agree, which is the point", () => {
  it("renders the same number differently, so a swapped unit is visible", () => {
    // 90 is a minute and a half of run time and a tenth of a second of
    // latency. If a call site ever reaches for the wrong one, it does not get
    // a plausible reading — it gets a reading from the wrong world.
    expect(formatDuration(90)).toBe("01:30")
    expect(formatDurationMs(90)).toBe("90ms")
  })
})

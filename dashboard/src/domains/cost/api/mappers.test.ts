import { describe, expect, it } from "vitest"

import {
  platformCostsWireToSummary,
} from "@/domains/cost/api/mappers"

const ROLLUP = {
  since: "2026-08-14T00:00:00Z",
  windowDays: 30,
  windowUsdMicros: 12_340_000,
  allTimeUsdMicros: 190_500_000,
  byProject: [
    {
      projectId: "b3d8a402-1111-2222-3333-444444444444",
      costUsdMicros: 9_100_000,
      runs: 34,
    },
    {
      projectId: "77c1a9e0-1111-2222-3333-444444444444",
      costUsdMicros: 3_240_000,
      runs: 12,
    },
  ],
  byDay: [
    { date: "2026-09-11", costUsdMicros: 500_000 },
    { date: "2026-09-12", costUsdMicros: 820_000 },
  ],
} as const

describe("the platform rollup onto the report", () => {
  it("converts micros to dollars exactly once", () => {
    const summary = platformCostsWireToSummary(ROLLUP)

    expect(summary.windowUsd).toBeCloseTo(12.34, 2)
    expect(summary.allTimeUsd).toBeCloseTo(190.5, 2)
    expect(summary.byApp[0]?.spend).toBeCloseTo(9.1, 2)
    expect(summary.byDay[1]?.spend).toBeCloseTo(0.82, 2)
  })

  it("degrades every figure the rollup does not carry", () => {
    const summary = platformCostsWireToSummary(ROLLUP)

    expect(summary.perSuccess).toBeNull()
    expect(summary.successRate).toBeNull()
    expect(summary.budget).toBeNull()
    expect(summary.failures).toEqual([])
  })

  it("states the window's daily average as the day figure", () => {
    const summary = platformCostsWireToSummary(ROLLUP)

    // $12.34 over the two days the series carries, to the cent.
    expect(summary.totalDay).toBe(6.17)
    expect(summary.windowDays).toBe(30)
  })

  it("labels each series day by its weekday and names projects through the caller's word", () => {
    const summary = platformCostsWireToSummary(ROLLUP, (projectId) =>
      projectId.startsWith("b3d8") ? "atlas" : projectId
    )

    // 2026-09-11 is a Friday, 2026-09-12 a Saturday — UTC, the rollup's own
    // timezone.
    expect(summary.byDay.map((day) => day.label)).toEqual(["fri", "sat"])
    expect(summary.byApp[0]?.app).toBe("atlas")
    expect(summary.byApp[1]?.app).toBe("77c1a9e0-1111-2222-3333-444444444444")
  })

  it("counts the runs the window's slices name", () => {
    const summary = platformCostsWireToSummary(ROLLUP)

    expect(summary.windowRuns).toBe(46)
  })
})

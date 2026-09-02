import { describe, expect, it } from "vitest"

import { COST_SEED } from "@/shared/api/mock/cost.seed"

import {
  budgetHeat,
  budgetLeftUsd,
  budgetPercent,
  budgetShare,
  failurePercent,
  spendAxis,
  spendDayAverage,
  spendPeakDay,
  spendShare,
  spendWeekTotal,
  successPercent,
} from "./cost"
import type { CostByApp, CostDaySpend, CostSummary } from "./types"

function app(name: string, spend: number): CostByApp {
  return { app: name, spend, runs: 1, perSuccess: spend, trend: "+0%" }
}

describe("the proxy cap", () => {
  it("reads the share as a fraction of the cap", () => {
    expect(budgetShare({ used: 110, cap: 220 })).toBeCloseTo(0.5)
    expect(budgetPercent({ used: 148.2, cap: 220 })).toBe(67)
  })

  it("says full when there is no cap to spend against", () => {
    // Not "nothing spent" — a cap of zero cannot hold anything, and a bar
    // reading empty there would be the one lie this meter must not tell.
    expect(budgetShare({ used: 12, cap: 0 })).toBe(1)
    expect(budgetHeat({ used: 12, cap: 0 })).toBe("over")
  })

  it("keeps three readings and no gradient between them", () => {
    expect(budgetHeat({ used: 100, cap: 220 })).toBe("ok")
    // 84.9% is still nothing to decide; 85% is where the decision starts.
    expect(budgetHeat({ used: 84.9, cap: 100 })).toBe("ok")
    expect(budgetHeat({ used: 85, cap: 100 })).toBe("near")
    expect(budgetHeat({ used: 99.9, cap: 100 })).toBe("near")
    expect(budgetHeat({ used: 100, cap: 100 })).toBe("over")
    expect(budgetHeat({ used: 260, cap: 220 })).toBe("over")
  })

  it("never reports negative headroom", () => {
    expect(budgetLeftUsd({ used: 148.2, cap: 220 })).toBeCloseTo(71.8)
    expect(budgetLeftUsd({ used: 260, cap: 220 })).toBe(0)
  })
})

describe("the breakdown's shared axis", () => {
  it("measures every bar against the largest spend", () => {
    const rows = [app("billing-api", 52.4), app("docs-site", 6.7)]
    const axis = spendAxis(rows)

    expect(axis).toBe(52.4)
    expect(spendShare(rows[0], axis)).toBe(1)
    expect(spendShare(rows[1], axis)).toBeCloseTo(0.128, 3)
  })

  it("floors the axis at a dollar so a quiet day draws quiet bars", () => {
    // Five cents across five apps is not five full-length bars.
    expect(spendAxis([app("a", 0.05), app("b", 0.02)])).toBe(1)
    expect(spendShare(app("a", 0.05), 1)).toBeCloseTo(0.05)
  })

  it("draws nothing at all for a breakdown with no rows", () => {
    expect(spendAxis([])).toBe(0)
    expect(spendShare(app("a", 12), 0)).toBe(0)
  })
})

describe("the readings the tiles state in words", () => {
  it("rounds the day's success rate to whole percent", () => {
    const summary = { successRate: 0.86 } as CostSummary
    expect(successPercent(summary)).toBe(86)
  })

  it("rounds a profile's failure rate to whole percent", () => {
    expect(failurePercent({ profile: "planner", rate: 0.11, note: "" })).toBe(11)
  })
})

describe("the week behind the day", () => {
  const days: CostDaySpend[] = [
    { label: "sat", spend: 96.5 },
    { label: "sun", spend: 88.2 },
    { label: "mon", spend: 130.4 },
    { label: "tue", spend: 180.8 },
    { label: "wed", spend: 131.1 },
    { label: "thu", spend: 142.6 },
    { label: "today", spend: 148.2 },
  ]

  it("totals the window to the cent", () => {
    expect(spendWeekTotal(days)).toBeCloseTo(917.8, 2)
  })

  it("averages over the series' own length, not a hardcoded week", () => {
    expect(spendDayAverage(days)).toBeCloseTo(131.11, 2)
    expect(spendDayAverage(days.slice(0, 3))).toBeCloseTo(105.03, 2)
  })

  it("has no average at all for an empty window", () => {
    // Not zero — zero would say the week was free.
    expect(spendDayAverage([])).toBeNull()
    expect(spendPeakDay([])).toBeNull()
  })

  it("names the heaviest day, not the latest", () => {
    expect(spendPeakDay(days)?.label).toBe("tue")
  })
})

describe("the seeded week tells the seeded story", () => {
  const byDay = COST_SEED.byDay

  it("anchors today's column to the per-day tile exactly", () => {
    // One reading said twice: the tile above the chart and the chart's last
    // bar are the same number, or the report argues with itself.
    expect(byDay).toHaveLength(7)
    expect(byDay[byDay.length - 1]?.spend).toBe(COST_SEED.totalDay)
    expect(byDay[byDay.length - 1]?.daysAgo).toBe(0)
  })

  it("puts the spike where the incident story says one is", () => {
    // Three days back the auth-svc migration ran; the app list still carries
    // its +21% trend, and the outcomes seed spikes on the same day.
    const incident = byDay.find((day) => day.daysAgo === 3)
    const peak = spendPeakDay(
      byDay.map((day) => ({ label: day.weekday, spend: day.spend }))
    )
    expect(incident?.spend).toBeGreaterThan(COST_SEED.totalDay)
    expect(peak?.label).toBe(incident?.weekday)
  })

  it("keeps the weekend columns visibly lighter than the weekdays", () => {
    // Today is anchored to the tile and the incident day carries its spike, so
    // neither belongs in either set; whichever of them falls on a weekend, the
    // *other* weekend column still has to read as a quiet day.
    const quiet = byDay.filter(
      (day) => day.weekend && day.daysAgo > 0 && day.daysAgo !== 3
    )
    const working = byDay.filter(
      (day) => !day.weekend && day.daysAgo > 0 && day.daysAgo !== 3
    )
    expect(quiet.length).toBeGreaterThanOrEqual(1)
    expect(working.length).toBeGreaterThanOrEqual(3)

    const heaviestQuiet = Math.max(...quiet.map((day) => day.spend))
    const lightestWorking = Math.min(...working.map((day) => day.spend))
    expect(heaviestQuiet).toBeLessThan(lightestWorking)
  })

  it("derives its weekday labels from the clock, not a stamped date", () => {
    for (const day of byDay) {
      expect(day.weekday).toMatch(/^(mon|tue|wed|thu|fri|sat|sun|today)$/)
    }
    expect(new Set(byDay.map((day) => day.weekday)).size).toBe(7)
  })
})

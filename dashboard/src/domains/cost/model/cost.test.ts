import { describe, expect, it } from "vitest"

import {
  budgetHeat,
  budgetLeftUsd,
  budgetPercent,
  budgetShare,
  failurePercent,
  spendAxis,
  spendShare,
  successPercent,
} from "./cost"
import type { CostByApp, CostSummary } from "./types"

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

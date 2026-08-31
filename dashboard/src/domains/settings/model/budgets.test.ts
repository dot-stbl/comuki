import { describe, expect, it } from "vitest"

import {
  BUDGET_NEAR_SHARE,
  budgetHeat,
  budgetLeftUsd,
  budgetPercent,
  budgetShare,
} from "@/domains/settings/model/budgets"
import type { Budgets } from "@/domains/settings/model/types"

function budgets(usedUsd: number, globalUsd: number): Budgets {
  return {
    perTaskUsd: 2,
    perAppUsd: 40,
    globalUsd,
    usedUsd,
    killSwitch: false,
    pauseSwarm: false,
  }
}

describe("the proxy budget, read three ways", () => {
  it("stays quiet while there is nothing to decide", () => {
    expect(budgetHeat(budgets(148.2, 220))).toBe("ok")
    expect(budgetPercent(budgets(148.2, 220))).toBe(67)
  })

  it("turns at the point where headroom becomes a deadline", () => {
    const at = budgets(BUDGET_NEAR_SHARE * 200, 200)
    expect(budgetHeat(at)).toBe("near")
    // One dollar short of the threshold is still nothing to decide — the
    // reading has to change *at* the line, not near it.
    expect(budgetHeat(budgets(BUDGET_NEAR_SHARE * 200 - 1, 200))).toBe("ok")
  })

  it("calls the cap itself over, not near", () => {
    expect(budgetHeat(budgets(200, 200))).toBe("over")
    expect(budgetHeat(budgets(240, 200))).toBe("over")
    // What is left never goes negative: "-$40 left" is not a reading.
    expect(budgetLeftUsd(budgets(240, 200))).toBe(0)
  })

  it("treats an absent cap as no cap rather than as a cap of zero", () => {
    // A full bar would say the swarm had spent everything it was allowed,
    // which is the opposite of what an unbudgeted proxy is doing.
    expect(budgetShare(budgets(500, 0))).toBe(0)
    expect(budgetHeat(budgets(500, 0))).toBe("ok")
  })
})

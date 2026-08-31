import type { Budgets } from "@/domains/settings/model/types"

/**
 * How hot the global cap is, in three readings rather than a gradient.
 *
 * Below `near` there is nothing to decide, and a screen that colours a proxy
 * budget at 40% has taught the operator to ignore the colour by the time one
 * reaches 90%. The two thresholds are the ones the swarm itself acts on: at the
 * cap the kill-switch stops new claims, and the step before it is the last
 * point at which raising the cap is still a decision rather than an incident.
 */
export type BudgetHeat = "ok" | "near" | "over"

/** The point where a cap stops being headroom and starts being a deadline. */
export const BUDGET_NEAR_SHARE = 0.85

/** Spend against the global cap, clamped at nothing and unbounded above. */
export function budgetShare(budgets: Budgets): number {
  if (budgets.globalUsd <= 0) {
    // No cap is not a cap of zero: an unbudgeted proxy is spending against
    // nothing, and a full bar would say the opposite of what is true.
    return 0
  }
  return Math.max(0, budgets.usedUsd / budgets.globalUsd)
}

export function budgetHeat(budgets: Budgets): BudgetHeat {
  const share = budgetShare(budgets)
  if (share >= 1) {
    return "over"
  }
  return share >= BUDGET_NEAR_SHARE ? "near" : "ok"
}

/** The reading as whole percent, which is how the screen says it. */
export function budgetPercent(budgets: Budgets): number {
  return Math.round(budgetShare(budgets) * 100)
}

/** What is left before the kill-switch fires. Never below zero. */
export function budgetLeftUsd(budgets: Budgets): number {
  return Math.max(0, budgets.globalUsd - budgets.usedUsd)
}

import type {
  CostBudget,
  CostByApp,
  CostFailure,
  CostSummary,
} from "./types"

/**
 * The day's arithmetic, in one place.
 *
 * All of this used to live inline in `cost-page.tsx` and in the two panels
 * under it — three files each doing their own division, and the only reason
 * they agreed was that nobody had edited one of them yet. A report whose
 * figures are derived in the components that draw them has no single place to
 * be wrong in, which is exactly the property the screen is for.
 */

/** Three readings of the proxy cap. Not a gradient — see `budgetHeat`. */
export type BudgetHeat = "ok" | "near" | "over"

/** How much of the proxy cap is spent, 0–1 and uncapped above 1. */
export function budgetShare(budget: CostBudget): number {
  if (budget.cap <= 0) {
    // No cap is not "nothing spent" — it is a cap that cannot hold anything,
    // and the bar says full rather than empty.
    return 1
  }
  return Math.max(0, budget.used / budget.cap)
}

/**
 * Three readings, not a gradient.
 *
 * `near` starts at 85% because that is where the budget stops being a fact and
 * starts being a thing somebody has to decide about — raise the cap, or let the
 * kill-switch stop the swarm at it. Below that there is nothing to do, and a
 * screen that colours the bar at 40% has taught the operator to ignore the
 * colour by the time it reaches 90%. The same threshold and the same three
 * words as `models/model/keys.ts`, because it is the same question about a
 * different cap.
 */
export function budgetHeat(budget: CostBudget): BudgetHeat {
  const share = budgetShare(budget)
  if (share >= 1) {
    return "over"
  }
  if (share >= 0.85) {
    return "near"
  }
  return "ok"
}

/** What is left under the cap, in dollars. Never negative. */
export function budgetLeftUsd(budget: CostBudget): number {
  return Math.max(0, budget.cap - budget.used)
}

/** The cap's reading as whole percent — the figure the tile shows. */
export function budgetPercent(budget: CostBudget): number {
  return Math.round(budgetShare(budget) * 100)
}

/** Tasks that cleared, as whole percent — the figure the day tile shows. */
export function successPercent(summary: CostSummary): number {
  return Math.round(summary.successRate * 100)
}

/**
 * The axis every bar in the breakdown is measured against: the largest spend.
 *
 * Floored at one dollar, which is not arithmetic hygiene — it is what stops a
 * day where almost nothing ran from drawing five full-length bars over five
 * cents. A breakdown with no rows has no axis at all and draws nothing.
 */
export function spendAxis(rows: CostByApp[]): number {
  if (rows.length === 0) {
    return 0
  }
  return Math.max(...rows.map((row) => row.spend), 1)
}

/**
 * One app's share of that axis, 0–1.
 *
 * Every bar is measured against the same axis on purpose: two bars on
 * different scales cannot be compared, and comparing them is the whole task.
 */
export function spendShare(row: CostByApp, axis: number): number {
  if (axis <= 0) {
    return 0
  }
  return Math.min(1, Math.max(0, row.spend / axis))
}

/** A profile's failure rate as whole percent. */
export function failurePercent(row: CostFailure): number {
  return Math.round(row.rate * 100)
}

import type {
  CostBudget,
  CostByApp,
  CostByModel,
  CostDaySpend,
  CostFailure,
  CostForecast,
  CostPeriod,
  CostSummary,
  CostTopProject,
} from "./types"

/**
 * The day's arithmetic, in one place.
 *
 * All of this used to live inline in `cost-page.tsx` and in the two panels
 * under it — three files each doing their own division, and the only reason
 * they agreed was that nobody had edited one of them yet. A report whose
 * figures are derived in the components that draw them has no single place
 * to be wrong in, which is exactly the property the screen is for.
 */

// Re-export the domain types this module's helpers operate on, so a caller
// imports everything from `./cost` without reaching into `./types`.
export type {
  CostBudget,
  CostByApp,
  CostByModel,
  CostDaySpend,
  CostFailure,
  CostForecast,
  CostPeriod,
  CostSummary,
  CostTopProject,
}

/**
 * Three readings of a cap, not a gradient — see `budgetHeat` and `costHeat`.
 *
 * `near` starts at 85% because that is where the budget stops being a fact and
 * starts being a thing somebody has to decide about — raise the cap, or let the
 * kill-switch stop the swarm at it. Below that there is nothing to do, and a
 * screen that colours the bar at 40% has taught the operator to ignore the
 * colour by the time it reaches 90%. The same threshold and the same three
 * words across every screen that reads a cap.
 */
export type BudgetHeat = "ok" | "near" | "over"
export type CostHeat = BudgetHeat

export function costHeat(share: number): CostHeat {
  if (share >= 1) {
    return "over"
  }
  if (share >= 0.85) {
    return "near"
  }
  return "ok"
}

/** A share of a cap, clamped to 0..1, with NaN-safe defaults. */
export function capShare(used: number, cap: number): number {
  if (!(cap > 0)) {
    return 1
  }
  if (!(used >= 0)) {
    return 0
  }
  return Math.min(1, Math.max(0, used / cap))
}

/**
 * Delta vs the previous period, as a signed fraction.
 *
 * Returns `null` when there is no previous period to compare against — the
 * period has just opened, or this is the very first report — and the screen
 * renders an honest dash rather than a "0%" that says nothing happened.
 */
export function periodDelta(current: number, previous: number): number | null {
  if (!(previous > 0)) {
    return null
  }
  return (current - previous) / previous
}

export function budgetHeat(budget: CostBudget): CostHeat {
  const share = budgetShare(budget)
  if (share >= 1) {
    return "over"
  }
  if (share >= 0.85) {
    return "near"
  }
  return "ok"
}

/** How much of the cap is spent, 0–1 and uncapped above 1. */
export function budgetShare(budget: CostBudget): number {
  if (budget.cap <= 0) {
    // No cap is not "nothing spent" — it is a cap that cannot hold anything,
    // and the bar says full rather than empty.
    return 1
  }
  return Math.max(0, budget.used / budget.cap)
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
  return Math.round((summary.successRate ?? 0) * 100)
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

/* --------------------------------------------------------------------------
 * The week behind the day — the time half of the report.
 * ------------------------------------------------------------------------ */

export function spendWeekTotal(days: CostDaySpend[]): number {
  return Math.round(days.reduce((sum, day) => sum + day.spend, 0) * 100) / 100
}

export function spendDayAverage(days: CostDaySpend[]): number | null {
  if (days.length === 0) {
    return null
  }
  return Math.round((spendWeekTotal(days) / days.length) * 100) / 100
}

export function spendPeakDay(days: CostDaySpend[]): CostDaySpend | null {
  return days.reduce<CostDaySpend | null>(
    (peak, day) => (peak === null || day.spend > peak.spend ? day : peak),
    null
  )
}

/**
 * The model breakdown axis, on the same floor + share contract as
 * `spendAxis` / `spendShare`. Same reasoning — a day where only one model ran
 * would draw a full-length bar over five cents, and a breakdown's only job is
 * to compare.
 */
export function modelAxis(rows: CostByModel[]): number {
  if (rows.length === 0) {
    return 0
  }
  return Math.max(...rows.map((row) => row.spend), 1)
}

export function modelShare(row: CostByModel, axis: number): number {
  if (axis <= 0) {
    return 0
  }
  return Math.min(1, Math.max(0, row.spend / axis))
}

/**
 * Project ranking axis — the largest spend across the top-N.
 *
 * Same floor-and-share rule as the per-app axis; the table sorts itself
 * by spend before it lands here, so the axis only ever measures across
 * already-ranked rows.
 */
export function projectAxis(rows: CostTopProject[]): number {
  if (rows.length === 0) {
    return 0
  }
  return Math.max(...rows.map((row) => row.spend), 1)
}

export function projectShare(row: CostTopProject, axis: number): number {
  if (axis <= 0) {
    return 0
  }
  return Math.min(1, Math.max(0, row.spend / axis))
}

/** The whole number of days in a period — used by forecast + period copy. */
export function periodDays(period: CostPeriod): number {
  if (period === "day") {
    return 1
  }
  if (period === "week") {
    return 7
  }
  return 30
}

/**
 * The portion of the period that has elapsed, in days.
 *
 * Returned as the count of past days in the byDay axis — what the seed has
 * observed so far — divided by the period's full length.
 */
export function periodElapsed(
  daysObserved: number,
  period: CostPeriod
): number {
  if (daysObserved <= 0) {
    return 0
  }
  const total = periodDays(period)
  return Math.min(1, daysObserved / total)
}

/**
 * Wire-shaped types the seed/mapper carry across the boundary.
 *
 * The model layer (`./cost`) takes these and re-projects the ones the
 * domain actually reads.
 */

export interface CostByApp {
  app: string
  spend: number
  runs: number
  perSuccess: number
  trend: string
}

/** One day of the spend series: which weekday it was, and what it cost. */
export interface CostDaySpend {
  /** The day's short label ("mon", "today"). */
  label: string
  spend: number
}

export interface CostFailure {
  profile: string
  rate: number
  note: string
}

export interface CostBudget {
  used: number
  cap: number
}

/**
 * The cost report, in model-shaped form.
 *
 * Domain model — the cost page renders this; the seed's `SeedCostSummary` is
 * the wire-shaped source and the mapper turns one into the other. Everything
 * the new widgets need (model breakdown, forecast, project ranking, the
 * period the report is for) lives here.
 *
 * The two history axes — byDay (per-day for the chosen period) and byModel
 * (per-model for the chosen period) — are both delivered for the active
 * period. The screen does not transform period data on the fly; it asks the
 * query for the period it wants, and the seed re-shapes accordingly. The
 * period is a value, not a derived index.
 */

export type CostPeriod = "day" | "week" | "month"

export interface CostByModel {
  /** LLM model identifier (`glm-5.2`, `glm-4.5`, `MiniMax-M3`). The seed's
   *  full model lineup, not a top-N. */
  model: string
  spend: number
  tokens: number
  runs: number
}

export interface CostTopProject {
  projectId: string
  /** Project key (`comuki`, `atlas`) — what the operator reads. */
  projectKey: string
  projectName: string
  spend: number
  runs: number
  /** Current budget cap (USD). Zero means "no cap". */
  cap: number
}

export interface CostForecast {
  /** Burn rate in USD per day, averaged over the period. */
  burnRatePerDay: number
  /** Projected end-of-period spend at current burn rate. */
  projectedEndOfPeriod: number
  /** Budget cap the forecast is checked against. */
  cap: number
  /** The forecast as a share of the cap, 0–1+ (uncapped above 1). */
  share: number
}

export interface CostSummary {
  /** Period this report covers. Drives the byDay / byModel lengths. */
  period: CostPeriod
  /** Total spend for the chosen period. */
  totalPeriod: number
  /** Spend in the period immediately before this one — drives the delta. */
  totalPreviousPeriod: number
  /** What the swarm spent today — invariant of the period toggle so the
   *  budget widget can show "today" without re-fetching. */
  todaySpend: number
  /** Today's budget cap (always shown as the first row of the budget tile). */
  todayCap: number
  /** Spend so far this month — invariant of the period toggle. */
  monthSpend: number
  /** This month's budget cap. */
  monthCap: number
  /** Per-success cost (cents). */
  perSuccess: number
  /** Tasks cleared, 0–1. */
  successRate: number
  /** Spend per app for the chosen period. */
  byApp: CostByApp[]
  /** Spend per LLM model for the chosen period. */
  byModel: CostByModel[]
  /** Spend per day for the chosen period, oldest first. */
  byDay: CostDaySpend[]
  /** Top projects by spend for the chosen period. */
  topProjects: CostTopProject[]
  /** Budget cap and how much of it the period has spent. */
  budget: CostBudget
  /** Forecast to end of period at current burn rate. */
  forecast: CostForecast
  /** Per-profile failure analytics. */
  failures: CostFailure[]
}

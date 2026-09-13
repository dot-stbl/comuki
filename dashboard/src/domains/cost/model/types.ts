export interface CostByApp {
  app: string
  spend: number
  runs: number
  /** `null` when the source carries no per-success price. */
  perSuccess: number | null
  /** One-word direction; `null` when the source carries no trend. */
  trend: string | null
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

export interface CostSummary {
  /**
   * Cost per successful task. `null` when the source does not compute it —
   * the platform rollup counts spend and runs, and a success price it never
   * divided is not ours to invent.
   */
  perSuccess: number | null
  /**
   * The day's figure on the tile. In mock it is a day's spend; in real mode
   * it is the window's daily average (derived, and labelled as such).
   */
  totalDay: number | null
  /** Fraction of tasks that cleared; `null` when unreported. */
  successRate: number | null
  byApp: CostByApp[]
  /** The proxy cap reading; `null` when no cap is reported. */
  budget: CostBudget | null
  failures: CostFailure[]
  /** Spend per day over the chosen window, oldest first. */
  byDay: CostDaySpend[]
  /** Real-mode window facts the tiles name directly. */
  windowDays?: number
  /** Window spend across every project, real mode. */
  windowUsd?: number
  /** All-time spend across every project, real mode. */
  allTimeUsd?: number
  /** Runs named by the window's slices, real mode. */
  windowRuns?: number
}

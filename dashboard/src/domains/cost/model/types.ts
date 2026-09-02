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

export interface CostSummary {
  perSuccess: number
  totalDay: number
  successRate: number
  byApp: CostByApp[]
  budget: CostBudget
  failures: CostFailure[]
  /** Spend per day over the chosen window, oldest first. */
  byDay: CostDaySpend[]
}

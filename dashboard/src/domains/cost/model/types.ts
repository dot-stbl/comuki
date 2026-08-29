export interface CostByApp {
  app: string
  spend: number
  runs: number
  perSuccess: number
  trend: string
}

export interface CostFailure {
  stage: string
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
}

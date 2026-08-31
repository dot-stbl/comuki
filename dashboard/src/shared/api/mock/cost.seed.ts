export interface SeedCostByApp {
  app: string
  spend: number
  runs: number
  perSuccess: number
  trend: string
}

export interface SeedCostFailure {
  profile: string
  rate: number
  note: string
}

export interface SeedCostBudget {
  used: number
  cap: number
}

export interface SeedCostSummary {
  perSuccess: number
  totalDay: number
  successRate: number
  byApp: SeedCostByApp[]
  budget: SeedCostBudget
  failures: SeedCostFailure[]
}

export const COST_SEED: SeedCostSummary = {
  perSuccess: 0.42,
  totalDay: 148.2,
  successRate: 0.86,
  byApp: [
    {
      app: "billing-api",
      spend: 52.4,
      runs: 38,
      perSuccess: 0.41,
      trend: "+6%",
    },
    {
      app: "web-app",
      spend: 41.1,
      runs: 51,
      perSuccess: 0.33,
      trend: "-3%",
    },
    {
      app: "auth-svc",
      spend: 33.8,
      runs: 12,
      perSuccess: 1.12,
      trend: "+21%",
    },
    {
      app: "worker-pool",
      spend: 14.2,
      runs: 22,
      perSuccess: 0.29,
      trend: "-1%",
    },
    {
      app: "docs-site",
      spend: 6.7,
      runs: 9,
      perSuccess: 0.38,
      trend: "+2%",
    },
  ],
  budget: { used: 148.2, cap: 220 },
  failures: [
    {
      profile: "planner",
      rate: 0.11,
      note: "types mismatch most often",
    },
    {
      profile: "tester",
      rate: 0.07,
      note: "flaky e2e on CI",
    },
    {
      profile: "implementer",
      rate: 0.04,
      note: "escalates to lead",
    },
  ],
}

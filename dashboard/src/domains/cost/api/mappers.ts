import type { CostSummary } from "@/domains/cost/model/types"
import type { SeedCostSummary } from "@/shared/api/mock/cost.seed"

export function toCostSummary(seed: SeedCostSummary): CostSummary {
  return {
    perSuccess: seed.perSuccess,
    totalDay: seed.totalDay,
    successRate: seed.successRate,
    byApp: seed.byApp.map((row) => ({
      app: row.app,
      spend: row.spend,
      runs: row.runs,
      perSuccess: row.perSuccess,
      trend: row.trend,
    })),
    budget: {
      used: seed.budget.used,
      cap: seed.budget.cap,
    },
    failures: seed.failures.map((row) => ({
      profile: row.profile,
      rate: row.rate,
      note: row.note,
    })),
  }
}

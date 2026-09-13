import type { CostSummary } from "@/domains/cost/model/cost"
import type {
  SeedCostPeriod,
  SeedCostSummary,
} from "@/shared/api/mock/cost.seed"

/**
 * One period of the seed → one `CostSummary`. Used both by the mock-first
 * query (which calls `toCostSummary(COST_SEED)`) and by the period
 * branches of the seed (`COST_SEED_BY_PERIOD`), so every screen reads the
 * same shape regardless of period.
 *
 * The mapper is also where the forecast's `share` lands: the seed gives the
 * raw numbers (cap, burn rate, projected end-of-period) and the model fills
 * in the *share* of the cap so the screen can paint it with the same
 * `costHeat` reading the budget already uses.
 */
export function toCostSummary(seed: SeedCostSummary): CostSummary {
  return {
    period: seed.period,
    totalPeriod: seed.totalPeriod,
    totalPreviousPeriod: seed.totalPreviousPeriod,
    todaySpend: seed.todaySpend,
    todayCap: seed.todayCap,
    monthSpend: seed.monthSpend,
    monthCap: seed.monthCap,
    perSuccess: seed.perSuccess,
    successRate: seed.successRate,
    byApp: seed.byApp.map((row) => ({
      app: row.app,
      spend: row.spend,
      runs: row.runs,
      perSuccess: row.perSuccess,
      trend: row.trend,
    })),
    byModel: seed.byModel.map((row) => ({
      model: row.model,
      spend: row.spend,
      tokens: row.tokens,
      runs: row.runs,
    })),
    byDay: seed.byDay.map((day) => ({
      label: day.weekday,
      spend: day.spend,
    })),
    topProjects: seed.topProjects.map((row) => ({
      projectId: row.projectId,
      projectKey: row.projectKey,
      projectName: row.projectName,
      spend: row.spend,
      runs: row.runs,
      cap: row.cap,
    })),
    budget: {
      used: seed.budget.used,
      cap: seed.budget.cap,
    },
    forecast: {
      cap: seed.forecast.cap,
      burnRatePerDay: seed.forecast.burnRatePerDay,
      projectedEndOfPeriod: seed.forecast.projectedEndOfPeriod,
      share:
        seed.forecast.cap > 0
          ? seed.forecast.projectedEndOfPeriod / seed.forecast.cap
          : 1,
    },
    failures: seed.failures.map((row) => ({
      profile: row.profile,
      rate: row.rate,
      note: row.note,
    })),
  }
}

/**
 * One period from the by-period snapshot → `SeedCostSummary` (the shape
 * `toCostSummary` already accepts).
 *
 * Stamp the period on the snapshot so the screen can answer "what period
 * is this?" without having to remember which branch it picked.
 */
export function periodSnapshot(
  period: SeedCostPeriod,
  snapshot: Omit<SeedCostSummary, "period">
): SeedCostSummary {
  return { period, ...snapshot }
}

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
    byDay: seed.byDay.map((day) => ({
      label: day.weekday,
      spend: day.spend,
    })),
  }
}

/* ------------------------------------------------------------------ *
 * The wire — `GET /api/v1/costs?days=N` (`PlatformCostsView`, camelCased
 * by the serializer). Money fields are USD micros (1 USD = 1_000_000) and
 * are converted exactly once, here.
 * ------------------------------------------------------------------ */

/** The host's `PlatformCostsView`. */
export interface PlatformCostsWire {
  readonly since: string
  readonly windowDays: number
  readonly windowUsdMicros: number
  readonly allTimeUsdMicros: number
  readonly byProject: readonly {
    readonly projectId: string
    readonly costUsdMicros: number
    readonly runs: number
  }[]
  readonly byDay: readonly {
    readonly date: string
    readonly costUsdMicros: number
  }[]
}

/** USD micros to dollars — the one place the unit changes. */
function usd(micros: number): number {
  return micros / 1_000_000
}

/** A UTC day ("2026-09-10") as the series' own weekday label ("thu"). */
function weekday(date: string): string {
  const parsed = Date.parse(`${date}T00:00:00Z`)
  if (Number.isNaN(parsed)) {
    return date
  }
  return new Date(parsed)
    .toLocaleDateString("en-US", { weekday: "short", timeZone: "UTC" })
    .toLowerCase()
}

/**
 * The platform rollup onto the report's summary.
 *
 * What the rollup carries, the summary states: window and all-time spend,
 * per-project slices, the day series. What it does not carry — a per-success
 * price, a success rate, a failure rollup, a proxy cap — degrades to the
 * nulls the tiles know how to draw, and the daily figure becomes the
 * window's average (derived, and labelled as an average on the tile).
 */
export function platformCostsWireToSummary(
  wire: PlatformCostsWire,
  projectName: (projectId: string) => string = (projectId) => projectId
): CostSummary {
  const windowUsd = usd(wire.windowUsdMicros)
  const days = wire.byDay.length

  return {
    perSuccess: null,
    totalDay:
      days > 0
        ? Math.round((windowUsd / days) * 100) / 100
        : windowUsd,
    successRate: null,
    byApp: wire.byProject.map((slice) => ({
      app: projectName(slice.projectId),
      spend: usd(slice.costUsdMicros),
      runs: slice.runs,
      perSuccess: null,
      trend: null,
    })),
    budget: null,
    failures: [],
    byDay: wire.byDay.map((day) => ({
      label: weekday(day.date),
      spend: usd(day.costUsdMicros),
    })),
    windowDays: wire.windowDays,
    windowUsd,
    allTimeUsd: usd(wire.allTimeUsdMicros),
    windowRuns: wire.byProject.reduce((total, slice) => total + slice.runs, 0),
  }
}

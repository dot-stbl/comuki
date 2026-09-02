import { seedDayAxis } from "./runs.seed"

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

/** One column of the spend-by-day series. See the block comment below. */
export interface SeedCostDay {
  daysAgo: number
  weekday: string
  /** Saturday or Sunday — the days the spend story expects to be lighter. */
  weekend: boolean
  spend: number
}

export interface SeedCostSummary {
  perSuccess: number
  totalDay: number
  successRate: number
  byApp: SeedCostByApp[]
  budget: SeedCostBudget
  failures: SeedCostFailure[]
  /** Spend per day over the last week, oldest first, today last. */
  byDay: SeedCostDay[]
}

/* ---------------------------------------------------------------------------
 * The week of spend, continued from the figures above.
 *
 * The day series has to agree with three things the report already says: the
 * per-day tile (`totalDay`), the auth-svc row's `+21%` trend, and the swarm's
 * working rhythm. So the values are generated rather than listed:
 *
 *   - today is anchored to `totalDay` exactly, because the tile and the last
 *     bar are one reading said twice;
 *   - three days ago takes the incident multiplier — the auth-svc identity
 *     migration that broke the week's runs and their budget, which the
 *     outcomes series spikes on the same day;
 *   - Saturday and Sunday columns sit well under the weekdays, because fewer
 *     tickets arrive on a weekend — the shape has to look like a real week.
 *
 * The axis comes from `seedDayAxis()` in the run seed, so the weekend columns
 * are the *actual* weekend columns on whichever day the app is opened.
 * ------------------------------------------------------------------------- */

/** Organic variation by days-ago — no real week is a flat line. */
const DAY_FACTOR: Record<number, number> = {
  6: 0.92,
  5: 0.95,
  4: 0.9,
  3: 1.22, // the auth-svc migration day
  2: 0.88,
  1: 0.96,
}

const WEEKEND_FACTOR = 0.66
/** The day the incident story says one is. */
const INCIDENT_DAYS_AGO = 3

function spendByDay(totalDay: number): SeedCostDay[] {
  return seedDayAxis().map((day) => {
    const factor =
      (DAY_FACTOR[day.daysAgo] ?? 1) *
      (day.weekend && day.daysAgo !== 0 && day.daysAgo !== INCIDENT_DAYS_AGO
        ? WEEKEND_FACTOR
        : 1)
    return {
      daysAgo: day.daysAgo,
      weekday: day.weekday,
      weekend: day.weekend,
      spend: Math.round(totalDay * factor * 100) / 100,
    }
  })
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
  byDay: spendByDay(148.2),
}

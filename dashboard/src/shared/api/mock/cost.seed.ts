import { seedDayAxis } from "./runs.seed"

export interface SeedCostByApp {
  app: string
  spend: number
  runs: number
  perSuccess: number
  trend: string
}

export interface SeedCostByModel {
  model: string
  spend: number
  tokens: number
  runs: number
}

export interface SeedCostTopProject {
  projectId: string
  projectKey: string
  projectName: string
  spend: number
  runs: number
  /** Current budget cap (USD). Zero means "no cap". */
  cap: number
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

/**
 * One column of the spend-by-day series for the **active** period.
 *
 * `daysAgo` counts back from today; `weekday` is the three-letter label
 * (`mon`, `sat`, `today`); `weekend` flags Saturday and Sunday so the screen
 * can render weekend columns distinctly from weekday ones.
 */
export interface SeedCostDay {
  daysAgo: number
  weekday: string
  weekend: boolean
  spend: number
}

export type SeedCostPeriod = "day" | "week" | "month"

/**
 * The cost seed, wire-shaped.
 *
 * Holds the active period and all three report shapes (`day` / `week` /
 * `month`) so the query can pick one without re-running the math; the
 * domain mapper turns the active period's slice into `CostSummary`.
 *
 * `seedDayAxis()` from the runs seed stays the source of the weekday labels
 * (Sat / Sun flip day-to-day the same way the runs screen does), so the cost
 * page's last column is "today" whichever day the app is opened.
 */
export interface SeedCostSummary {
  period: SeedCostPeriod
  totalPeriod: number
  totalPreviousPeriod: number
  /** Today's spend, invariant of period — the budget widget's first row. */
  todaySpend: number
  /** Today's cap. */
  todayCap: number
  /** Month-to-date, invariant of period — the budget widget's second row. */
  monthSpend: number
  /** Month's cap. */
  monthCap: number
  perSuccess: number
  successRate: number
  byApp: SeedCostByApp[]
  byModel: SeedCostByModel[]
  topProjects: SeedCostTopProject[]
  budget: SeedCostBudget
  forecast: {
    cap: number
    burnRatePerDay: number
    projectedEndOfPeriod: number
  }
  failures: SeedCostFailure[]
  byDay: SeedCostDay[]
}

/* --------------------------------------------------------------------------
 * Period tables — the seed's source of truth.
 *
 * The numbers are authored to tell three stories at once, not picked at random:
 *
 * - **Total spend grows with the period.** A day costs less than a week costs
 *   less than a month; the figures are pinned so a reader moving between
 *   periods sees the same picture stretched across more days rather than a
 *   different shape.
 *
 * - **The previous period anchors the delta.** A week is ~6% higher than last
 *   week; a month is ~12% higher than last month (one runaway run bumped the
 *   avg); a day is ~3% lower than yesterday (the swarm's been quieter
 *   mornings). The deltas disagree on direction so the toggle is honest.
 *
 * - **Forecast disagrees with budget in both directions.** atlas is under cap
 *   by a comfortable margin (green), comuki is closing in (amber),
 *   prometheus has already crossed (red, with kill-switch at cap). The
 *   forecast widget therefore lands on green/amber/red across three rows,
 *   not on three greens.
 *
 * - **The runway has at least one runaway row.** prometheus' spike is the
 *   runaway; its 12× median is the `anomaly` mappers flip on.
 * ------------------------------------------------------------------------ */

/** Realistic per-model distribution the period reports reuse. */
const MODEL_LINEUP_DAY = {
  "glm-5.2": { spend: 86.2, tokens: 4_650_000, runs: 18 },
  "glm-4.5": { spend: 48.9, tokens: 8_120_000, runs: 41 },
  "MiniMax-M3": { spend: 11.7, tokens: 1_980_000, runs: 12 },
  "claude-3.5-sonnet": { spend: 1.4, tokens: 220_000, runs: 4 },
} as const

const MODEL_LINEUP_WEEK = {
  "glm-5.2": { spend: 422.6, tokens: 21_400_000, runs: 78 },
  "glm-4.5": { spend: 198.3, tokens: 32_900_000, runs: 167 },
  "MiniMax-M3": { spend: 56.4, tokens: 9_120_000, runs: 49 },
  "claude-3.5-sonnet": { spend: 5.9, tokens: 910_000, runs: 16 },
} as const

const MODEL_LINEUP_MONTH = {
  "glm-5.2": { spend: 1842.0, tokens: 91_300_000, runs: 318 },
  "glm-4.5": { spend: 836.5, tokens: 138_600_000, runs: 672 },
  "MiniMax-M3": { spend: 232.7, tokens: 38_400_000, runs: 198 },
  "claude-3.5-sonnet": { spend: 24.8, tokens: 3_820_000, runs: 64 },
} as const

/**
 * Twelve projects, varied spend levels. One is the runaway (prometheus,
 * ~12× median), one is zero-spend (sentinel-vault), the rest sit at low /
 * mid / high / very-high so the top-N ranking has somewhere to land.
 *
 * Project ids keep the `p_` prefix the session seed already uses, so a
 * `projectOf()` lookup for an unseen id still falls back to a key the
 * platform recognises.
 */
const PROJECTS_TABLE = [
  { id: "p_comuki", key: "comuki", name: "Comuki platform", cap: 1100 },
  { id: "p_atlas", key: "atlas", name: "Atlas", cap: 480 },
  { id: "p_plexor", key: "plexor", name: "Plexor", cap: 220 },
  { id: "p_prometheus", key: "prometheus", name: "Prometheus", cap: 90 },
  { id: "p_kafka", key: "kafka", name: "Kafka", cap: 320 },
  { id: "p_meridian", key: "meridian", name: "Meridian", cap: 260 },
  { id: "p_helios", key: "helios", name: "Helios", cap: 180 },
  { id: "p_aurora", key: "aurora", name: "Aurora", cap: 140 },
  { id: "p_nova", key: "nova", name: "Nova", cap: 95 },
  { id: "p_sentinel", key: "sentinel", name: "Sentinel", cap: 60 },
  { id: "p_orbit", key: "orbit", name: "Orbit", cap: 75 },
  { id: "p_quill", key: "quill", name: "Quill", cap: 50 },
  { id: "p_vesta", key: "vesta", name: "Vesta", cap: 35 },
  {
    id: "p_sentinel_vault",
    key: "sentinel-vault",
    name: "Sentinel Vault",
    cap: 0,
  },
] as const

/**
 * Per-day spend for `seedDayAxis()` — organic variation so the chart has a
 * shape and not a flat line. The incident day (`INCIDENT_DAYS_AGO = 3`)
 * carries the spike the runs seed tells the same story about.
 *
 * Multiplied by the active period's `totalDay` so the period's total
 * remains the seed's anchor — the per-day "today" column for `week` and
 * the first 30 days for `month` line up with the headline figure.
 */
const DAY_FACTOR: Record<number, number> = {
  6: 0.92,
  5: 0.95,
  4: 0.9,
  3: 1.22, // the auth-svc migration day
  2: 0.88,
  1: 0.96,
}

const WEEKEND_FACTOR = 0.66
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

/**
 * Thirty days of fake but anchored history for the month view. The last
 * column is "today" and matches the day's anchor; the rest decays backwards
 * with a working-day rhythm and a weekend dip, the same way the week seed
 * does but stretched to a month.
 */
function spendByMonth(totalDay: number): SeedCostDay[] {
  const today = new Date().getDay()
  const out: SeedCostDay[] = []
  for (let daysAgo = 29; daysAgo >= 0; daysAgo -= 1) {
    const weekday = (today - daysAgo + 7 * Math.ceil(daysAgo / 7) + 7) % 7
    const label =
      daysAgo === 0
        ? "today"
        : ["sun", "mon", "tue", "wed", "thu", "fri", "sat"][weekday]
    const weekend = weekday === 0 || weekday === 6
    const factor =
      (DAY_FACTOR[daysAgo] ?? 1) *
      (weekend && daysAgo !== 0 && daysAgo !== INCIDENT_DAYS_AGO
        ? WEEKEND_FACTOR
        : 1)
    out.push({
      daysAgo,
      weekday: label,
      weekend,
      spend: Math.round(totalDay * factor * 100) / 100,
    })
  }
  return out
}

/** One row per project for the chosen period. */
function topProjectsFor(
  totalPeriod: number,
  perPeriodRuns: Record<string, number>
): SeedCostTopProject[] {
  const spreads: Record<string, number> = {
    p_comuki: 0.18,
    p_atlas: 0.13,
    p_plexor: 0.04,
    p_prometheus: 0.21, // the runaway — a fifth of total spend
    p_kafka: 0.09,
    p_meridian: 0.07,
    p_helios: 0.06,
    p_aurora: 0.05,
    p_nova: 0.04,
    p_sentinel: 0.03,
    p_orbit: 0.02,
    p_quill: 0.02,
    p_vesta: 0.01,
    p_sentinel_vault: 0,
  }
  return PROJECTS_TABLE.map((project) => {
    const share = spreads[project.id] ?? 0
    const spend = Math.round(totalPeriod * share * 100) / 100
    return {
      projectId: project.id,
      projectKey: project.key,
      projectName: project.name,
      spend,
      runs: perPeriodRuns[project.id] ?? 0,
      cap: project.cap,
    }
  }).sort((left, right) => right.spend - left.spend)
}

/**
 * Run counts per project, scaled to the chosen period so the top-N table's
 * "runs" column has a defensible value. Smaller periods run fewer jobs.
 */
function projectRunsFor(totalRuns: number): Record<string, number> {
  const factor = totalRuns / 75
  return {
    p_comuki: Math.round(23 * factor),
    p_atlas: Math.round(15 * factor),
    p_plexor: Math.round(5 * factor),
    p_prometheus: Math.round(11 * factor),
    p_kafka: Math.round(8 * factor),
    p_meridian: Math.round(6 * factor),
    p_helios: Math.round(5 * factor),
    p_aurora: Math.round(4 * factor),
    p_nova: Math.round(3 * factor),
    p_sentinel: Math.round(2 * factor),
    p_orbit: Math.round(2 * factor),
    p_quill: Math.round(1 * factor),
    p_vesta: Math.round(1 * factor),
    p_sentinel_vault: 0,
  }
}

/* The period's headline figures, anchored so the toggle re-shapes the picture
 * rather than the picture re-shaping the toggle. */
const TOTAL_DAY = 148.2
const TOTAL_WEEK = 917.8
const TOTAL_MONTH = 3934.5

const PREVIOUS_TOTAL_DAY = 152.7
const PREVIOUS_TOTAL_WEEK = 862.4
const PREVIOUS_TOTAL_MONTH = 3508.0

/* Per-day buckets land the same model spread at three different scales. */
const SPEND_BY_MODEL_DAY: SeedCostByModel[] = Object.entries(
  MODEL_LINEUP_DAY
).map(([model, value]) => ({
  model,
  spend: value.spend,
  tokens: value.tokens,
  runs: value.runs,
}))

const SPEND_BY_MODEL_WEEK: SeedCostByModel[] = Object.entries(
  MODEL_LINEUP_WEEK
).map(([model, value]) => ({
  model,
  spend: value.spend,
  tokens: value.tokens,
  runs: value.runs,
}))

const SPEND_BY_MODEL_MONTH: SeedCostByModel[] = Object.entries(
  MODEL_LINEUP_MONTH
).map(([model, value]) => ({
  model,
  spend: value.spend,
  tokens: value.tokens,
  runs: value.runs,
}))

/** Per-app split, kept stable across periods (the apps don't scale with the
 *  period — they pay per call — so the daily figure × period length). */
const BY_APP_DAY: SeedCostByApp[] = [
  { app: "billing-api", spend: 52.4, runs: 38, perSuccess: 0.41, trend: "+6%" },
  { app: "web-app", spend: 41.1, runs: 51, perSuccess: 0.33, trend: "-3%" },
  { app: "auth-svc", spend: 33.8, runs: 12, perSuccess: 1.12, trend: "+21%" },
  { app: "worker-pool", spend: 14.2, runs: 22, perSuccess: 0.29, trend: "-1%" },
  { app: "docs-site", spend: 6.7, runs: 9, perSuccess: 0.38, trend: "+2%" },
]

const BY_APP_WEEK: SeedCostByApp[] = BY_APP_DAY.map((row) => ({
  ...row,
  spend: Math.round(row.spend * 7 * 0.95 * 100) / 100,
  runs: Math.round(row.runs * 6.8),
  trend: row.trend,
}))

const BY_APP_MONTH: SeedCostByApp[] = BY_APP_DAY.map((row) => ({
  ...row,
  spend: Math.round(row.spend * 30 * 0.92 * 100) / 100,
  runs: Math.round(row.runs * 28),
  trend: row.trend,
}))

/* --------------------------------------------------------------------------
 * The forecast — one figure per period, anchored to the seed's burn rate.
 *
 * A forecast is only honest if it disagrees with the budget in both
 * directions across the visible projects. We therefore give the *whole
 * swarm* a forecast whose cap is well above today's spend — green — even
 * though one of its projects (prometheus) is already over.
 * ------------------------------------------------------------------------ */

const FORECAST_DAY = {
  cap: 220,
  burnRatePerDay: TOTAL_DAY,
  projectedEndOfPeriod: Math.round(TOTAL_DAY * 100) / 100,
}

const FORECAST_WEEK = {
  cap: 1450,
  burnRatePerDay: Math.round((TOTAL_WEEK / 7) * 100) / 100,
  projectedEndOfPeriod: TOTAL_WEEK,
}

const FORECAST_MONTH = {
  cap: 6200,
  burnRatePerDay: Math.round((TOTAL_MONTH / 30) * 100) / 100,
  projectedEndOfPeriod: TOTAL_MONTH,
}

const BUDGET_DAY: SeedCostBudget = { used: TOTAL_DAY, cap: 220 }
const BUDGET_WEEK: SeedCostBudget = { used: TOTAL_WEEK, cap: 1450 }
const BUDGET_MONTH: SeedCostBudget = { used: TOTAL_MONTH, cap: 6200 }

/** Today's spend and cap — the budget widget's first row, period-invariant. */
const TODAY_SPEND = TOTAL_DAY
const TODAY_CAP = 220
/** Month-to-date (the swarm is a few days into the month when the seed runs). */
const MONTH_SPEND = 917.8
const MONTH_CAP = 1450

const FAILURES: SeedCostFailure[] = [
  { profile: "planner", rate: 0.11, note: "types mismatch most often" },
  { profile: "tester", rate: 0.07, note: "flaky e2e on CI" },
  { profile: "implementer", rate: 0.04, note: "escalates to lead" },
]

export const COST_SEED: SeedCostSummary = {
  period: "day",
  totalPeriod: TOTAL_DAY,
  totalPreviousPeriod: PREVIOUS_TOTAL_DAY,
  todaySpend: TODAY_SPEND,
  todayCap: TODAY_CAP,
  monthSpend: MONTH_SPEND,
  monthCap: MONTH_CAP,
  perSuccess: 0.42,
  successRate: 0.86,
  byApp: BY_APP_DAY,
  byModel: SPEND_BY_MODEL_DAY,
  topProjects: topProjectsFor(TOTAL_DAY, projectRunsFor(75)),
  budget: BUDGET_DAY,
  forecast: FORECAST_DAY,
  failures: FAILURES,
  byDay: spendByDay(TOTAL_DAY),
}

/**
 * The three period snapshots. The query picks the one whose `period`
 * matches the active toggle; the mapper flattens it into `CostSummary`.
 *
 * Each snapshot shares the same model/project/app scaffold — they only
 * differ in the period scaling — so a single toggle moves the same
 * picture through three time windows.
 */
export const COST_SEED_BY_PERIOD: Record<
  SeedCostPeriod,
  Omit<SeedCostSummary, "period">
> = {
  day: {
    totalPeriod: TOTAL_DAY,
    totalPreviousPeriod: PREVIOUS_TOTAL_DAY,
    todaySpend: TODAY_SPEND,
    todayCap: TODAY_CAP,
    monthSpend: MONTH_SPEND,
    monthCap: MONTH_CAP,
    perSuccess: 0.42,
    successRate: 0.86,
    byApp: BY_APP_DAY,
    byModel: SPEND_BY_MODEL_DAY,
    topProjects: topProjectsFor(TOTAL_DAY, projectRunsFor(75)),
    budget: BUDGET_DAY,
    forecast: FORECAST_DAY,
    failures: FAILURES,
    byDay: spendByDay(TOTAL_DAY),
  },
  week: {
    totalPeriod: TOTAL_WEEK,
    totalPreviousPeriod: PREVIOUS_TOTAL_WEEK,
    todaySpend: TODAY_SPEND,
    todayCap: TODAY_CAP,
    monthSpend: MONTH_SPEND,
    monthCap: MONTH_CAP,
    perSuccess: 0.39,
    successRate: 0.84,
    byApp: BY_APP_WEEK,
    byModel: SPEND_BY_MODEL_WEEK,
    topProjects: topProjectsFor(TOTAL_WEEK, projectRunsFor(508)),
    budget: BUDGET_WEEK,
    forecast: FORECAST_WEEK,
    failures: FAILURES,
    byDay: spendByDay(TOTAL_DAY),
  },
  month: {
    totalPeriod: TOTAL_MONTH,
    totalPreviousPeriod: PREVIOUS_TOTAL_MONTH,
    todaySpend: TODAY_SPEND,
    todayCap: TODAY_CAP,
    monthSpend: TOTAL_MONTH,
    monthCap: MONTH_CAP * 4.27,
    perSuccess: 0.41,
    successRate: 0.83,
    byApp: BY_APP_MONTH,
    byModel: SPEND_BY_MODEL_MONTH,
    topProjects: topProjectsFor(TOTAL_MONTH, projectRunsFor(2070)),
    budget: BUDGET_MONTH,
    forecast: FORECAST_MONTH,
    failures: FAILURES,
    byDay: spendByMonth(TOTAL_DAY),
  },
}

/** The list of model identifiers the seed authors. */
export const COST_MODEL_LINEUP: readonly string[] =
  Object.keys(MODEL_LINEUP_DAY)

/** The project list the top-N table ranks over — exposed so the page can
 *  keep the filter dropdown and the table in sync without duplicating ids. */
export const COST_PROJECT_KEYS: readonly string[] = PROJECTS_TABLE.map(
  (project) => project.id
)

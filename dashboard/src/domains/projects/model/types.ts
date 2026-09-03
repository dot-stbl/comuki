/** A project as the platform list shows it: the record plus what it is doing. */
export interface ProjectRow {
  id: string
  /** The handle every other list in the product shows. A value, not a name. */
  slug: string
  name: string
  gitProfileRepo: string | null
  createdAt: string
  /** Runs the swarm is standing on for this project right now. */
  activeRuns: number
  /** Every run this shift has seen for it, finished ones included. */
  totalRuns: number
  /**
   * Today's spend, or `null` when nothing has been attributed to the project.
   *
   * `null` rather than `0` because they are different facts: zero is a project
   * that ran and cost nothing, `null` is a project the cost report has never
   * heard of. A row that renders both as `$0.00` is telling the operator that a
   * new project is already accounted for.
   */
  spendToday: number | null
}

export interface CreateProjectInput {
  name: string
  slug: string
  /** `null` when the project runs on the platform's default profiles. */
  gitProfileRepo: string | null
}

/**
 * One project's runtime settings — the knobs the operator reaches for first.
 *
 * The wire carries a numeric `version` for optimistic concurrency: the next PUT
 * has to echo it back, otherwise the host returns 409 (`project.settings_conflict`)
 * and the screen re-reads. The form holds `version` separately from the inputs
 * and refuses to submit when the two diverge; that policy lives in the panel,
 * not here, because the panel is the place that already knows what the user
 * has changed.
 */
export interface ProjectSettings {
  projectId: string
  /** Number of worker containers kept warm when there is no work. */
  minIdle: number
  /** Hard cap on the number of workers running at once. */
  maxConcurrent: number
  /** Seconds before an idle worker is recycled; `null` means the platform default. */
  idleTtlSeconds: number | null
  approveRequired: boolean
  knowledgeEnabled: boolean
  verifyEnabled: boolean
  proxyEnabled: boolean
  /** USD micros (1 USD = 1_000_000) — null when no soft budget is configured. */
  softBudgetUsdMicros: number | null
  /** USD micros — null when no hard budget is configured. */
  hardBudgetUsdMicros: number | null
  /** Optimistic-concurrency token the next PUT must echo. */
  version: number
  /** Last server write; surface only when the panel needs it. */
  updatedAt: string
}

/**
 * One project's cost rollup.
 *
 * USD micros on the wire, USD on the screen — the mapper does the divide. The
 * panel renders `spentUsd` next to `softBudgetUsd` and turns one red when the
 * soft cap is breached and another red when the hard cap is; both flags ride
 * along so the panel does not have to compute them a second time.
 */
export interface ProjectCostSummary {
  projectId: string
  spentUsd: number
  softBudgetUsd: number | null
  hardBudgetUsd: number | null
  softExceeded: boolean
  hardExceeded: boolean
  /** Most recent usage events, newest first — empty when nothing has been attributed. */
  recent: UsageEvent[]
}

/** One row in the cost feed: which run, which model, what it cost. */
export interface UsageEvent {
  id: string
  runId: string | null
  source: string
  model: string
  inputTokens: number
  outputTokens: number
  costUsd: number
  occurredAt: string
}

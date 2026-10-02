/** A project as the platform list shows it: the record plus what it is doing. */
export interface ProjectRow {
  readonly id: string
  /** The handle every other list in the product shows. A value, not a name. */
  readonly slug: string
  readonly name: string
  readonly gitProfileRepo: string | null
  readonly createdAt: string
  /**
   * Whether the host has archived the project. The shared `["projects"]`
   * cache keeps archived rows on purpose — identity names grants against
   * them — while screen-facing hooks filter them out of what they return,
   * so a row the registry still knows is not a row the screens still show.
   */
  readonly archived: boolean
  /** Runs the swarm is standing on for this project right now. */
  readonly activeRuns: number
  /** Every run this shift has seen for it, finished ones included. */
  readonly totalRuns: number
  /**
   * Today's spend, or `null` when nothing has been attributed to the project.
   *
   * `null` rather than `0` because they are different facts: zero is a project
   * that ran and cost nothing, `null` is a project the cost report has never
   * heard of. A row that renders both as `$0.00` is telling the operator that a
   * new project is already accounted for.
   */
  readonly spendToday: number | null
  /**
   * The operator-chosen mark override — a single emoji or an image URL, opaque
   * on the wire. `null` means no override, and the screen derives a brand mark
   * from `gitProfileRepo`'s host instead (`model/identity.ts`).
   */
  readonly icon: string | null
  /**
   * The accent colour, stored and served as lower-case `#rrggbb`. Decoration
   * only: it paints the dot beside the mark and tints the tag chips through the
   * `--project-accent` custom property, never chrome. `null` falls back to the
   * muted ink of the surface.
   */
  readonly color: string | null
  /**
   * What this project *is*, in the operator's own vocabulary — already
   * normalised server-side (lowercase, deduplicated). Always an array on the
   * view; a project with no tags carries `[]`, not `null`.
   */
  readonly tags: readonly string[]
  /**
   * Environment-class id the scalar source repository binds to (catalog id,
   * e.g. `"net10-sdk-bun"`); `null` when the project has no confirmed class.
   * Mirrors `Project.EnvClass` (add-worker-environments 2.2): an empty
   * binding means implement work items for the project are not claimable.
   */
  readonly envClass: string | null
}

export interface CreateProjectInput {
  readonly name: string
  readonly slug: string
  /** `null` when the project runs on the platform's default profiles. */
  readonly gitProfileRepo: string | null
  /** The identity fields — all optional, never a reason to refuse a create. */
  readonly icon: string | null
  readonly color: string | null
  readonly tags: readonly string[]
}

/**
 * The fields a `PATCH /api/v1/projects/{id}` accepts. Mirrors the wire DTO.
 *
 * The identity fields are optional *on the patch*: `icon`/`color` absent means
 * untouched (same as `null`), and `tags` absent means untouched while `[]`
 * clears — the distinction the wire makes for the one list field (design D5).
 */
export interface ProjectUpdate {
  readonly name: string | null
  readonly description: string | null
  readonly icon?: string | null
  readonly color?: string | null
  readonly tags?: readonly string[]
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

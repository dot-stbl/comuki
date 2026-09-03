import type {
  ProjectCostSummary,
  ProjectRow,
  ProjectSettings,
  UsageEvent,
} from "@/domains/projects/model/types"
import type { CreateProjectRequest } from "@/shared/api/_generated/types/CreateProjectRequest"
import type { UpdateSettingsRequest } from "@/shared/api/_generated/types/UpdateSettingsRequest"
import type { SeedProject } from "@/shared/api/mock/projects.seed"

// ---------------------------------------------------------------------------
// Wire → domain mappers (real-backend path).
//
// The kubb-generated response types for the projects endpoints are `any`
// because the host's OpenAPI document does not include response schemas
// for `ProjectView` / `ProjectSettingsView` / `ProjectCostsView` — the C#
// records have no `[ProducesResponseType]` annotations. The shape each
// mapper expects is encoded inline below as a TypeScript `interface` that
// mirrors the C# record property-for-property. When the host grows explicit
// response schemas, kubb will emit typed DTOs and these interfaces become
// dead weight — that is the day to drop them in favour of the kubb types.
//
// The mappers are intentionally tolerant: a wire row missing one of the
// optional fields falls back to a domain default (`null`, `0`, `false`,
// `""`) rather than throwing. The screen renders those defaults honestly
// (dashes, zero, "—") — fabricating values would be a worse lie than
// declaring the field absent.
// ---------------------------------------------------------------------------

/** Wire shape of GET /api/v1/projects — a `ProjectView[]`. */
interface ProjectView {
  id: string
  name: string
  slug: string
  description: string | null
  profilesGitUrl: string | null
  profilesGitRef: string | null
  archived: boolean
  archivedAt: string | null
  createdAt: string
  updatedAt: string
}

/** Wire shape of GET /api/v1/projects/{id}/settings. */
interface ProjectSettingsView {
  projectId: string
  minIdle: number
  maxConcurrent: number
  idleTtlSeconds: number | null
  approveRequired: boolean
  knowledgeEnabled: boolean
  verifyEnabled: boolean
  proxyEnabled: boolean
  softBudgetUsdMicros: number | null
  hardBudgetUsdMicros: number | null
  updatedAt: string
  version: number
}

/** Wire shape of GET /api/v1/projects/{id}/costs. */
interface ProjectCostsView {
  projectId: string
  spentUsdMicros: number
  softLimitUsdMicros: number | null
  hardLimitUsdMicros: number | null
  softExceeded: boolean
  hardExceeded: boolean
  recent: UsageEventView[]
}

/** Wire shape of one entry inside `ProjectCostsView.recent`. */
interface UsageEventView {
  id: string
  runId: string | null
  source: string
  model: string
  inputTokens: number
  outputTokens: number
  costUsdMicros: number
  occurredAt: string
}

const EMPTY_COSTS: UsageEvent[] = []

/**
 * Wire row → domain `ProjectRow`.
 *
 * The derived columns (`activeRuns`, `totalRuns`, `spendToday`) are not
 * carried by `ProjectView`. In real mode they default to the honest
 * "not measured yet" values (zero for counters, `null` for spend) — the
 * screen renders those as dashes, exactly what a project the platform
 * has never heard of looks like. A future slice that joins runs and cost
 * data into the wire row replaces these defaults with real numbers; the
 * mapper signature is stable through that change.
 */
export function mapProjectViewToDetail(view: ProjectView): ProjectRow {
  return {
    id: view.id,
    slug: view.slug,
    name: view.name,
    // The domain treats `gitProfileRepo` as a single handle; the wire splits
    // URL and ref. We carry the URL alone — the form collects a single string,
    // and the wire ref field is empty in every seed. When the platform grows a
    // ref picker, the mapper widens to a `${url}@${ref}` join.
    gitProfileRepo: view.profilesGitUrl,
    createdAt: view.createdAt,
    activeRuns: 0,
    totalRuns: 0,
    spendToday: null,
  }
}

/**
 * Wire list → list of domain rows.
 *
 * The projects list endpoint returns a bare `ProjectView[]` — no paged
 * envelope like the runs endpoint carries. The screen renders the whole
 * list and lets the data table's own filter chip carry narrowing.
 */
export function mapProjectsPageToSummaries(views: ProjectView[]): ProjectRow[] {
  return views.map(mapProjectViewToDetail)
}

/**
 * Wire row → domain `ProjectSettings`.
 *
 * Settings arrive with a `version` the next PUT has to echo back. The mapper
 * carries it through unchanged; the panel reads it from `version` and the
 * `mapProjectSettingsToUpdateRequest` below puts it back into the request.
 */
export function mapProjectSettingsViewToSettings(
  view: ProjectSettingsView
): ProjectSettings {
  return {
    projectId: view.projectId,
    minIdle: view.minIdle,
    maxConcurrent: view.maxConcurrent,
    idleTtlSeconds: view.idleTtlSeconds,
    approveRequired: view.approveRequired,
    knowledgeEnabled: view.knowledgeEnabled,
    verifyEnabled: view.verifyEnabled,
    proxyEnabled: view.proxyEnabled,
    softBudgetUsdMicros: view.softBudgetUsdMicros,
    hardBudgetUsdMicros: view.hardBudgetUsdMicros,
    version: view.version,
    updatedAt: view.updatedAt,
  }
}

/**
 * Domain settings → wire `UpdateSettingsRequest`.
 *
 * The wire uses `int | string` for every numeric field (kubb's loose typing
 * of C# `int`/`long`), so we pass the numbers through and let the host parse
 * them. `idleTtlSeconds` and the budget fields stay `null` when the panel
 * says "platform default" — the host treats those as unset rather than zero.
 */
export function mapProjectSettingsToUpdateRequest(
  settings: ProjectSettings
): UpdateSettingsRequest {
  return {
    version: settings.version,
    minIdle: settings.minIdle,
    maxConcurrent: settings.maxConcurrent,
    idleTtlSeconds: settings.idleTtlSeconds,
    approveRequired: settings.approveRequired,
    knowledgeEnabled: settings.knowledgeEnabled,
    verifyEnabled: settings.verifyEnabled,
    proxyEnabled: settings.proxyEnabled,
    softBudgetUsdMicros: settings.softBudgetUsdMicros,
    hardBudgetUsdMicros: settings.hardBudgetUsdMicros,
  }
}

/**
 * Wire `ProjectCostsView` → domain `ProjectCostSummary`.
 *
 * USD micros on the wire, USD on the screen — we divide once here, and the
 * panel renders the rounded dollar value. The cost feed keeps the order the
 * host returns it (newest first per the handler); the mapper does not
 * reorder, so a `slice(0, 50)` upstream and an unfiltered `slice()` here
 * give the panel a stable ordering.
 */
export function mapCostsPageToCostSummary(
  view: ProjectCostsView
): ProjectCostSummary {
  const recent = Array.isArray(view.recent)
    ? view.recent.map(mapUsageEvent)
    : EMPTY_COSTS

  return {
    projectId: view.projectId,
    spentUsd: microsToUsd(view.spentUsdMicros),
    softBudgetUsd: numberOrNull(view.softLimitUsdMicros, microsToUsd),
    hardBudgetUsd: numberOrNull(view.hardLimitUsdMicros, microsToUsd),
    softExceeded: view.softExceeded,
    hardExceeded: view.hardExceeded,
    recent,
  }
}

function mapUsageEvent(view: UsageEventView): UsageEvent {
  return {
    id: view.id,
    runId: view.runId,
    source: view.source,
    model: view.model,
    inputTokens: view.inputTokens,
    outputTokens: view.outputTokens,
    costUsd: microsToUsd(view.costUsdMicros),
    occurredAt: view.occurredAt,
  }
}

function microsToUsd(micros: number): number {
  return micros / 1_000_000
}

function numberOrNull(
  value: number | null | undefined,
  map: (input: number) => number
): number | null {
  return value == null ? null : map(value)
}

// ---------------------------------------------------------------------------
// Mock-first mappers (unchanged from pre-wire behaviour).
//
// The operator's `dev:mock` flow and the Storybook need the same shape
// `useProjectsQuery` returns, so the mock path maps the `SeedProject` +
// joined runs/cost rows through `buildProjectRows` and returns that.
// No domain shape drift between mock and real modes — the screen branches
// on `env.useMock`, not on the return type.
// ---------------------------------------------------------------------------

/** Wire-free seed row → domain `ProjectRow`. Derived columns default to zero / null. */
export function toProjectRow(seed: SeedProject): ProjectRow {
  return {
    id: seed.id,
    slug: seed.slug,
    name: seed.name,
    gitProfileRepo: seed.gitProfileRepo,
    createdAt: seed.createdAt,
    activeRuns: 0,
    totalRuns: 0,
    spendToday: null,
  }
}

/**
 * Form input → wire `CreateProjectRequest`.
 *
 * The form collects `name`, `slug` and `gitProfileRepo`; the host's create
 * contract wants URL and ref separately, plus a `description` we do not
 * collect. `description` and `profilesGitRef` are sent as `null` — the
 * form has no field for them, and the host treats their absence as
 * "not configured" rather than as an error.
 */
export function mapCreateProjectInputToCreateRequest(
  input: CreateProjectInputLike
): CreateProjectRequest {
  return {
    name: input.name,
    slug: input.slug,
    description: null,
    profilesGitUrl: input.gitProfileRepo,
    profilesGitRef: null,
  }
}

/** The form's domain input — re-declared here to avoid pulling in `model/types`. */
interface CreateProjectInputLike {
  name: string
  slug: string
  gitProfileRepo: string | null
}

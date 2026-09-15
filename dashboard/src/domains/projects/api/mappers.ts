import type {
  ProjectCostSummary,
  ProjectRow,
  ProjectSettings,
  UsageEvent,
} from "@/domains/projects/model/types"
import type { CreateProjectRequest } from "@/shared/api/_generated/types/CreateProjectRequest"
import type { ProjectCostsView } from "@/shared/api/_generated/types/ProjectCostsView"
import type { UpdateSettingsRequest } from "@/shared/api/_generated/types/UpdateSettingsRequest"
import type { UsageEventView } from "@/shared/api/_generated/types/UsageEventView"
import type { SeedProject } from "@/shared/api/mock/projects.seed"
import type { ProjectRef } from "@/shared/session"

// ---------------------------------------------------------------------------
// Wire → domain mappers (real-backend path).
//
// The costs view is typed by the kubb-generated `ProjectCostsView` (the
// spec now declares the response schema). The projects/settings reads are
// still `any`-free local claims below — their endpoints carry no response
// schemas yet; the day they grow them, the same swap happens there.
//
// The spec types counters as `number | string` (the serializer may read
// numbers from strings) and the ids the host wraps in typed records
// (`ProjectId`, `RunId`) as `{ value: string }` objects — both are
// normalised at this edge. The mappers stay intentionally tolerant: a wire
// row missing one of the optional fields falls back to a domain default
// (`null`, `0`, `false`, `""`) rather than throwing. The screen renders
// those defaults honestly (dashes, zero, "—") — fabricating values would
// be a worse lie than declaring the field absent.
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
    archived: view.archived,
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
 * Cached registry rows → the session's `ProjectRef[]`.
 *
 * The projection the session hook runs over the shared `["projects"]`
 * cache: id, the key the operator calls the project by (the row's
 * `slug`), and the name. The session hands these to every permission
 * sentence and every project pick on the product; nothing heavier
 * belongs in a context that lives above the query cache's project
 * screens. Archived rows are the caller's concern — the session hook
 * filters them out before mapping.
 */
export function mapProjectRowsToProjectRefs(
  rows: ProjectRow[]
): ProjectRef[] {
  return rows.map((row) => ({
    id: row.id,
    key: row.slug,
    name: row.name,
  }))
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
    // Domain type admission landed in the BE (ProjectDomainType + Custom JSON
    // map) but the FE settings panel does not yet expose it as a control.
    // Pass the platform default — the panel keeps working until the BE
    // surfaces a domain-type control and we wire it through here.
    domainType: "Standard",
    customDomainTypesJson: null,
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
    projectId: view.projectId.value ?? "",
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
    runId: view.runId?.value ?? null,
    source: view.source,
    model: view.model,
    inputTokens: Number(view.inputTokens),
    outputTokens: Number(view.outputTokens),
    costUsd: microsToUsd(view.costUsdMicros),
    occurredAt: view.occurredAt,
  }
}

function microsToUsd(micros: number | string): number {
  return Number(micros) / 1_000_000
}

function numberOrNull(
  value: number | string | null | undefined,
  map: (input: number | string) => number
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
    archived: false,
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

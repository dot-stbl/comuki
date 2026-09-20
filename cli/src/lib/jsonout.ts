/**
 * Pure JSON mappers for `comuki status|runs|whoami --json`.
 * Shape is a 1:1 projection of the existing wire views — no fetching,
 * no Ink, no ANSI. Callers stringify with `JSON.stringify(…, null, 2)`.
 */
import type {
  ComputeSnapshotView,
  KnowledgeDocumentsPageView,
  MeView,
  ProjectView,
  RunsPageView,
  RunView,
} from "./client"
import type { WhoAmI } from "./auth"

export interface StatusJsonError {
  compute?: string
  projects?: string
  knowledge?: string
  runs?: string
}

export interface StatusJsonInput {
  readonly who: WhoAmI
  readonly compute?: ComputeSnapshotView
  readonly projects?: readonly ProjectView[]
  readonly knowledge?: KnowledgeDocumentsPageView
  readonly runs?: RunsPageView
  readonly errors?: StatusJsonError
}

export interface StatusJson {
  readonly identity: { readonly kind: WhoAmI["kind"]; readonly label: string }
  readonly compute: {
    readonly provider: string
    readonly queued: number
    readonly running: number
  } | null
  readonly projects: { readonly count: number } | null
  readonly knowledge: {
    readonly documents: number
    readonly chunks: number
  } | null
  readonly runs: {
    readonly total: number
    readonly byStatus: Readonly<Record<string, number>>
  } | null
  readonly errors: StatusJsonError
}

export function mapStatusJson(input: StatusJsonInput): StatusJson {
  const compute = input.compute
  const knowledge = input.knowledge
  const runs = input.runs
  const byStatus: Record<string, number> = {}
  if (runs) {
    for (const run of runs.items) {
      byStatus[run.status] = (byStatus[run.status] ?? 0) + 1
    }
  }
  return {
    identity: { kind: input.who.kind, label: input.who.label },
    compute: compute
      ? {
          provider: compute.provider,
          queued: compute.pools.reduce((sum, pool) => sum + pool.queued, 0),
          running: compute.pools.reduce((sum, pool) => sum + pool.running, 0),
        }
      : null,
    projects: input.projects ? { count: input.projects.length } : null,
    knowledge: knowledge
      ? {
          documents: knowledge.total,
          chunks: knowledge.items.reduce(
            (sum, document) => sum + document.chunkCount,
            0
          ),
        }
      : null,
    runs: runs ? { total: runs.total, byStatus } : null,
    errors: input.errors ?? {},
  }
}

export interface RunJson {
  readonly id: string
  readonly status: string
  readonly projectId: string
  readonly project: string | null
  readonly createdAt: string
  readonly updatedAt: string
}

export interface RunsPageJson {
  readonly page: number
  readonly pageSize: number
  readonly total: number
  readonly items: readonly RunJson[]
}

export function mapRunJson(
  run: RunView,
  projectSlug: string | null = null
): RunJson {
  return {
    id: run.id,
    status: run.status,
    projectId: run.projectId,
    project: projectSlug,
    createdAt: run.createdAt,
    updatedAt: run.updatedAt,
  }
}

export function mapRunsPageJson(
  page: RunsPageView,
  names: ReadonlyMap<string, string> = new Map()
): RunsPageJson {
  return {
    page: page.page,
    pageSize: page.pageSize,
    total: page.total,
    items: page.items.map((run) =>
      mapRunJson(run, names.get(run.projectId) ?? null)
    ),
  }
}

export interface WhoamiJson {
  readonly kind: WhoAmI["kind"]
  readonly label: string
  readonly userId: string | null
  readonly subjectType: string | null
  readonly subjectId: string | null
  readonly email: string | null
  readonly displayName: string | null
  readonly roles: readonly string[]
  readonly permissions: readonly string[]
}

export function mapWhoamiJson(who: WhoAmI, me: MeView | null): WhoamiJson {
  return {
    kind: who.kind,
    label: who.label,
    userId: me?.userId ?? null,
    subjectType: me?.subjectType ?? null,
    subjectId: me?.subjectId ?? null,
    email: me?.email ?? null,
    displayName: me?.displayName ?? null,
    roles: me?.roles ?? [],
    permissions: me?.permissions ?? [],
  }
}

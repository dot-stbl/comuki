/**
 * Pure ops-pack formatting for the chat REPL (`/runs`, `/workers`,
 * `/plan`, `/project`): the pinned live runs panel, the background
 * workers panel, the session plan view, and the project-context
 * notices. Wire shapes come from `lib/client.ts`; rendering follows
 * the `lib/format.ts` contract — spacing + the deck's status colours,
 * no frames (the approve card stays the transcript's only framed
 * element). Run ids and worker names render as OSC 8 hyperlinks when
 * a dashboard url is supplied — terminals without OSC 8 show the
 * bare label.
 *
 * Degrade note: the runs list wire (`GET /api/v1/runs`) carries no
 * worker/profile column — only id/projectId/status/timestamps — so the
 * panel's middle column shows the project slug, same as
 * `comuki runs list`.
 */
import type { ChatBlock } from "./sessions"
import type {
  BackgroundWorkerView,
  ProjectView,
  RunsPageView,
} from "./client"
import { linkSequence } from "./term"
import {
  ageFromIso,
  ageFromMs,
  extractPlanNodes,
  renderPlanItems,
  stepWord,
  tableRow,
  truncateTail,
} from "./format"
import { colors, paint, symbols } from "../theme"

/** Rows the /runs panel shows — one page of the newest runs. */
export const RUNS_FEED_PAGE_SIZE = 10

/**
 * Auto-refresh cadence of the pinned panel while its session is the
 * active one; the interval is torn down on panel clear / tab switch /
 * unmount (see the effect in `commands/chat.tsx`).
 */
export const RUNS_FEED_REFRESH_MS = 60_000

/** One rendered feed row — ages recompute at render time from `updatedAt`. */
export interface RunsFeedRow {
  readonly id: string
  /** Project slug (or short project id when unresolvable) — worker/profile is not on the wire. */
  readonly project: string
  readonly status: string
  readonly updatedAt: string
  /** Dashboard link target — the id cell renders as an OSC 8 hyperlink when set. */
  readonly url?: string
}

/** The pinned panel's state, patched into the session like `pendingPlan`. */
export interface RunsFeedPanel {
  readonly rows: readonly RunsFeedRow[]
  readonly total: number
  /** Epoch ms of the last successful fetch — the header's "as of" age. */
  readonly fetchedAt: number
  /** Last refresh failure; the panel keeps its rows and shows this note. */
  readonly refreshError: string | null
}

/**
 * Maps a runs page + project names to the panel's render rows; a
 * dashboard url (the config host) turns each row's id into a
 * `{url}/runs/{id}` hyperlink target. Pure.
 */
export function runsFeedRows(
  page: RunsPageView,
  names: ReadonlyMap<string, string>,
  dashboardUrl?: string
): RunsFeedRow[] {
  const base = dashboardUrl?.replace(/\/+$/, "")
  return page.items.map((run) => ({
    id: run.id,
    project: names.get(run.projectId) ?? run.projectId.slice(0, 8),
    status: run.status,
    updatedAt: run.updatedAt,
    ...(base ? { url: `${base}/runs/${run.id}` } : {}),
  }))
}

/**
 * Status band colour — running pops accent, success reads lavender,
 * failure yellow, waiting its dim gold, queued stays faint (colour
 * never carries status alone: the word always rides beside it).
 */
export function runStatusColor(status: string): string {
  const lowered = status.toLowerCase()
  if (lowered === "running" || lowered === "busy") {
    return colors.accent
  }
  if (["succeeded", "completed", "success", "replied"].includes(lowered)) {
    return colors.ok
  }
  if (["failed", "cancelled", "escalated"].includes(lowered)) {
    return colors.error
  }
  if (["waiting", "awaiting_approval", "idle"].includes(lowered)) {
    return colors.waiting
  }
  if (["queued", "pending"].includes(lowered)) {
    return colors.faint
  }
  return colors.muted
}

const FEED_COLUMNS = [
  { width: 15 },
  { width: 11 },
  { width: 17 },
  { width: 0 },
] as const

function feedColumnLine(labels: readonly string[], color: string): string {
  return paint(
    tableRow(labels.map((text, index) => ({ text, width: FEED_COLUMNS[index].width }))),
    color
  )
}

/**
 * The panel as finished transcript lines: `⏺ runs` header with the
 * shown/total + as-of age, a faint column line, the fixed-column rows
 * with their status bands, and a footer note (refresh cadence, or the
 * last refresh failure in failure yellow).
 */
export function renderRunsFeedPanel(
  panel: RunsFeedPanel,
  now: Date = new Date()
): string[] {
  const asOf = ageFromMs(Math.max(0, now.getTime() - panel.fetchedAt))
  const header = `  ${paint(symbols.event, colors.dim)} ${paint("runs", colors.muted)} ${paint(
    `· ${panel.rows.length} of ${panel.total} · ${asOf} ago`,
    colors.dim
  )}`
  const columnLine = `  ${feedColumnLine(["id", "status", "project", "age"], colors.faint)}`
  if (panel.rows.length === 0 && panel.refreshError !== null) {
    return [
      header,
      `  ${paint(`${symbols.cross} ${panel.refreshError}`, colors.error)}`,
    ]
  }
  const rows =
    panel.rows.length === 0
      ? [`  ${paint("no runs yet", colors.faint)}`]
      : panel.rows.map((row) => {
          const idCell = paint(row.id.slice(0, 13), colors.muted)
          return `  ${tableRow([
            {
              text: row.url ? linkSequence(row.url, idCell) : idCell,
              width: FEED_COLUMNS[0].width,
            },
            {
              text: paint(row.status.padEnd(10), runStatusColor(row.status)),
              width: FEED_COLUMNS[1].width,
            },
            {
              text: paint(truncateTail(row.project, 16), colors.dim),
              width: FEED_COLUMNS[2].width,
            },
            {
              text: paint(ageFromIso(row.updatedAt, now), colors.faint),
              width: FEED_COLUMNS[3].width,
            },
          ])}`
        })
  const footer =
    panel.refreshError !== null
      ? `  ${paint(`${symbols.cross} ${panel.refreshError}`, colors.error)}`
      : `  ${paint(
          `${symbols.bullet} auto-refresh ${RUNS_FEED_REFRESH_MS / 1000}s ${symbols.bullet} /runs refreshes now`,
          colors.faint
        )}`
  return [header, columnLine, ...rows, footer]
}

/**
 * Refresh merge: a failed poll keeps the previous rows (only the
 * as-of stamp and the error note move); any successful poll returns
 * the fresh panel wholesale.
 */
export function mergeRunsFeedRefresh(
  previous: RunsFeedPanel | null | undefined,
  next: RunsFeedPanel
): RunsFeedPanel {
  if (
    next.refreshError !== null &&
    previous != null &&
    previous.rows.length > 0
  ) {
    return {
      ...previous,
      fetchedAt: next.fetchedAt,
      refreshError: next.refreshError,
    }
  }
  return next
}

// ---------------------------------------------------------------------------
// /workers — the host's background worker registry
// ---------------------------------------------------------------------------

/** The three states the panel distinguishes; the word always rides the color. */
export type WorkerStatusWord = "running" | "degraded" | "stopped"

/**
 * Heuristic over the registry snapshot: consecutive failures read as
 * `degraded` first (the loudest signal), a scheduled next cycle reads
 * as `running`, and everything finished or in-flight (startup workers
 * after their single run, a cycle between stamps) reads as `stopped`.
 */
export function workerStatusWord(worker: BackgroundWorkerView): WorkerStatusWord {
  if (!worker.isHealthy || worker.consecutiveFailures > 0) {
    return "degraded"
  }
  if (worker.nextRunAt !== null) {
    return "running"
  }
  return "stopped"
}

/** Dichromat band: running reads lavender-ok, degraded waiting-yellow, stopped dim. */
export function workerStatusColor(word: WorkerStatusWord): string {
  if (word === "running") {
    return colors.ok
  }
  if (word === "degraded") {
    return colors.waiting
  }
  return colors.dim
}

const WORKERS_COLUMNS = [{ width: 19 }, { width: 11 }, { width: 0 }] as const

/**
 * The `/workers` panel as finished transcript lines — one shot per
 * invocation, no polling. Names link to the dashboard root via OSC 8
 * when `dashboardUrl` is supplied (the wire carries no per-worker run
 * ids, so there is nothing deeper to link); `last-run` shows the age
 * of the last cycle start, or `never` before the first one.
 */
export function renderWorkersPanel(
  workers: readonly BackgroundWorkerView[],
  dashboardUrl?: string,
  now: Date = new Date()
): string[] {
  const base = dashboardUrl?.replace(/\/+$/, "")
  const header = `  ${paint(symbols.event, colors.dim)} ${paint(
    "workers",
    colors.muted
  )} ${paint(`· ${workers.length}`, colors.dim)}`
  if (workers.length === 0) {
    return [header, `  ${paint("no background workers", colors.faint)}`]
  }
  const columnLine = `  ${paint(
    tableRow([
      { text: "name", width: WORKERS_COLUMNS[0].width },
      { text: "status", width: WORKERS_COLUMNS[1].width },
      { text: "last-run", width: WORKERS_COLUMNS[2].width },
    ]),
    colors.faint
  )}`
  const rows = workers.map((worker) => {
    const word = workerStatusWord(worker)
    const nameCell = paint(worker.name.slice(0, 18), colors.muted)
    const lastRun =
      worker.lastRunAt !== null
        ? ageFromIso(worker.lastRunAt, now)
        : "never"
    return `  ${tableRow([
      {
        text: base ? linkSequence(base, nameCell) : nameCell,
        width: WORKERS_COLUMNS[0].width,
      },
      {
        text: paint(word.padEnd(10), workerStatusColor(word)),
        width: WORKERS_COLUMNS[1].width,
      },
      {
        text: paint(lastRun, colors.faint),
        width: WORKERS_COLUMNS[2].width,
      },
    ])}`
  })
  const footer = `  ${paint(
    `${symbols.bullet} one-shot ${symbols.bullet} /workers refreshes`,
    colors.faint
  )}`
  return [header, columnLine, ...rows, footer]
}

// ---------------------------------------------------------------------------
// /plan — the active session's plan
// ---------------------------------------------------------------------------

function planHeader(detail: string): string {
  return `  ${paint(symbols.event, colors.dim)} ${paint("plan", colors.muted)} ${paint(
    `· ${detail}`,
    colors.dim
  )}`
}

/**
 * The session's plan as transcript lines: the pending approval plan
 * first (it is the live decision), else the newest plan part carried
 * by the transcript, else the dim no-plan notice. The wire carries no
 * per-step status — steps render with profile, brief and deps only.
 */
export function planPanelLines(
  blocks: readonly ChatBlock[],
  pendingPlan: unknown
): string[] {
  const pending = extractPlanNodes(pendingPlan)
  if (pending.length > 0) {
    return [
      planHeader(`${pending.length} ${stepWord(pending.length)} · awaiting approval`),
      ...renderPlanItems(pending),
    ]
  }
  for (let blockIndex = blocks.length - 1; blockIndex >= 0; blockIndex--) {
    const block = blocks[blockIndex]
    if (block.kind !== "message" || block.message.parts === null) {
      continue
    }
    const parts = block.message.parts
    for (let partIndex = parts.length - 1; partIndex >= 0; partIndex--) {
      const part = parts[partIndex]
      if (part.kind === "plan") {
        return [
          planHeader(`${part.nodes.length} ${stepWord(part.nodes.length)}`),
          ...renderPlanItems(part.nodes),
        ]
      }
    }
  }
  return [`  ${paint("no plan in this session", colors.faint)}`]
}

// ---------------------------------------------------------------------------
// /project — project context switch
// ---------------------------------------------------------------------------

/**
 * Resolves a project by id, slug or name (case-insensitive, exact
 * match). Pure — the caller owns the fetch and the context update.
 */
export function resolveProject(
  query: string,
  projects: readonly ProjectView[]
): ProjectView | undefined {
  const wanted = query.trim().toLowerCase()
  if (wanted.length === 0) {
    return undefined
  }
  return (
    projects.find((project) => project.id.toLowerCase() === wanted) ??
    projects.find((project) => project.slug.toLowerCase() === wanted) ??
    projects.find((project) => project.name.toLowerCase() === wanted)
  )
}

/** `/project` without args: the current context + every visible project. */
export function projectListingLines(
  current: string | null,
  projects: readonly ProjectView[]
): string[] {
  const header = `  ${paint(symbols.event, colors.dim)} ${paint("project", colors.muted)} ${paint(
    `· current: ${current ?? "none"}`,
    colors.dim
  )}`
  if (projects.length === 0) {
    return [header, `  ${paint("no projects visible", colors.faint)}`]
  }
  return [
    header,
    ...projects.map(
      (project) =>
        `  ${paint(`${symbols.bullet} ${project.slug} — ${project.name}`, colors.faint)}`
    ),
  ]
}

/** The context-switch notice: `⏺ project → {label}`. */
export function projectSwitchedLine(label: string): string {
  return `  ${paint(`${symbols.event} project ${symbols.arrow} ${label}`, colors.faint)}`
}

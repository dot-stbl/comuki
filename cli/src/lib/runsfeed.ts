/**
 * Pure ops-pack formatting for the chat REPL (`/runs`, `/plan`,
 * `/project`): the pinned live runs panel, the session plan view, and
 * the project-context notices. Wire shapes come from `lib/client.ts`;
 * rendering follows the `lib/format.ts` contract — spacing + the deck's
 * status colours, no frames (the approve card stays the transcript's
 * only framed element).
 *
 * Degrade note: the runs list wire (`GET /api/v1/runs`) carries no
 * worker/profile column — only id/projectId/status/timestamps — so the
 * panel's middle column shows the project slug, same as
 * `comuki runs list`.
 */
import type { ChatBlock } from "./sessions"
import type { ProjectView, RunsPageView } from "./client"
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

/** Maps a runs page + project names to the panel's render rows. Pure. */
export function runsFeedRows(
  page: RunsPageView,
  names: ReadonlyMap<string, string>
): RunsFeedRow[] {
  return page.items.map((run) => ({
    id: run.id,
    project: names.get(run.projectId) ?? run.projectId.slice(0, 8),
    status: run.status,
    updatedAt: run.updatedAt,
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
      : panel.rows.map(
          (row) =>
            `  ${tableRow([
              {
                text: paint(row.id.slice(0, 13), colors.muted),
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
        )
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

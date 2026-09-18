/**
 * Pure snapshot of `comuki status` / REPL `/status`: one platform
 * picture as finished transcript lines. Each source fails independently
 * (403 on one permission must not blank the rest); a failed line prints
 * its reason dimmed instead. Wire shapes come from `lib/client.ts`;
 * rendering follows the `lib/format.ts` contract — spacing + the deck's
 * status colours, no frames.
 */
import type {
  ComputeSnapshotView,
  HealthView,
  KnowledgeDocumentsPageView,
  ProjectView,
  RunsPageView,
} from "./client"
import { colors, paint, symbols } from "../theme"

const fmt = (value: number): string => value.toLocaleString("en-US")

const HOT_RUN_STATUSES = [
  "queued",
  "running",
  "escalated",
  "awaiting_approval",
] as const

/** One settled source of the snapshot — either a painted line or a dim failure. */
export type StatusLine =
  | { readonly ok: true; readonly text: string }
  | { readonly ok: false; readonly label: string; readonly reason: string }

/** The four (plus health) sources `/status` fetches in parallel. */
export interface StatusSnapshot {
  readonly health: PromiseSettledResult<HealthView>
  readonly compute: PromiseSettledResult<ComputeSnapshotView>
  readonly projects: PromiseSettledResult<readonly ProjectView[]>
  readonly knowledge: PromiseSettledResult<KnowledgeDocumentsPageView>
  readonly runs: PromiseSettledResult<RunsPageView>
}

/** One `comuki status` fetch against a live client — never throws. */
export async function fetchStatusSnapshot(client: {
  health(): Promise<HealthView>
  compute(): Promise<ComputeSnapshotView>
  projects(): Promise<readonly ProjectView[]>
  knowledgeDocuments(
    page: number,
    pageSize: number
  ): Promise<KnowledgeDocumentsPageView>
  runs(page: number, pageSize: number): Promise<RunsPageView>
}): Promise<StatusSnapshot> {
  const [health, compute, projects, knowledge, runs] = await Promise.allSettled([
    client.health(),
    client.compute(),
    client.projects(),
    client.knowledgeDocuments(1, 100),
    client.runs(1, 100),
  ])
  return { health, compute, projects, knowledge, runs }
}

/** Header of the REPL panel: `⏺ status`. */
function statusHeader(): string {
  return `  ${paint(symbols.event, colors.dim)} ${paint("status", colors.muted)}`
}

/**
 * The snapshot as finished transcript lines. Health leads (the host is
 * either answering or not); the four original `comuki status` sources
 * follow in the same order the argv command prints them.
 */
export function renderStatusPanel(snapshot: StatusSnapshot): string[] {
  return [
    statusHeader(),
    ...statusLines(snapshot).map((line) => `  ${renderStatusLine(line)}`),
  ]
}

/** The argv command's body — no header, no extra gutter (Ink adds `"  "`). */
export function statusLines(snapshot: StatusSnapshot): StatusLine[] {
  return [
    healthLine(snapshot.health),
    computeLine(snapshot.compute),
    projectsLine(snapshot.projects),
    knowledgeLine(snapshot.knowledge),
    runsLine(snapshot.runs),
  ]
}

/**
 * One argv/Ink row: ok text, or `label: reason` dimmed. No leading
 * gutter — `comuki status` wraps each row in `<Text>{"  "}…</Text>`;
 * the REPL panel adds the two-space gutter itself via
 * `renderStatusPanel`.
 */
export function renderStatusLine(line: StatusLine): string {
  if (line.ok) {
    return line.text
  }
  return paint(`${line.label}: ${line.reason}`, colors.dim)
}

function healthLine(result: PromiseSettledResult<HealthView>): StatusLine {
  if (result.status === "fulfilled") {
    const word = result.value.status
    const color = word === "ok" ? colors.ok : colors.waiting
    return {
      ok: true,
      text: `${paint("health:", colors.dim)} ${paint(word, color)}`,
    }
  }
  return { ok: false, label: "health", reason: describeStatusError(result.reason) }
}

function computeLine(
  result: PromiseSettledResult<ComputeSnapshotView>
): StatusLine {
  if (result.status === "fulfilled") {
    const snapshot = result.value
    const queued = snapshot.pools.reduce((sum, pool) => sum + pool.queued, 0)
    const running = snapshot.pools.reduce((sum, pool) => sum + pool.running, 0)
    return {
      ok: true,
      text: `${paint("provider:", colors.dim)} ${snapshot.provider}${paint(
        `  ·  queue: ${queued} queued ${symbols.bullet} ${running} running`,
        colors.dim
      )}`,
    }
  }
  return {
    ok: false,
    label: "provider",
    reason: describeStatusError(result.reason),
  }
}

function projectsLine(
  result: PromiseSettledResult<readonly ProjectView[]>
): StatusLine {
  if (result.status === "fulfilled") {
    return {
      ok: true,
      text: `${paint("projects:", colors.dim)} ${result.value.length} active`,
    }
  }
  return {
    ok: false,
    label: "projects",
    reason: describeStatusError(result.reason),
  }
}

function knowledgeLine(
  result: PromiseSettledResult<KnowledgeDocumentsPageView>
): StatusLine {
  if (result.status === "fulfilled") {
    const page = result.value
    const chunks = page.items.reduce(
      (sum, document) => sum + document.chunkCount,
      0
    )
    const note =
      page.total > page.items.length
        ? paint(
            ` (+${fmt(page.total - page.items.length)} more, first page only)`,
            colors.dim
          )
        : ""
    return {
      ok: true,
      text: `${paint("knowledge:", colors.dim)} ${fmt(page.total)} documents ${symbols.bullet} ${fmt(chunks)} chunks${note}`,
    }
  }
  return {
    ok: false,
    label: "knowledge",
    reason: describeStatusError(result.reason),
  }
}

function runsLine(result: PromiseSettledResult<RunsPageView>): StatusLine {
  if (result.status === "fulfilled") {
    const page = result.value
    const byStatus = new Map<string, number>()
    for (const run of page.items) {
      byStatus.set(run.status, (byStatus.get(run.status) ?? 0) + 1)
    }
    const hot = HOT_RUN_STATUSES.map((status) => ({
      status,
      count: byStatus.get(status) ?? 0,
    }))
      .filter((entry) => entry.count > 0)
      .map((entry) => `${entry.count} ${entry.status}`)
      .join(paint(` ${symbols.bullet} `, colors.dim))
    return {
      ok: true,
      text: `${paint("runs:", colors.dim)} ${fmt(page.total)} total${
        hot ? paint(`  ·  `, colors.dim) + hot : ""
      }`,
    }
  }
  return { ok: false, label: "runs", reason: describeStatusError(result.reason) }
}

/**
 * Compact reason for a failed source. Mirrors `describeError` in
 * `commands/chat.tsx` without importing it (that module is the Ink
 * app — pulling it into a pure formatter would cycle).
 */
export function describeStatusError(error: unknown): string {
  if (
    error instanceof Error &&
    "status" in error &&
    typeof (error as { status: unknown }).status === "number"
  ) {
    const apiError = error as {
      status: number
      code?: string
      detail?: string
    }
    return `HTTP ${apiError.status}${apiError.code ? ` (${apiError.code})` : ""}: ${apiError.detail ?? "request failed"}`
  }
  const message = error instanceof Error ? error.message : String(error)
  if (
    /unable to connect|fetch failed|econnrefused|connection refused/i.test(
      message
    )
  ) {
    return "server unreachable"
  }
  return message
}

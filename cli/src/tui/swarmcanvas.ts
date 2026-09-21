/**
 * Swarm canvas renderer (issue #78).
 *
 * Pure line-builder: a `SwarmSummary` + i18n + width + an optional
 * "inspected row id" → the styled lines the host draws in the
 * canvas pane. No OpenTUI imports — the renderable lives in the
 * host (`./host.ts`); this module is the pure seam.
 *
 * The layout mirrors the focus-mode discipline: one header per
 * priority section, one summary line per row, optional detail
 * block under the inspected row. Empty swarm — `summaryIsEmpty`
 * returns `true` — produces no lines at all (the host shows
 * nothing in the right pane; focus mode stays quiet).
 *
 * Reuses `lineText` / `seg` from `./styled.ts`. No new OpenTUI
 * primitives introduced (issue #78 §"swarmcanvas.ts").
 */

import { TUI_NAMESPACE, tr, type I18nInstance } from "../locales"
import {
  blankLine,
  lineLength,
  seg,
  truncateTail,
  type Segment,
  type StyledLine,
} from "./styled"
import {
  attachFailure,
  attachPlanNodes,
  deriveSwarmSummary,
  findSwarmRow,
  summaryIsEmpty,
  type SwarmRow,
  type SwarmSummary,
} from "./swarm"
import type { AttentionSignal } from "../kernel/attention"
import { pendingApprovalEntry, type ApprovalEntry } from "./approvals"
import { approvalDetail, approvalSummary } from "./styled"
import type { HarnessSession } from "../harness/state"

// ---------------------------------------------------------------------------
// Render context — passed by the host on every render
// ---------------------------------------------------------------------------

export interface SwarmCanvasContext {
  readonly i18n: I18nInstance
  readonly width: number
  /**
   * The currently inspected row id (worker id or correlation id).
   * When set, the matching row's detail block renders expanded.
   * The host wires this to the palette's `swarm-canvas-inspect`
   * payload.
   */
  readonly inspectedId: string | null
  /**
   * Live sessions keyed by id — the canvas reads the active session
   * for plan-node data on awaiting-approval rows. Sessions absent
   * from the map render their detail block without plan-node info.
   */
  readonly sessions: ReadonlyMap<string, HarnessSession>
}

// ---------------------------------------------------------------------------
// Public surface — the host wires this on every snapshot commit
// ---------------------------------------------------------------------------

/**
 * Compose the canvas surface for the latest attention signal.
 *
 * When the swarm is empty (only P2 workers, or no remote sessions at
 * all) the function returns `[]` — the host shows nothing in the
 * right pane and the focus-mode default holds.
 *
 * The host pre-computes `pendingApprovalEntry` for the inspected row
 * (when applicable) to attach stacked plan nodes to the detail block.
 */
export function renderSwarmCanvas(
  signal: AttentionSignal,
  context: SwarmCanvasContext
): readonly StyledLine[] {
  const labels = reasonLabels(context.i18n)
  const summary: SwarmSummary = deriveSwarmSummary(signal, labels)
  // Focus-mode quiet case: no P0/P1 surfaces and only P2 (running)
  // workers carry the signal. The canvas stays empty — running
  // workers do not deserve their own row by themselves.
  if (
    summary.p0.length === 0 &&
    summary.p1.length === 0 &&
    summary.p2.length > 0
  ) {
    return []
  }
  if (summaryIsEmpty(summary)) {
    return []
  }

  const inspected = inspectedRow(summary, context)
  const enriched = enrichRow(inspected, context)
  const summaryWithInspect = enriched === null
    ? summary
    : replaceRow(summary, enriched)

  const lines: StyledLine[] = []
  lines.push(canvasHeader(context.i18n))
  lines.push(blankLine())

  // P0 first — the user's top priority.
  if (summaryWithInspect.p0.length > 0) {
    lines.push(sectionHeader(context.i18n, "p0"))
    for (const row of summaryWithInspect.p0) {
      lines.push(...rowLines(row, context))
      if (isInspected(row, context)) {
        lines.push(...detailLines(row, context))
      }
    }
    lines.push(blankLine())
  }

  // P1 — only when something demands the user's review.
  if (summaryWithInspect.p1.length > 0) {
    lines.push(sectionHeader(context.i18n, "p1"))
    for (const row of summaryWithInspect.p1) {
      lines.push(...rowLines(row, context))
      if (isInspected(row, context)) {
        lines.push(...detailLines(row, context))
      }
    }
    lines.push(blankLine())
  }

  // P2 — only renders alongside P0/P1 (the canvas is a quiet-mode
  // surface; running workers do not deserve their own row by
  // themselves). Focus-mode default: a P2-only swarm is silent.
  if (
    summaryWithInspect.p2.length > 0 &&
    (summaryWithInspect.p0.length > 0 || summaryWithInspect.p1.length > 0)
  ) {
    lines.push(sectionHeader(context.i18n, "p2"))
    for (const row of summaryWithInspect.p2) {
      lines.push(...rowLines(row, context))
      if (isInspected(row, context)) {
        lines.push(...detailLines(row, context))
      }
    }
    lines.push(blankLine())
  }

  // Trim trailing blanks — the host stacks the canvas in a flex
  // pane and trailing blanks just push the composer out of view.
  while (lines.length > 0 && isBlankLine(lines[lines.length - 1]!)) {
    lines.pop()
  }
  return lines
}

// ---------------------------------------------------------------------------
// Rendered lines — section header, row, detail
// ---------------------------------------------------------------------------

function canvasHeader(i18n: I18nInstance): StyledLine {
  return [
    seg("  ", "faint"),
    seg(tr(i18n, "transcript.swarm.canvasHeader"), "muted", { bold: true }),
  ]
}

function sectionHeader(i18n: I18nInstance, priority: "p0" | "p1" | "p2"): StyledLine {
  const labelKey = `transcript.swarm.priority.${priority}`
  const tone = priority === "p0" ? "error" : priority === "p1" ? "waiting" : "muted"
  return [
    seg("  ", "faint"),
    seg(tr(i18n, labelKey), tone, { bold: true }),
    seg("  ", "faint"),
    seg(tr(i18n, "transcript.swarm.sectionDiverging"), "faint"),
  ]
}

function rowLines(row: SwarmRow, context: SwarmCanvasContext): StyledLine[] {
  const segments: Segment[] = [
    seg("  · ", "faint"),
    seg(priorityTag(row.priority), priorityTone(row.priority), { bold: true }),
    seg("  ", "faint"),
  ]
  segments.push(seg(row.headline, "text"))
  if (row.badge.length > 0) {
    segments.push(seg("  ", "faint"))
    segments.push(seg(row.badge, badgeTone(row.priority), { bold: true }))
  }
  if (row.correlationId.length > 0) {
    segments.push(seg("  ", "faint"))
    segments.push(seg(`#${row.correlationId}`, "faint"))
  }
  if (row.secondary.length > 0) {
    segments.push(seg("  — ", "muted"))
    segments.push(seg(row.secondary, "muted"))
  }
  const line: StyledLine = truncateRowLine(segments, context.width)
  return [line]
}

function detailLines(row: SwarmRow, context: SwarmCanvasContext): StyledLine[] {
  const detail = row.detail
  if (detail === null) {
    return []
  }
  const lines: StyledLine[] = []
  const indent = "    "
  const width = Math.max(8, context.width - indent.length)
  lines.push([
    seg(indent, "faint"),
    seg(tr(context.i18n, "transcript.swarm.detailInspect"), "accent"),
    seg("  ", "faint"),
    seg(row.correlationId, "muted"),
  ])
  switch (detail.kind) {
    case "awaiting-approval": {
      lines.push([
        seg(indent, "faint"),
        seg(detail.scope, "muted"),
      ])
      for (const node of detail.planNodes) {
        lines.push([
          seg(`${indent}  `, "faint"),
          seg(node.id, "faint"),
          seg("  ", "faint"),
          seg(node.profile, "text", { bold: true }),
          seg("  →  ", "faint"),
          seg(node.brief, "muted"),
        ])
      }
      break
    }
    case "stalled": {
      const label = interpolate(context.i18n, "transcript.swarm.leaseHint", {
        seconds: detail.seconds,
      })
      lines.push([
        seg(indent, "faint"),
        seg(label, "error"),
      ])
      break
    }
    case "evidence": {
      lines.push([
        seg(indent, "faint"),
        seg(detail.hint, "muted"),
      ])
      break
    }
    case "failed": {
      const code = detail.code.length > 0 ? detail.code : "error"
      const label = interpolate(context.i18n, "transcript.swarm.failureHint", {
        code,
      })
      lines.push([
        seg(indent, "faint"),
        seg(label, "error"),
      ])
      if (detail.message.length > 0) {
        lines.push([
          seg(indent, "faint"),
          seg(detail.message, "muted"),
        ])
      }
      break
    }
    case "active":
      break
  }
  // Cap detail length — narrow viewports truncate per-line.
  return lines.map((line) =>
    lineLength(line) > width ? truncateLine(line, width) : line
  )
}

/**
 * Truncate a styled line (already composed) to `width` columns.
 * Collapses to two segments: the head segment keeps its tone, the
 * tail carries the truncated rest.
 */
function truncateLine(line: StyledLine, width: number): StyledLine {
  if (line.length === 0) {
    return [seg(" ", "text")]
  }
  const head = line[0]!
  const tailText = truncateTail(
    line.map((segment) => segment.text).join(""),
    Math.max(1, width - head.text.length)
  )
  return [seg(head.text, head.tone, head.style, head.atomic), seg(tailText, head.tone)]
}

// ---------------------------------------------------------------------------
// Inspected-row enrichment
// ---------------------------------------------------------------------------

/** Find the row that matches the inspected id, or `null`. */
function inspectedRow(summary: SwarmSummary, context: SwarmCanvasContext): SwarmRow | null {
  if (context.inspectedId === null) {
    return null
  }
  return findSwarmRow(summary, context.inspectedId)
}

/**
 * Attach live detail data to the inspected row — plan nodes from the
 * pending approval entry, the failed turn's code/message, etc.
 */
function enrichRow(row: SwarmRow | null, context: SwarmCanvasContext): SwarmRow | null {
  if (row === null) {
    return null
  }
  const session = context.sessions.get(row.sessionId)
  if (session === undefined) {
    return row
  }
  if (row.reason === "awaiting-approval") {
    const approval = pendingApprovalEntry({
      session,
      messageId: row.correlationId,
      requester: row.title,
      createdAtUnixMs: 0,
      trailingMeta: null,
    })
    if (approval === null) {
      return row
    }
    return attachPlanNodes(
      row,
      planNodesFrom(approval).map((node) => ({
        id: node.id,
        profile: node.profileKey,
        brief: node.brief,
      }))
    )
  }
  if (row.reason === "failed" && session.turn.kind === "failed") {
    return attachFailure(row, session.turn.error.code, session.turn.error.message)
  }
  return row
}

/** Extract the plan-node rows from an `ApprovalEntry` for the detail block. */
function planNodesFrom(approval: ApprovalEntry): readonly { id: string; profileKey: string; brief: string }[] {
  const action = approval.pendingAction
  if (action.kind === "stacked") {
    return action.nodes.map((node) => ({
      id: node.id,
      profileKey: node.profileKey,
      brief: node.brief,
    }))
  }
  return []
}

/** Replace the row inside the summary by id. Pure — returns a new summary. */
function replaceRow(summary: SwarmSummary, replacement: SwarmRow): SwarmSummary {
  const replace = (rows: readonly SwarmRow[]): readonly SwarmRow[] =>
    rows.map((row) => (row.sessionId === replacement.sessionId ? replacement : row))
  return {
    ...summary,
    p0: replace(summary.p0),
    p1: replace(summary.p1),
    p2: replace(summary.p2),
  }
}

function isInspected(row: SwarmRow, context: SwarmCanvasContext): boolean {
  return (
    context.inspectedId !== null &&
    (row.sessionId === context.inspectedId || row.correlationId === context.inspectedId)
  )
}

// ---------------------------------------------------------------------------
// Local helpers — i18n, clipping, tone
// ---------------------------------------------------------------------------

function reasonLabels(i18n: I18nInstance): {
  readonly awaiting: string
  readonly stalled: string
  readonly evidence: string
  readonly failed: string
  readonly active: string
} {
  return {
    awaiting: tr(i18n, "transcript.swarm.awaiting"),
    stalled: tr(i18n, "transcript.swarm.stalled"),
    evidence: tr(i18n, "transcript.swarm.evidence"),
    failed: tr(i18n, "transcript.swarm.failed"),
    active: tr(i18n, "transcript.swarm.active"),
  }
}

/** `tr` with interpolation — same loud-miss contract as `entries.ts`. */
function interpolate(
  i18n: I18nInstance,
  key: string,
  params: Record<string, string | number>
): string {
  const value = i18n.t(key, { ns: TUI_NAMESPACE, ...params })
  if (typeof value === "string" && value.length > 0 && value !== key) {
    return value
  }
  throw new Error(
    `i18n: missing or empty key '${key}' in locale '${i18n.language}' (namespace '${TUI_NAMESPACE}')`
  )
}

function priorityTag(priority: "p0" | "p1" | "p2"): string {
  return priority.toUpperCase()
}

function priorityTone(priority: "p0" | "p1" | "p2"): "error" | "waiting" | "muted" {
  if (priority === "p0") {
    return "error"
  }
  if (priority === "p1") {
    return "waiting"
  }
  return "muted"
}

function badgeTone(priority: "p0" | "p1" | "p2"): "error" | "waiting" | "muted" {
  return priorityTone(priority)
}

/**
 * Compose a one-line row from its segments, truncating to `width`
 * columns when narrow. Returns a `StyledLine` (a flat segment
 * array) so the host can render each segment with its own tone — a
 * single combined segment would erase priority / badge coloring on
 * narrow terminals.
 */
function truncateRowLine(segments: readonly Segment[], width: number): StyledLine {
  if (segments.length === 0) {
    return [seg(" ", "text")]
  }
  const total = segments.reduce((sum, segment) => sum + segment.text.length, 0)
  if (total <= width) {
    return [...segments]
  }
  const head = segments[0]
  const headTone = head?.tone ?? "text"
  const headText = head?.text ?? ""
  const budget = Math.max(1, width - headText.length)
  const tailText = truncateTail(segments.map((segment) => segment.text).join(""), budget)
  return [seg(headText, headTone), seg(tailText, headTone)]
}

function isBlankLine(line: StyledLine): boolean {
  return line.every((segment) => segment.text.trim().length === 0)
}

// Re-exports so a host can read everything from a single import.
export {
  attachFailure,
  attachPlanNodes,
  deriveSwarmSummary,
  findSwarmRow,
  summaryIsEmpty,
  type SwarmRow,
  type SwarmRowDetail,
  type SwarmSummary,
} from "./swarm"

// The approvalDetail + approvalSummary re-exports keep the canvas's
// dependency surface obvious: a host upgrading to stacked approvals
// sees the same imports the canvas already uses.
export { approvalDetail, approvalSummary }
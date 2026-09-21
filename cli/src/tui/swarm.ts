/**
 * Swarm summary — the attention-first view-model for the canvas
 * (issue #78).
 *
 * Pure data derived from the latest `AttentionSignal`. The signal
 * already classifies every remote session into P0/P1/P2 with a
 * stable `AttentionItem` shape; the summary splits the items into
 * one section per priority bucket and adds a stable per-row rationale
 * the canvas can render.
 *
 * Focus mode is the default: when the only signal is P2, the summary
 * is `isEmpty() === true` and the canvas renders nothing. P0 always
 * wins the user's attention — even a single stalled or awaiting worker
 * becomes the topmost row.
 *
 * The summary carries the per-row context the canvas needs to compose
 * the drill-down detail block: the rationale, the pending scope
 * (when awaiting), the lease hint (when stalled), the failure code
 * (when failed), and the correlation id the palette's
 * `swarm-canvas-inspect <id>` deep-links to.
 */

import type {
  AttentionItem,
  AttentionPriority,
  AttentionReason,
  AttentionSignal,
} from "../kernel/attention"

/**
 * The full canvas surface — three buckets, ordered top to bottom.
 * `items` inside a bucket follow the same order the kernel produced
 * (oldest reason first) so the topmost row is the most stale.
 */
export interface SwarmSummary {
  readonly revision: number
  readonly generatedAtUnixMs: number
  readonly nowUnixMs: number
  /** P0 rows — awaiting approval or stalled. The user's top priority. */
  readonly p0: readonly SwarmRow[]
  /** P1 rows — needs review (failed, evidence unread). */
  readonly p1: readonly SwarmRow[]
  /** P2 rows — running, no anomaly. */
  readonly p2: readonly SwarmRow[]
}

/** True when no row surfaces in any bucket — focus mode stays quiet. */
export function summaryIsEmpty(summary: SwarmSummary): boolean {
  return summary.p0.length === 0 && summary.p1.length === 0 && summary.p2.length === 0
}

/**
 * One row in the swarm canvas — the row-level view-model. The canvas
 * reads `headline` + `secondary` + `badge` for the summary line and
 * the `detail` block when the user opens it via inspect / hover.
 */
export interface SwarmRow {
  /** The session id this row refers to. */
  readonly sessionId: string
  /** Priority bucket — preserved so the canvas can label the section. */
  readonly priority: AttentionPriority
  /** Stable reason — drives `badge` + `detail.kind`. */
  readonly reason: AttentionReason
  /**
   * Primary text: profile · title + (duration seconds) — the
   * 60-ish character summary line the canvas leads with.
   */
  readonly headline: string
  /** Worker profile (e.g. `planId`, `impl`) the row prefixes. */
  readonly profile: string
  /** Session title verbatim — used by the headline. */
  readonly title: string
  /**
   * Human-readable duration string (e.g. `92s`). When zero or
   * unknown, an empty string — the canvas omits the suffix.
   */
  readonly durationLabel: string
  /**
   * Stable correlation id — the in-flight turn request id for
   * thinking/failed, the session id for awaiting. The palette's
   * `swarm-canvas-inspect <id>` reads this verbatim.
   */
  readonly correlationId: string
  /**
   * Badge label the row carries next to the headline — short,
   * already-localized (the renderer reads it as-is).
   */
  readonly badge: string
  /**
   * Secondary text shown after the headline — a second clause the
   * canvas renders muted. Most rows have one; some have none.
   */
  readonly secondary: string
  /**
   * Drill-down detail block — what the canvas shows when the user
   * inspects this row. `null` for active (P2) rows where the
   * headline is already the full story.
   */
  readonly detail: SwarmRowDetail | null
}

export type SwarmRowDetail =
  | {
      readonly kind: "awaiting-approval"
      readonly scope: string
      readonly planNodes: readonly { readonly id: string; readonly profile: string; readonly brief: string }[]
    }
  | {
      readonly kind: "stalled"
      readonly seconds: number
      readonly reason: string
    }
  | {
      readonly kind: "evidence"
      readonly hint: string
    }
  | {
      readonly kind: "failed"
      readonly code: string
      readonly message: string
    }
  | { readonly kind: "active" }

/**
 * Derive the canvas surface from a fresh `AttentionSignal`. Pure —
 * no listeners, no side effects.
 *
 * When `labels` is omitted, the row carries raw enum reasons in the
 * `badge` slot so tests can assert without depending on i18n. The
 * production canvas passes the localized labels (one set per locale).
 */
export function deriveSwarmSummary(
  signal: AttentionSignal,
  labels?: {
    readonly awaiting?: string
    readonly stalled?: string
    readonly evidence?: string
    readonly failed?: string
    readonly active?: string
  }
): SwarmSummary {
  const awaiting = labels?.awaiting ?? "awaiting-approval"
  const stalled = labels?.stalled ?? "stalled"
  const evidence = labels?.evidence ?? "evidence"
  const failed = labels?.failed ?? "failed"
  const active = labels?.active ?? "active"

  const p0: SwarmRow[] = []
  const p1: SwarmRow[] = []
  const p2: SwarmRow[] = []

  for (const item of signal.items) {
    const row = rowFromItem(item, { awaiting, stalled, evidence, failed, active })
    switch (row.priority) {
      case "p0":
        p0.push(row)
        break
      case "p1":
        p1.push(row)
        break
      case "p2":
        p2.push(row)
        break
    }
  }

  return {
    revision: signal.revision,
    generatedAtUnixMs: signal.generatedAtUnixMs,
    nowUnixMs: signal.nowUnixMs,
    p0,
    p1,
    p2,
  }
}

interface ReasonLabels {
  readonly awaiting: string
  readonly stalled: string
  readonly evidence: string
  readonly failed: string
  readonly active: string
}

function rowFromItem(item: AttentionItem, labels: ReasonLabels): SwarmRow {
  const seconds = Math.max(0, Math.round(item.durationMs / 1000))
  const durationLabel = seconds > 0 ? `${seconds}s` : ""
  const baseHeadline = buildHeadline(item.profile, item.title, durationLabel)

  switch (item.reason) {
    case "awaiting-approval":
      return {
        sessionId: item.sessionId,
        priority: item.priority,
        reason: item.reason,
        headline: baseHeadline,
        profile: item.profile,
        title: item.title,
        durationLabel,
        correlationId: item.correlationId,
        badge: labels.awaiting,
        secondary: item.rationale,
        detail: {
          kind: "awaiting-approval",
          scope: item.rationale,
          planNodes: [],
        },
      }
    case "stalled":
      return {
        sessionId: item.sessionId,
        priority: item.priority,
        reason: item.reason,
        headline: baseHeadline,
        profile: item.profile,
        title: item.title,
        durationLabel,
        correlationId: item.correlationId,
        badge: labels.stalled,
        secondary: item.rationale,
        detail: {
          kind: "stalled",
          seconds,
          reason: item.rationale,
        },
      }
    case "evidence":
      return {
        sessionId: item.sessionId,
        priority: item.priority,
        reason: item.reason,
        headline: baseHeadline,
        profile: item.profile,
        title: item.title,
        durationLabel,
        correlationId: item.correlationId,
        badge: labels.evidence,
        secondary: item.rationale,
        detail: {
          kind: "evidence",
          hint: item.rationale,
        },
      }
    case "failed":
      return {
        sessionId: item.sessionId,
        priority: item.priority,
        reason: item.reason,
        headline: baseHeadline,
        profile: item.profile,
        title: item.title,
        durationLabel,
        correlationId: item.correlationId,
        badge: labels.failed,
        secondary: item.rationale,
        detail: {
          kind: "failed",
          code: "",
          message: item.rationale,
        },
      }
    case "active":
      return {
        sessionId: item.sessionId,
        priority: item.priority,
        reason: item.reason,
        headline: baseHeadline,
        profile: item.profile,
        title: item.title,
        durationLabel,
        correlationId: item.correlationId,
        badge: labels.active,
        secondary: "",
        detail: { kind: "active" },
      }
  }
}

/** Compose the summary line: `<profile> · <title> (<duration>)`. */
function buildHeadline(profile: string, title: string, durationLabel: string): string {
  const head = title.length > 0 ? title : "(untitled)"
  const profilePrefix = profile.length > 0 && profile !== head ? `${profile} · ` : ""
  const durationSuffix = durationLabel.length > 0 ? ` (${durationLabel})` : ""
  return `${profilePrefix}${head}${durationSuffix}`
}

/**
 * Find the row matching a deep-link id (worker id or correlation id).
 * Used by `palette run swarm-canvas-inspect <id>` to highlight a row
 * in the open canvas. Returns `null` when nothing matches.
 */
export function findSwarmRow(
  summary: SwarmSummary,
  id: string
): SwarmRow | null {
  const all: readonly SwarmRow[] = [...summary.p0, ...summary.p1, ...summary.p2]
  for (const row of all) {
    if (row.sessionId === id || row.correlationId === id) {
      return row
    }
  }
  return null
}

/**
 * Merge plan-node data into an awaiting-approval row's detail block.
 * The pure `deriveSwarmSummary` does not have access to the live
 * pendingPlan payload (the source already filtered it out); the
 * canvas calls this once per render to attach per-node info.
 */
export function attachPlanNodes(
  row: SwarmRow,
  planNodes: readonly { readonly id: string; readonly profile: string; readonly brief: string }[]
): SwarmRow {
  if (row.detail?.kind !== "awaiting-approval") {
    return row
  }
  return {
    ...row,
    detail: {
      kind: "awaiting-approval",
      scope: row.detail.scope,
      planNodes,
    },
  }
}

/**
 * Merge a `failed` turn's `error.code` + `message` into the detail
 * block. The view-model sees the rationale but the harness error
 * fields live in the session; the canvas wires them through here.
 */
export function attachFailure(
  row: SwarmRow,
  code: string,
  message: string
): SwarmRow {
  if (row.detail?.kind !== "failed") {
    return row
  }
  return {
    ...row,
    detail: {
      kind: "failed",
      code,
      message,
    },
  }
}
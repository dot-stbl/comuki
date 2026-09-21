/**
 * Pure render-model for the OpenTUI host: `HarnessState` (kernel
 * snapshots) + an i18n instance → strings the renderables draw. No
 * OpenTUI imports here — this module is trivially testable and the
 * host (`./host.ts`) owns every renderable.
 *
 * The kernel is the single source of truth: transcript messages, the
 * thinking turn's live text, failed turns as alert lines and the
 * awaiting-approval card all derive from `HarnessState` alone.
 */

import { activeSession } from "../harness/selectors"
import type {
  HarnessSession,
  HarnessState,
  SessionId,
} from "../harness/state"
import { tr, type I18nInstance } from "../locales"
import {
  buildTranscriptEntries,
  entryLines,
  type EntryRenderContext,
} from "./entries"
import {
  lineText,
  uniformLine,
  type StyledLine,
} from "./styled"

/**
 * The decoded approval card. `pendingPlan` on the wire is `unknown`;
 * this model accepts both the platform plan payload
 * (`{ nodes: [{ key, brief, … }], estimateMinutes }`) and the simpler
 * `{ planSteps: string[] }` shape. Fields the payload does not carry
 * stay `null` and the card omits their rows.
 */
export interface ApprovalCardModel {
  readonly intent: string | null
  readonly scope: string | null
  readonly risk: string | null
  readonly steps: readonly string[]
  readonly diff: readonly string[]
  readonly estimateMinutes: number | null
}

export function approvalCardModel(plan: unknown): ApprovalCardModel {
  const empty: ApprovalCardModel = {
    intent: null,
    scope: null,
    risk: null,
    steps: [],
    diff: [],
    estimateMinutes: null,
  }
  if (plan === null || typeof plan !== "object") {
    return empty
  }
  const record = plan as Record<string, unknown>
  const estimate =
    typeof record.estimateMinutes === "number" && record.estimateMinutes > 0
      ? Math.round(record.estimateMinutes)
      : null
  return {
    intent: optionalString(record.intent),
    scope: optionalString(record.scope),
    risk: optionalString(record.risk),
    steps: planSteps(record),
    diff: planDiff(record),
    estimateMinutes: estimate,
  }
}

function optionalString(value: unknown): string | null {
  return typeof value === "string" && value.trim().length > 0
    ? value.trim()
    : null
}

function planSteps(record: Record<string, unknown>): readonly string[] {
  if (Array.isArray(record.planSteps)) {
    const steps = record.planSteps.filter(
      (step): step is string => typeof step === "string" && step.length > 0
    )
    if (steps.length > 0) {
      return steps
    }
  }
  // The platform wire shape: nodes carry key/brief (brief falls back
  // to title server-side; here we read both defensively).
  if (Array.isArray(record.nodes)) {
    const steps: string[] = []
    for (const node of record.nodes) {
      if (node === null || typeof node !== "object") {
        continue
      }
      const entry = node as Record<string, unknown>
      const key = optionalString(entry.key) ?? optionalString(entry.id)
      const brief = optionalString(entry.brief) ?? optionalString(entry.title)
      if (key === null && brief === null) {
        continue
      }
      steps.push(brief !== null ? brief : (key as string))
    }
    if (steps.length > 0) {
      return steps
    }
  }
  return []
}

function planDiff(record: Record<string, unknown>): readonly string[] {
  const diff = optionalString(record.diff)
  return diff === null ? [] : diff.split("\n")
}

/**
 * Connection label for the top bar. `short` picks the compact variant
 * used below the compact-layout width threshold.
 */
export function connectionLabel(
  state: HarnessState,
  i18n: I18nInstance,
  short = false
): string {
  switch (state.connection.kind) {
    case "connected":
      return tr(i18n, short ? "connection.connectedShort" : "connection.connected")
    case "connecting":
      return tr(i18n, short ? "connection.connectingShort" : "connection.connecting")
    case "reconnecting":
      return tr(i18n, short ? "connection.reconnectingShort" : "connection.reconnecting")
    case "disconnected":
      return tr(i18n, short ? "connection.disconnectedShort" : "connection.disconnected")
  }
}

/**
 * Issue #77 — the coarse reconnect badge for the top bar. Lives
 * alongside the existing connection label so the user sees both:
 * the harness's fine-grained state (connecting / connected / ...) and
 * the orchestrator's coarse gate (online / recovering / offline).
 *
 * Returns `null` when the state is `online` and the badge would be
 * noise.
 */
export function reconnectBadge(
  online: boolean,
  i18n: I18nInstance
): string | null {
  if (online) {
    return null
  }
  // The orchestrator only exposes three states; we surface recovering
  // and offline here. The harness reducer separately tracks the
  // fine-grained `connection` field — `topBarContent` shows both.
  return tr(i18n, "transcript.session.home.recovering")
}

/** Top-bar chrome string for the active layout mode. */
export function topBarContent(
  state: HarnessState,
  session: HarnessSession | null,
  i18n: I18nInstance,
  compact: boolean,
  online: boolean = true
): string {
  const badge = reconnectBadge(online, i18n)
  const badgePart = badge !== null ? ` [${badge}]` : ""
  if (compact) {
    return `${tr(i18n, "chrome.titleCompact")}·${connectionLabel(state, i18n, true)}${badgePart}`
  }
  const title = session !== null && session.title.length > 0 ? ` · ${session.title}` : ""
  return `${tr(i18n, "chrome.title")} · ${connectionLabel(state, i18n)}${badgePart}${title}`
}

/**
 * Transcript lines rendered into the sticky-bottom viewport. Pure:
 * `session + i18n + width + expanded-entry ids` in, styled lines out.
 *
 * - transcript entries render per kind: message echoes keep the role
 *   prefix, assistant markdown renders through the styled renderer,
 *   collapsible entries (thinking/tools/code/diff/plan) collapse to a
 *   summary line unless their id sits in `expanded`;
 * - a thinking turn appends its live text as an uncollapsed entry;
 * - a failed turn appends one alert line with the error message;
 * - `null` session renders the empty-session hint.
 */
export function buildTranscriptStyledLines(
  session: HarnessSession | null,
  i18n: I18nInstance,
  width: number,
  expanded: ReadonlySet<string>
): readonly StyledLine[] {
  if (session === null) {
    // The welcome block from `buildHomeLines` already explains the
    // empty state — don't add a second competing hint here.
    return []
  }
  const context: EntryRenderContext = { i18n, width, expanded }
  const lines: StyledLine[] = []
  for (const entry of buildTranscriptEntries(session)) {
    lines.push(...entryLines(entry, context))
  }
  if (session.turn.kind === "failed") {
    lines.push(
      uniformLine(
        `${tr(i18n, "transcript.failed")} ${session.turn.error.message}`,
        "error"
      )
    )
  }
  return lines
}

/**
 * Plain-text transcript — the byte-identical flat view of the styled
 * pipeline (collapsed by default). Tests and callers that don't care
 * about tones read this.
 */
export function buildTranscriptLines(
  session: HarnessSession | null,
  i18n: I18nInstance,
  width: number
): readonly string[] {
  return buildTranscriptStyledLines(session, i18n, width, new Set()).map(lineText)
}

/** The card model for the active session's awaiting-approval turn. */
export function activeApprovalCard(
  state: HarnessState
): { readonly sessionId: SessionId; readonly card: ApprovalCardModel } | null {
  const session = activeSession(state)
  if (
    session === null ||
    session.identity.kind !== "remote" ||
    session.turn.kind !== "awaiting-approval"
  ) {
    return null
  }
  return {
    sessionId: session.identity.id,
    card: approvalCardModel(session.pendingPlan),
  }
}

/**
 * The queued-follow-up indicator line for the active session, or
 * `null` when the queue is empty. Pure: the kernel's per-session
 * queue (turns submitted while a turn was in flight) in, one line
 * out — `⧗ queued: <first 20 chars>` plus a `(+N more)` suffix when
 * more turns sit behind the head.
 */
export function queuedFollowUpLine(
  session: HarnessSession | null,
  i18n: I18nInstance
): string | null {
  if (session === null || session.queue.length === 0) {
    return null
  }
  const head = session.queue[0]!.message
  const preview = head.length > 20 ? `${head.slice(0, 20)}…` : head
  const rest = session.queue.length - 1
  const more =
    rest > 0
      ? ` (+${rest} ${tr(i18n, "queue.moreSuffix")})`
      : ""
  return `⧗ ${tr(i18n, "queue.prefix")} ${preview}${more}`
}

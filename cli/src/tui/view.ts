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
  TurnState,
} from "../harness/state"
import { tr, type I18nInstance } from "../locales"

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

/** Top-bar chrome string for the active layout mode. */
export function topBarContent(
  state: HarnessState,
  session: HarnessSession | null,
  i18n: I18nInstance,
  compact: boolean
): string {
  if (compact) {
    return `${tr(i18n, "chrome.titleCompact")}·${connectionLabel(state, i18n, true)}`
  }
  const title = session !== null && session.title.length > 0 ? ` · ${session.title}` : ""
  return `${tr(i18n, "chrome.title")} · ${connectionLabel(state, i18n)}${title}`
}

/**
 * Transcript lines rendered into the sticky-bottom viewport. Pure:
 * `session + i18n + width` in, lines out.
 *
 * - transcript messages get a role prefix, continuation lines indent;
 * - a thinking turn appends its live text under a "thinking" prefix;
 * - a failed turn appends one alert line with the error message;
 * - `null` session renders the empty-session hint.
 */
export function buildTranscriptLines(
  session: HarnessSession | null,
  i18n: I18nInstance,
  width: number
): readonly string[] {
  if (session === null) {
    return [tr(i18n, "transcript.emptySession")]
  }
  const lines: string[] = []
  for (const message of session.transcript) {
    pushPrefixed(lines, rolePrefix(message.role, i18n), message.content, width)
  }
  pushTurnLines(lines, session.turn, i18n, width)
  return lines
}

function pushTurnLines(
  lines: string[],
  turn: TurnState,
  i18n: I18nInstance,
  width: number
): void {
  switch (turn.kind) {
    case "thinking": {
      const prefix = tr(i18n, "transcript.thinking")
      if (turn.accumulatedText.length > 0) {
        pushPrefixed(lines, prefix, turn.accumulatedText, width)
      } else {
        lines.push(prefix)
      }
      return
    }
    case "failed": {
      lines.push(`${tr(i18n, "transcript.failed")} ${turn.error.message}`)
      return
    }
    case "idle":
    case "awaiting-approval":
      return
  }
}

function rolePrefix(
  role: HarnessSession["transcript"][number]["role"],
  i18n: I18nInstance
): string {
  switch (role) {
    case "user":
      return tr(i18n, "transcript.you")
    case "assistant":
      return tr(i18n, "transcript.comuki")
    case "system":
      return tr(i18n, "transcript.system")
    case "tool":
      return tr(i18n, "transcript.tool")
  }
}

/**
 * `prefix first-line` + indented wrapped continuation. Existing
 * newlines split first; long segments word-wrap at `width`.
 */
function pushPrefixed(
  lines: string[],
  prefix: string,
  content: string,
  width: number
): void {
  const room = Math.max(8, width - prefix.length - 1)
  // Word-wrap each paragraph; the first wrapped line carries the
  // prefix, the rest indent under it.
  let isFirstLine = true
  for (const paragraph of content.split("\n")) {
    const pieces = wrapText(paragraph, room)
    if (pieces.length === 0) {
      lines.push(isFirstLine ? `${prefix} ` : "")
      isFirstLine = false
      continue
    }
    for (const piece of pieces) {
      lines.push(isFirstLine ? `${prefix} ${piece}` : `${" ".repeat(prefix.length + 1)}${piece}`)
      isFirstLine = false
    }
  }
}

/** Word-wrap `text` to `width`; empty text wraps to nothing. */
export function wrapText(text: string, width: number): readonly string[] {
  if (text.length === 0) {
    return []
  }
  const words = text.split(/\s+/).filter((word) => word.length > 0)
  if (words.length === 0) {
    return []
  }
  const lines: string[] = []
  let current = ""
  for (const word of words) {
    // A single word longer than the width hard-splits by characters.
    let token = word
    while (token.length > width) {
      if (current.length > 0) {
        lines.push(current)
        current = ""
      }
      lines.push(token.slice(0, width))
      token = token.slice(width)
    }
    if (current.length === 0) {
      current = token
      continue
    }
    if (current.length + 1 + token.length <= width) {
      current = `${current} ${token}`
      continue
    }
    lines.push(current)
    current = token
  }
  if (current.length > 0) {
    lines.push(current)
  }
  return lines
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

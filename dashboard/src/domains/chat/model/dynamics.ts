import { messageParts } from "@/domains/chat/model/parts"
import type { ChatMessage } from "@/domains/chat/model/types"

/**
 * The processing dynamics, computed rather than stored.
 *
 * The mission: the operator sees comuki *working* — the typing pause, the
 * tool steps of its reasoning, the badge that says where the turn is, the
 * metrics of what it cost. None of that is a new fact on the wire; it is all
 * derivable from the parts and the meta a turn already carries, so it lives
 * here as pure functions — testable without a DOM, and one place for the
 * "what does the thread say about this turn" vocabulary.
 *
 * ## What is honest here
 *
 * The brain's progress fragments are free text. A line *may* name a tool
 * call (`memory.search("identity refactor")`) and usually reads as a
 * sentence. The parser below shapes what is there — one step per line, a
 * call-shaped prefix emphasized, the remainder kept as the step's tail — and
 * invents nothing: a line that names no call renders as a note, and a
 * thinking text with no lines at all renders no block.
 */

/** One line of the model's working-out, shaped for the step rendering. */
export interface ThinkingStep {
  /** The whole line, verbatim — the renderer never rewrites evidence. */
  text: string
  /** The call-shaped prefix (`memory.search("x")`) when the line has one. */
  call?: string
  /** Whatever followed the call on the same line, unmodified. */
  tail?: string
}

/**
 * The call shape a step line may open with: `name(...)` or `name.args(...)`
 * with anything (or nothing) inside the parentheses.
 *
 * Deliberately permissive about the inside — arguments arrive as JSON, as a
 * bare word, or empty — and deliberately anchored at the start: a sentence
 * that merely *mentions* a call mid-way is a sentence, not a step.
 */
const CALL_PREFIX = /^([a-z][\w.]*\([^)]*\))\s*(.*)$/i

/**
 * The working-out, one step per non-empty line.
 *
 * A `name(args)` prefix is split out for the mono emphasis and its tail (a
 * result, a state) is kept beside it; everything else is a note that renders
 * without the call treatment. Blank lines are dropped — they are spacing the
 * brain sent, not content.
 */
export function parseThinkingSteps(text: string): ThinkingStep[] {
  return text
    .split("\n")
    .map((line) => line.trim())
    .filter((line) => line.length > 0)
    .map((line) => {
      const match = CALL_PREFIX.exec(line)
      return match
        ? { text: line, call: match[1], tail: match[2] || undefined }
        : { text: line }
    })
}

/**
 * The iteration the working-out reached, when it said.
 *
 * The brain streams lines like `iteration 2: …` between its fragments. The
 * *last* one named is the truth about where the turn is — an earlier number
 * is history the turn already moved past.
 */
export function thinkingIteration(text: string): number | undefined {
  let found: number | undefined
  for (const line of text.split("\n")) {
    const match = /\biteration\s+(\d+)\b/i.exec(line)
    if (match) {
      const value = Number(match[1])
      if (Number.isFinite(value)) {
        found = value
      }
    }
  }
  return found
}

/** What the turn is doing, as the byline badge spells it. */
export type TurnPhase = "thinking" | "plan" | "done"

/**
 * The turn's phase, from its own state.
 *
 * - **thinking** — a reply still arriving that has working-out to show. The
 *   one phase that moves; its steps spin and its badge runs.
 * - **plan** — the turn produced a plan. The reply is a proposal-in-waiting
 *   even after it settles, so the badge stays `plan` and not `done`.
 * - **done** — everything else an assistant said. A settled reply with
 *   working-out keeps its evidence folded under a `done` badge, because the
 *   working-out *finished* — spinning on history would be a lie.
 *
 * A person's turn says none of these things about itself and gets no badge.
 */
export function turnPhase(message: ChatMessage): TurnPhase | undefined {
  if (message.kind !== "reply") {
    return undefined
  }

  const parts = messageParts(message)
  if (message.streaming && parts.some((part) => part.kind === "thinking")) {
    return "thinking"
  }
  return parts.some((part) => part.kind === "plan") ? "plan" : "done"
}

/** One pipe-separated figure of the metrics line. */
export interface TurnMetric {
  /** The figure with its unit, ready to render (`8.2s`, `614 tok`). */
  value: string
}

/**
 * The metrics of one assistant turn, or nothing.
 *
 * Latency, tokens and cost come from the row's `meta`; the tool count is the
 * turn's own tool parts. A figure that nothing reported is skipped, not
 * zeroed — `$0.000` on a turn that cost nothing to *report* would be the
 * thread claiming a measurement it never had. When nothing reports at all,
 * there is no line: the mockup's `8.2s | 4 tools | 614 tok | $0.003` is a
 * reading, not furniture.
 */
export function turnMetrics(message: ChatMessage): TurnMetric[] | undefined {
  if (message.kind !== "reply") {
    return undefined
  }

  const parts = messageParts(message)
  const metrics: TurnMetric[] = []

  const latency = message.meta?.latencyMs
  if (latency !== undefined && Number.isFinite(latency)) {
    metrics.push({ value: formatLatency(latency) })
  }

  const toolCount = parts.filter((part) => part.kind === "tool").length
  if (toolCount > 0) {
    metrics.push({ value: `${toolCount} ${toolCount === 1 ? "tool" : "tools"}` })
  }

  const meta = message.meta
  const tokens =
    meta?.tokensIn !== undefined || meta?.tokensOut !== undefined
      ? (meta.tokensIn ?? 0) + (meta.tokensOut ?? 0)
      : undefined
  if (tokens !== undefined && tokens > 0) {
    metrics.push({ value: `${tokens.toLocaleString("en-US")} tok` })
  }

  const cost = message.meta?.costMicros
  if (cost !== undefined && Number.isFinite(cost)) {
    metrics.push({ value: formatCost(cost) })
  }

  return metrics.length > 0 ? metrics : undefined
}

/**
 * How long the turn took, in the unit the number deserves.
 *
 * The tool card's own duration rule, applied to the turn: milliseconds where
 * the difference between them matters, seconds above, one decimal and no
 * more.
 */
function formatLatency(ms: number): string {
  return ms < 1000 ? `${Math.round(ms)}ms` : `${(ms / 1000).toFixed(1)}s`
}

/**
 * Micros → dollars, at the precision the figure earned.
 *
 * Three decimals while the value is below a cent per thousand micros, two
 * once whole cents are on the record — a `$0.00` on a turn that cost 40
 * micros hides the only interesting digit it had.
 */
function formatCost(micros: number): string {
  const dollars = micros / 1_000_000
  return dollars < 0.1
    ? `$${dollars.toFixed(3)}`
    : `$${dollars.toFixed(2)}`
}

/**
 * The marker the host prefixes to the memory digest it journals as a system
 * row — `ChatTurnJournalist` on the platform side. Matched by prefix because
 * the digest itself is free text; a marker the dashboard invented would
 * never match a row the host wrote.
 */
const DIGEST_MARKER = "memory digest fed to the brain:"

/**
 * Whether this row is the memory digest the turn fed the brain.
 *
 * The digest is journaled for audit as its own system message (mapped to a
 * `reply` by the wire seam — the domain has no system kind), and the thread
 * renders it as the compact memory chip rather than a paragraph: it is
 * context the turn *consumed*, and drawing it at full prose weight would
 * make the loudest thing in the thread something nobody said.
 */
export function isMemoryDigest(message: ChatMessage): boolean {
  if (message.kind !== "reply") {
    return false
  }
  const prose = messageParts(message).find((part) => part.kind === "text")
  return (
    prose !== undefined &&
    "markdown" in prose &&
    prose.markdown.startsWith(DIGEST_MARKER)
  )
}

/**
 * How many facts the digest carried, when it said.
 *
 * The digest's own lines are its facts — one recollection per line is the
 * shape the memory service writes — so the count is a reading of what is
 * there. An empty digest answers zero rather than nothing: "fed the brain
 * nothing" is itself the fact an operator would want beside an answer that
 * ignored yesterday.
 */
export function digestFactCount(message: ChatMessage): number {
  const prose = messageParts(message).find((part) => part.kind === "text")
  if (prose === undefined || !("markdown" in prose)) {
    return 0
  }
  const body = prose.markdown.slice(DIGEST_MARKER.length)
  return body
    .split("\n")
    .map((line) => line.trim())
    .filter((line) => line.length > 0).length
}

/**
 * The digest's own text, stripped of the marker, for the chip's folded body.
 */
export function digestBody(message: ChatMessage): string {
  const prose = messageParts(message).find((part) => part.kind === "text")
  if (prose === undefined || !("markdown" in prose)) {
    return ""
  }
  return prose.markdown.slice(DIGEST_MARKER.length).trim()
}

/**
 * Pure message → terminal-line formatting. No React, no I/O: every
 * function takes wire shapes (or plain values) and returns finished
 * ANSI-styled strings, which both the Ink components render and the
 * tests assert byte-for-byte.
 *
 * Style contract (minimal structure, Dichromat deck): the user's words
 * are bare bold text at column 0 — no prefix, no label; collapsed
 * events (thinking, tools) are dim `⏺` bullets two spaces in with the
 * status right after the args; the assistant leads with the periwinkle
 * `◆`; the approve card is the one framed element in the transcript
 * (code blocks keep their dim frames too). Hierarchy comes from
 * spacing and weight, never boxes.
 */
import {
  colors,
  gutter,
  messageMark,
  paint,
  stripAnsi,
  symbols,
} from "../theme"
import type { ChatMessageView, MessagePart, PlanItemView } from "./client"
import { DEFAULT_MARKDOWN_WIDTH, renderMarkdownLines } from "./markdown"

// ---------------------------------------------------------------------------
// Transcript chrome — gutter + spacing
// ---------------------------------------------------------------------------

/**
 * Prefixes every non-empty line with the one-space transcript gutter.
 * Empty lines stay truly empty (a gutter on a blank line is trailing
 * whitespace).
 */
export function gutterLines(lines: readonly string[]): string[] {
  return lines.map((line) => (line.length > 0 ? gutter + line : line))
}

/**
 * Ink drops `<Text>{""}</Text>` rows entirely — a blank separator line
 * must carry a single space to actually render. Every rendered line
 * passes through here on its way to a `<Text>`.
 */
export function blankRow(line: string): string {
  return line.length === 0 ? " " : line
}

/**
 * Spacing normalization for a rendered message: a run of two or more
 * blank lines collapses to exactly one, and trailing blank lines are
 * trimmed. Leading blanks survive — the user turn separator is one.
 */
export function normalizeSpacing(lines: readonly string[]): string[] {
  const result: string[] = []
  for (const line of lines) {
    const blank = line.trim().length === 0
    if (blank && result[result.length - 1] === "") {
      continue
    }
    result.push(blank ? "" : line)
  }
  while (result.length > 0 && result[result.length - 1] === "") {
    result.pop()
  }
  return result
}

/** ANSI-aware tail truncation for live buffers and tool summaries. */
export function truncateTail(text: string, maxWidth: number): string {
  const plain = stripAnsi(text)
  if (plain.length <= maxWidth) {
    return text
  }
  return "…" + plain.slice(plain.length - maxWidth + 1)
}

function firstLine(text: string): string {
  const line = text.split("\n", 1)[0] ?? ""
  return line.trim()
}

// ---------------------------------------------------------------------------
// Tool arguments — `memory.recall("identity module", 5)`
// ---------------------------------------------------------------------------

/**
 * Positional argument summary with a total char budget (default 40):
 * strings arrive quoted, numbers/booleans bare, arrays count as
 * `n key`, nested objects stay silent. Broken json → empty string.
 */
export function summarizeToolArgs(inputJson: string, maxChars = 40): string {
  let input: Record<string, unknown>
  try {
    input = JSON.parse(inputJson) as Record<string, unknown>
  } catch {
    return ""
  }
  const pieces: string[] = []
  let used = 0
  for (const [key, value] of Object.entries(input)) {
    if (value === null || value === undefined) {
      continue
    }
    let piece: string
    if (typeof value === "string") {
      piece = JSON.stringify(value)
    } else if (typeof value === "number" || typeof value === "boolean") {
      piece = String(value)
    } else if (Array.isArray(value)) {
      piece = `${value.length} ${key}`
    } else {
      continue
    }
    const separator = pieces.length > 0 ? 2 : 0
    if (used + separator + piece.length > maxChars) {
      const room = maxChars - used - separator
      if (room > 1) {
        pieces.push(piece.slice(0, room - 1) + "…")
      }
      break
    }
    pieces.push(piece)
    used += separator + piece.length
  }
  return pieces.join(", ")
}

// ---------------------------------------------------------------------------
// Plans — the approve card (the one allowed frame in the transcript)
// ---------------------------------------------------------------------------

export function renderPlanItems(nodes: readonly PlanItemView[]): string[] {
  return nodes.map((node) => {
    const brief = firstLine(node.brief) || "(no brief)"
    const deps =
      node.dependsOn.length > 0
        ? paint(`  ← ${node.dependsOn.join(", ")}`, colors.dim)
        : ""
    return `  ${paint(symbols.bullet, colors.accent)} ${paint(node.profileKey, colors.bright)} ${paint(symbols.arrow, colors.dim)} ${brief}${deps}`
  })
}

/**
 * The pending approval card: a `rule`-colored box-drawing frame with
 * the plan's steps numbered inside and the `approve · reject` hint
 * below it. Frame width = min(content + 4, width − 4), right-padded
 * with ─.
 */
export function renderPendingPlan(
  plan: unknown,
  width: number = DEFAULT_MARKDOWN_WIDTH
): string[] {
  const nodes = extractPlanNodes(plan)
  if (nodes.length === 0) {
    return [paint("  (plan payload unreadable)", colors.dim)]
  }
  const estimate = estimateMinutes(plan)
  const header = `plan · ${nodes.length} ${stepWord(nodes.length)}${
    estimate !== null ? ` · est ${estimate}m` : ""
  }`
  const steps = nodes.map(
    (node, index) => `${index + 1} · ${firstLine(node.brief) || "(no brief)"}`
  )
  return [
    ...planFrameLines(header, steps, width),
    `  ${paint("approve", colors.ok)}${paint(" · ", colors.dim)}${paint(
      "reject",
      colors.error
    )}${paint(" [reason]", colors.dim)}`,
  ]
}

/** The `rule`-colored frame around the card: top rule with the header, rows, bottom. */
function planFrameLines(
  header: string,
  steps: readonly string[],
  width: number
): string[] {
  const contentWidth = Math.max(header.length, ...steps.map((s) => s.length))
  const boxWidth = Math.max(
    header.length + 6,
    Math.min(contentWidth + 4, Math.max(12, width - 4))
  )
  const room = boxWidth - 4
  const fit = (text: string) =>
    text.length > room ? text.slice(0, Math.max(1, room - 1)) + "…" : text
  const row = (step: string) => {
    const plain = fit(step)
    const split = plain.indexOf(" · ") + 3
    return `${paint(plain.slice(0, split), colors.dim)}${plain.slice(split)}`
  }
  return [
    // Rule draws the frame; the header text rides it in text-muted.
    paint(`  ┌─ `, colors.rule) +
      paint(header, colors.dim) +
      paint(` ${"─".repeat(Math.max(1, boxWidth - header.length - 5))}┐`, colors.rule),
    ...steps.map(
      (step) => paint(`  │ `, colors.rule) + padVisible(row(step), room) + paint(` │`, colors.rule)
    ),
    paint(`  └${"─".repeat(boxWidth - 2)}┘`, colors.rule),
  ]
}

/** `1 шаг`, `2 шага`, `5 шагов` — russian pluralization for the header. */
export function stepWord(count: number): string {
  const mod10 = count % 10
  const mod100 = count % 100
  if (mod10 === 1 && mod100 !== 11) {
    return "шаг"
  }
  if (mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14)) {
    return "шага"
  }
  return "шагов"
}

/** Optional top-level estimate in minutes; null when the plan carries none. */
function estimateMinutes(plan: unknown): number | null {
  if (plan === null || typeof plan !== "object") {
    return null
  }
  const value = (plan as { estimateMinutes?: unknown }).estimateMinutes
  return typeof value === "number" && value > 0 ? Math.round(value) : null
}

/**
 * Normalizes raw plan nodes. The canonical wire shape is
 * `{ id, title, profileKey, brief }` (camelCase `PlanNode`); older or
 * test payloads may carry `key` — both identify a node, `brief` falls
 * back to `title`.
 */
export function extractPlanNodes(plan: unknown): PlanItemView[] {
  if (plan === null || typeof plan !== "object") {
    return []
  }
  const nodes = (plan as { nodes?: unknown }).nodes
  if (!Array.isArray(nodes)) {
    return []
  }
  return nodes.flatMap((node): PlanItemView[] => {
    if (node === null || typeof node !== "object") {
      return []
    }
    const record = node as Record<string, unknown>
    const key =
      typeof record.key === "string"
        ? record.key
        : typeof record.id === "string"
          ? record.id
          : null
    if (key === null) {
      return []
    }
    const brief =
      typeof record.brief === "string" && record.brief.trim().length > 0
        ? record.brief
        : typeof record.title === "string"
          ? record.title
          : ""
    return [
      {
        key,
        profileKey:
          typeof record.profileKey === "string" ? record.profileKey : "",
        brief,
        dependsOn: [],
      },
    ]
  })
}

// ---------------------------------------------------------------------------
// Collapsible blocks — thinking + tool parts render as one dim `⏺` event
// line unless the transcript runs verbose (ctrl+o). Pure derivation lives
// here; the toggle state is per-session in lib/sessions.ts.
// ---------------------------------------------------------------------------

/**
 * What one collapsed event line shows: `⏺ thinking · 4.1k tok · 6.2s`,
 * `⏺ memory.recall("identity module", 5)  ok 41ms`. Durations and token
 * counts only appear when the wire actually carried them.
 */
export interface CollapsedSummary {
  readonly icon: string
  readonly label: string
  /** Status badge (`ok` / `error` / `…`), tools only; null when absent. */
  readonly badge: string | null
  /** Dim metadata segments (`4.1k tok`, `6.2s`); empty when unknown. */
  readonly details: readonly string[]
}

/**
 * Derives the collapsed summary for a part, or `null` when the part is
 * not collapsible (text, code, diagram, handoff, plan always render in
 * full). The single source for both the pure line renderer and tests.
 */
export function collapsedSummary(part: MessagePart): CollapsedSummary | null {
  if (part.kind === "thinking") {
    const details: string[] = []
    if (typeof part.tokens === "number" && part.tokens > 0) {
      details.push(formatTokenCount(part.tokens))
    }
    if (typeof part.durationMs === "number") {
      details.push(formatDurationMs(part.durationMs))
    }
    return { icon: symbols.event, label: "thinking", badge: null, details }
  }
  if (part.kind === "tool") {
    return {
      icon: symbols.event,
      label: `${part.name}(${summarizeToolArgs(part.inputJson)})`,
      badge: toolStatusBadge(part.status),
      details:
        typeof part.durationMs === "number"
          ? [formatDurationMs(part.durationMs)]
          : [],
    }
  }
  return null
}

function toolStatusBadge(status: string): string {
  const lowered = status.toLowerCase()
  if (lowered === "failed" || lowered === "error") {
    return "error"
  }
  if (lowered === "running") {
    return "…"
  }
  return "ok"
}

/** `120ms`, `3.4s`, `2m 5s` — compact durations for collapsed lines. */
export function formatDurationMs(durationMs: number): string {
  if (durationMs < 1000) {
    return `${Math.round(durationMs)}ms`
  }
  if (durationMs < 60_000) {
    return `${(durationMs / 1000).toFixed(1)}s`
  }
  const minutes = Math.floor(durationMs / 60_000)
  const seconds = Math.round((durationMs % 60_000) / 1000)
  return seconds > 0 ? `${minutes}m ${seconds}s` : `${minutes}m`
}

/** `40 tok`, `1.2k tok` — thinking size when duration is unknown. */
export function formatTokenCount(tokens: number): string {
  return tokens < 1000 ? `${tokens} tok` : `${(tokens / 1000).toFixed(1)}k tok`
}

function badgeColor(badge: string): string {
  if (badge === "error") {
    return colors.error
  }
  if (badge === "…") {
    return colors.accent
  }
  if (badge === "ok") {
    return colors.ok
  }
  return colors.dim
}

/** Renders a derived summary as its single quiet event line. */
export function renderCollapsedLine(summary: CollapsedSummary): string {
  let line = `  ${paint(summary.icon, colors.dim)} ${paint(summary.label, colors.muted)}`
  if (summary.badge !== null) {
    line += `  ${paint(summary.badge, badgeColor(summary.badge))}`
  }
  if (summary.details.length > 0) {
    line +=
      summary.badge === null
        ? ` ${paint(
            summary.details.map((detail) => `${symbols.bullet} ${detail}`).join(" "),
            colors.dim
          )}`
        : ` ${paint(summary.details.join(" "), colors.dim)}`
  }
  return line
}

/** Expanded thinking body: markdown-rendered, dimmed, under the event line. */
function renderExpandedThinking(text: string, width: number): string[] {
  return renderMarkdownLines(text, width).map((line) =>
    line.trim().length === 0 ? line : paint(indentBlock(line, "    "), colors.dim)
  )
}

/** Expanded tool body: pretty-printed input and output under the event line. */
function renderExpandedTool(
  part: Extract<MessagePart, { kind: "tool" }>
): string[] {
  return [
    ...prettyJsonBlock("input", part.inputJson),
    ...(part.outputJson ? prettyJsonBlock("output", part.outputJson) : []),
  ]
}

function prettyJsonBlock(label: string, json: string): string[] {
  let body = json
  try {
    body = JSON.stringify(JSON.parse(json), null, 2)
  } catch {
    // Not parseable json — the raw payload is the honest view.
  }
  return [
    paint(`    ${label}:`, colors.dim),
    ...body.split("\n").map((line) => paint(`    ${line}`, colors.muted)),
  ]
}

// ---------------------------------------------------------------------------
// Parts → lines
// ---------------------------------------------------------------------------

/** Indents a multi-line block by two spaces, keeping per-line ANSI. */
export function indentBlock(text: string, indent = "  "): string {
  return text
    .split("\n")
    .map((line) => (line.length > 0 ? indent + line : line))
    .join("\n")
}

/** Collapse control for thinking/tool parts; omitted options → full render. */
export interface PartRenderOptions {
  readonly expanded?: boolean
}

export function renderPart(
  part: MessagePart,
  width: number = DEFAULT_MARKDOWN_WIDTH,
  { expanded = true }: PartRenderOptions = {}
): string[] {
  if (part.kind === "thinking" || part.kind === "tool") {
    const summary = collapsedSummary(part)
    if (summary === null) {
      // Defensive only — thinking/tool are always collapsible.
      return []
    }
    if (!expanded) {
      return [renderCollapsedLine(summary)]
    }
    // Expanded keeps the event line as the header of the revealed block.
    return [
      renderCollapsedLine(summary),
      ...(part.kind === "thinking"
        ? renderExpandedThinking(part.text, width)
        : renderExpandedTool(part)),
    ]
  }
  switch (part.kind) {
    case "code": {
      const anchor =
        part.path !== null && part.path !== undefined
          ? paint(
              ` ${part.path}${part.startLine ? `:${part.startLine}` : ""}`,
              colors.dim
            )
          : ""
      const header = `  ${paint(symbols.bullet, colors.accent)} ${part.language}${anchor}`
      return [
        header,
        ...indentBlock(part.source)
          .split("\n")
          .map((line) => paint(line, colors.muted)),
      ]
    }
    case "diagram":
      return [
        `  ${paint(symbols.bullet, colors.accent)} ${part.dialect}`,
        ...indentBlock(part.source)
          .split("\n")
          .map((line) => paint(line, colors.muted)),
      ]
    case "handoff":
      return [`  ${paint(symbols.arrow, colors.accent)} open ${part.query}`]
    case "plan":
      return renderPlanItems(part.nodes)
    case "text":
      return renderMarkdownLines(part.markdown, width)
  }
}

export function renderParts(
  parts: readonly MessagePart[],
  width: number = DEFAULT_MARKDOWN_WIDTH,
  options?: PartRenderOptions
): string[] {
  const lines: string[] = []
  let previousWasEvent = false
  for (const part of parts) {
    const isEvent = part.kind === "thinking" || part.kind === "tool"
    // The events block and the prose answer breathe apart — one blank
    // line on each side of the boundary, never inside a run of events.
    if (lines.length > 0 && isEvent !== previousWasEvent) {
      lines.push("")
    }
    lines.push(...renderPart(part, width, options))
    previousWasEvent = isEvent
  }
  return lines
}

// ---------------------------------------------------------------------------
// Whole messages
// ---------------------------------------------------------------------------

/**
 * One transcript row → lines. Assistant rows prefer parts (the rich
 * shape); `content` is the flat fallback. Both render markdown through
 * `lib/markdown.ts`. User rows echo as typed — bare bold text at
 * column 0, one blank line before AND after. Tool and system journal
 * rows render muted. Options omitted → full render (the pure layer's
 * default); the transcript passes the session's ctrl+o toggle so
 * thinking/tool parts collapse to `⏺` event lines.
 *
 * Identity chrome: the assistant leads with its periwinkle `◆` brand
 * mark on the shared one-space gutter with a dim `comuki` label above
 * its content; journal rows keep the quiet `·` bullets. Wrapping is
 * computed at `width - 1` so the gutter never pushes a line past the
 * terminal edge.
 */
export function renderMessage(
  message: ChatMessageView,
  width: number = DEFAULT_MARKDOWN_WIDTH,
  options?: PartRenderOptions
): string[] {
  if (message.role === "user") {
    return renderUserEcho(message.content)
  }
  if (message.role === "assistant") {
    const mark = messageMark(message.role)
    const header = `${paint(mark.glyph, mark.glyphColor)}${
      mark.label.length > 0
        ? ` ${paint(mark.label, mark.labelColor)}`
        : ""
    }`
    const lines =
      message.parts !== null && message.parts.length > 0
        ? renderParts(message.parts, Math.max(8, width - gutter.length), options)
        : renderMarkdownLines(message.content, Math.max(8, width - gutter.length))
    const meta = message.meta
    const cost = meta?.model
      ? paint(
          `  ${symbols.bullet} ${meta.model}${
            typeof meta.tokensIn === "number" &&
            typeof meta.tokensOut === "number"
              ? ` ${meta.tokensIn}→${meta.tokensOut} tok`
              : ""
          }`,
          colors.dim
        )
      : null
    return normalizeSpacing(
      gutterLines([header, ...lines, ...(cost ? [cost] : [])])
    )
  }
  // tool / system journal rows
  const label = message.toolName ?? message.role
  const body = message.content.trim()
  return [
    paint(
      `${gutter}${symbols.bullet} ${label}${body ? `: ${truncateTail(body, 72)}` : ""}`,
      colors.muted
    ),
  ]
}

/**
 * The user's own words as the transcript shows them — bold deck-text
 * at column 0 with a blank line on each side. Also used for the
 * immediate echo on send, so the live line and the restored history
 * of the same turn are byte-identical.
 */
export function renderUserEcho(content: string): string[] {
  const text = content.trim()
  if (text.length === 0) {
    return []
  }
  return ["", paint(text, messageMark("user").textColor), ""]
}

// ---------------------------------------------------------------------------
// Misc status formatting
// ---------------------------------------------------------------------------

/** `2m`, `1h`, `3d` — coarse ages for run tables and headers. */
export function ageFromIso(iso: string, now: Date = new Date()): string {
  const then = Date.parse(iso)
  if (Number.isNaN(then)) {
    return "?"
  }
  return ageFromMs(now.getTime() - then)
}

export function ageFromMs(ms: number): string {
  const seconds = Math.max(0, Math.floor(ms / 1000))
  if (seconds < 60) {
    return `${seconds}s`
  }
  const minutes = Math.floor(seconds / 60)
  if (minutes < 60) {
    return `${minutes}m`
  }
  const hours = Math.floor(minutes / 60)
  if (hours < 24) {
    return `${hours}h`
  }
  return `${Math.floor(hours / 24)}d`
}

/** Pads a status word to a fixed column, colored by semantics. */
export function paintStatus(status: string): string {
  const lowered = status.toLowerCase()
  if (["succeeded", "completed", "replied", "success"].includes(lowered)) {
    return paint(status.padEnd(10), colors.ok)
  }
  if (["failed", "cancelled", "escalated"].includes(lowered)) {
    return paint(status.padEnd(10), colors.error)
  }
  if (["running", "busy"].includes(lowered)) {
    return paint(status.padEnd(10), colors.accent)
  }
  if (["queued", "waiting", "awaiting_approval", "idle"].includes(lowered)) {
    return paint(status.padEnd(10), colors.waiting)
  }
  return paint(status.padEnd(10), colors.muted)
}

/** Left-pads `text` so stripAnsi(text).length === width. */
export function padVisible(text: string, width: number): string {
  const visible = stripAnsi(text).length
  return visible >= width ? text : text + " ".repeat(width - visible)
}

/** Simple fixed-column table row renderer for `runs list`. */
export function tableRow(
  cells: readonly { text: string; width: number }[]
): string {
  return cells
    .map((cell) =>
      cell.width === 0
        ? cell.text
        : padVisible(truncateTail(cell.text, cell.width), cell.width)
    )
    .join("  ")
    .trimEnd()
}

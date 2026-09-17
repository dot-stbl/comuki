/**
 * Pure message → terminal-line formatting. No React, no I/O: every
 * function takes wire shapes (or plain values) and returns finished
 * ANSI-styled strings, which both the Ink components render and the
 * tests assert byte-for-byte.
 *
 * Style contract (option A, terminal-native): thinking dimmed, tools in
 * muted mono with a compact `name(args) → result` shape, prose in the
 * terminal default, one slate-blue accent for statuses and the prompt.
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
// Tool parts — `memory.search("identity")  2 facts`
// ---------------------------------------------------------------------------

/** Short positional summary of a tool call: first string-ish arguments. */
export function summarizeToolInput(name: string, inputJson: string): string {
  let input: Record<string, unknown>
  try {
    input = JSON.parse(inputJson) as Record<string, unknown>
  } catch {
    return name
  }
  const args: string[] = []
  for (const [key, value] of Object.entries(input)) {
    if (value === null || value === undefined) {
      continue
    }
    if (typeof value === "string") {
      args.push(
        JSON.stringify(value.length > 32 ? value.slice(0, 29) + "…" : value)
      )
    } else if (typeof value === "number" || typeof value === "boolean") {
      args.push(String(value))
    } else if (Array.isArray(value)) {
      args.push(`${value.length} ${key}`)
    }
    if (args.length === 2) {
      break
    }
  }
  return args.length > 0 ? `${name}(${args.join(", ")})` : name
}

/**
 * Compact observation summary: array fields count (`2 facts`), explicit
 * count/total fields pass through, anything else stays silent.
 */
export function summarizeToolOutput(
  outputJson: string | null | undefined,
  status: string
): string {
  if (status === "running") {
    return ""
  }
  if (!outputJson) {
    return ""
  }
  let output: unknown
  try {
    output = JSON.parse(outputJson)
  } catch {
    return ""
  }
  if (Array.isArray(output)) {
    return output.length > 0 ? `${output.length} items` : "0 items"
  }
  if (output !== null && typeof output === "object") {
    for (const [key, value] of Object.entries(
      output as Record<string, unknown>
    )) {
      if (typeof value === "number" && /count|total|hits|results?/.test(key)) {
        return String(value)
      }
    }
    for (const [key, value] of Object.entries(
      output as Record<string, unknown>
    )) {
      if (Array.isArray(value)) {
        return value.length > 0 ? `${value.length} ${key}` : ""
      }
    }
  }
  return ""
}

/** One tool line: muted name(args), dim result / duration, status glyph. */
export function renderToolPart(
  part: Extract<MessagePart, { kind: "tool" }>
): string {
  const call = summarizeToolInput(part.name, part.inputJson)
  const result = summarizeToolOutput(part.outputJson, part.status)
  const duration =
    typeof part.durationMs === "number"
      ? `${Math.round(part.durationMs)}ms`
      : ""
  const tail = [result, duration]
    .filter((piece) => piece.length > 0)
    .join(paint(" " + symbols.bullet + " ", colors.dim))

  if (part.status === "failed") {
    return `  ${paint(symbols.cross, colors.red)} ${paint(call, colors.muted)} ${paint(tail, colors.red)}`.trimEnd()
  }
  if (part.status === "running") {
    return `  ${paint("…", colors.accent)} ${paint(call, colors.muted)}`
  }
  return `  ${paint(symbols.checkmark, colors.green)} ${paint(call, colors.muted)}${tail ? `  ${paint(tail, colors.dim)}` : ""}`.trimEnd()
}

// ---------------------------------------------------------------------------
// Plans — the approve card
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

/** Renders the pending plan JSON from a turn result (unknown-shaped by design). */
export function renderPendingPlan(plan: unknown): string[] {
  const nodes = extractPlanNodes(plan)
  if (nodes.length === 0) {
    return [paint("  (plan payload unreadable)", colors.dim)]
  }
  return renderPlanItems(nodes)
}

function extractPlanNodes(plan: unknown): PlanItemView[] {
  if (plan !== null && typeof plan === "object") {
    const nodes = (plan as { nodes?: unknown }).nodes
    if (Array.isArray(nodes)) {
      return nodes.filter(
        (node): node is PlanItemView =>
          node !== null && typeof node === "object" && "key" in node
      )
    }
  }
  return []
}

// ---------------------------------------------------------------------------
// Collapsible blocks — thinking + tool parts render as one summary line
// unless the transcript runs verbose (ctrl+o). Pure derivation lives here;
// the toggle state is per-session in lib/sessions.ts.
// ---------------------------------------------------------------------------

/** What one collapsed block shows: `◌ thinking · 3.4s`, `⚙ tool(args) → ok`. */
export interface CollapsedSummary {
  readonly icon: string
  readonly label: string
  readonly badge: string | null
}

/**
 * Derives the collapsed summary for a part, or `null` when the part is
 * not collapsible (text, code, diagram, handoff, plan always render in
 * full). The single source for both the pure line renderer and tests.
 */
export function collapsedSummary(part: MessagePart): CollapsedSummary | null {
  if (part.kind === "thinking") {
    const badge =
      typeof part.durationMs === "number"
        ? formatDurationMs(part.durationMs)
        : typeof part.tokens === "number" && part.tokens > 0
          ? formatTokenCount(part.tokens)
          : null
    return { icon: symbols.thinking, label: "thinking", badge }
  }
  if (part.kind === "tool") {
    return {
      icon: symbols.tool,
      label: `${part.name}(${summarizeToolArgs(part.inputJson)})`,
      badge: toolStatusBadge(part.status),
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

function badgeColor(badge: string): string {
  if (badge === "error") {
    return colors.red
  }
  if (badge === "…") {
    return colors.accent
  }
  if (badge === "ok") {
    return colors.green
  }
  return colors.dim
}

/** Renders a derived summary as its single dim transcript line. */
export function renderCollapsedLine(summary: CollapsedSummary): string {
  const badge =
    summary.badge === null
      ? ""
      : // Result badges (ok / error / …) read as `→ result`; metadata
        // badges (durations, token counts) read as `· detail`.
        ` ${paint(
          isResultBadge(summary.badge) ? symbols.arrow : symbols.bullet,
          colors.dim
        )} ${paint(summary.badge, badgeColor(summary.badge))}`
  return `  ${paint(summary.icon, colors.dim)} ${paint(summary.label, colors.muted)}${badge}`
}

function isResultBadge(badge: string): boolean {
  return badge === "ok" || badge === "error" || badge === "…"
}

/** Expanded thinking: markdown-rendered, dimmed, indented two spaces. */
function renderExpandedThinking(text: string, width: number): string[] {
  return renderMarkdownLines(text, width).map((line) =>
    line.trim().length === 0 ? line : paint(indentBlock(line), colors.dim)
  )
}

/** Expanded tool: the legacy status line plus full args and result. */
function renderExpandedTool(
  part: Extract<MessagePart, { kind: "tool" }>
): string[] {
  return [
    renderToolPart(part),
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
  if (!expanded) {
    const summary = collapsedSummary(part)
    if (summary !== null) {
      return [renderCollapsedLine(summary)]
    }
  }
  switch (part.kind) {
    case "thinking":
      return renderExpandedThinking(part.text, width)
    case "tool":
      return renderExpandedTool(part)
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
  return parts.flatMap((part) => renderPart(part, width, options))
}

// ---------------------------------------------------------------------------
// Whole messages
// ---------------------------------------------------------------------------

/**
 * One transcript row → lines. Assistant rows prefer parts (the rich
 * shape); `content` is the flat fallback. Both render markdown through
 * `lib/markdown.ts`. User rows echo as typed — plain, one line. Tool
 * and system journal rows render muted. Options omitted → full render
 * (the pure layer's default); the transcript passes the session's
 * ctrl+o toggle so thinking/tool parts collapse to summary lines.
 *
 * Identity chrome: every row leads with its `messageMark` glyph on the
 * shared one-space gutter — the user's dim `›` with bright text (one
 * blank line before, the turn separator), the assistant's brand `◆`
 * with a dim `comuki` label above its content, quiet `·` bullets for
 * journal rows. Wrapping is computed at `width - 1` so the gutter
 * never pushes a line past the terminal edge.
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
 * The user's own words as the transcript shows them — also used for the
 * immediate echo on send, so the live line and the restored history of
 * the same turn are byte-identical. One blank line before (the turn
 * separator), dim `›`, bright text.
 */
export function renderUserEcho(content: string): string[] {
  const mark = messageMark("user")
  const text = content.trim()
  if (text.length === 0) {
    return []
  }
  return [
    "",
    `${gutter}${paint(mark.glyph, mark.glyphColor)} ${paint(text, mark.textColor)}`,
  ]
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
    return paint(status.padEnd(10), colors.green)
  }
  if (["failed", "cancelled", "escalated"].includes(lowered)) {
    return paint(status.padEnd(10), colors.red)
  }
  if (["running", "busy"].includes(lowered)) {
    return paint(status.padEnd(10), colors.accent)
  }
  if (["queued", "waiting", "awaiting_approval", "idle"].includes(lowered)) {
    return paint(status.padEnd(10), colors.yellow)
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

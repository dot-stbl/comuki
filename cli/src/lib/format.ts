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
import { colors, paint, stripAnsi, symbols } from "../theme"
import type { ChatMessageView, MessagePart, PlanItemView } from "./client"

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
// Parts → lines
// ---------------------------------------------------------------------------

/** Indents a multi-line block by two spaces, keeping per-line ANSI. */
export function indentBlock(text: string, indent = "  "): string {
  return text
    .split("\n")
    .map((line) => (line.length > 0 ? indent + line : line))
    .join("\n")
}

export function renderPart(part: MessagePart): string[] {
  switch (part.kind) {
    case "thinking":
      return part.text
        .split("\n")
        .filter((line) => line.trim().length > 0)
        .map((line) => paint(indentBlock(line), colors.dim))
    case "tool":
      return [renderToolPart(part)]
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
      return part.markdown.split("\n")
  }
}

export function renderParts(parts: readonly MessagePart[]): string[] {
  return parts.flatMap(renderPart)
}

// ---------------------------------------------------------------------------
// Whole messages
// ---------------------------------------------------------------------------

/**
 * One transcript row → lines. Assistant rows prefer parts (the rich
 * shape); `content` is the flat fallback. User rows echo as typed. Tool
 * and system journal rows render muted.
 */
export function renderMessage(message: ChatMessageView): string[] {
  if (message.role === "user") {
    return [
      `${paint("you  ", colors.accent)}${symbols.prompt} ${message.content}`,
    ]
  }
  if (message.role === "assistant") {
    const lines =
      message.parts !== null && message.parts.length > 0
        ? renderParts(message.parts)
        : message.content.split("\n")
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
    return cost ? [...lines, cost] : lines
  }
  // tool / system journal rows
  const label = message.toolName ?? message.role
  const body = message.content.trim()
  return [
    paint(
      `  ${symbols.bullet} ${label}${body ? `: ${truncateTail(body, 72)}` : ""}`,
      colors.muted
    ),
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

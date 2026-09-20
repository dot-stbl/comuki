/**
 * Styled-line primitives for the OpenTUI transcript (issue #74).
 *
 * The port of `lib/markdown.ts` + `lib/diff.ts` RENDERING BEHAVIOR
 * onto renderer-neutral lines: every line is a list of styled
 * segments (tone + weight flags) instead of finished ANSI strings.
 * The host (`./host.ts`) maps tones to renderable fg colors; this
 * module stays pure and trivially testable — no OpenTUI imports.
 *
 * Style contract mirrors the Ink Dichromat deck: prose in `text`,
 * quiet tiers in `muted`/`faint`, one accent for inline code /
 * bullets / h3+, ok lavender for diff additions, error yellow for
 * deletions, `rule` for code-frame borders. Colour never carries
 * status alone — every status pairs with its word.
 *
 * Approval rendering (issue #76) sits beside the entry renderers:
 * `approvalSummary` / `approvalDetail` are the typed seam the
 * transcript pipeline calls per density (compact / domain / stacked).
 */

import { lexer, type Token, type Tokens } from "marked"

import { TUI_NAMESPACE, tr, type I18nInstance } from "../locales"
import type { ApprovalEntry } from "./approvals"

// ---------------------------------------------------------------------------
// Tones + segments
// ---------------------------------------------------------------------------

/**
 * The eight deck colors a transcript line can wear. Hex values mirror
 * the Dichromat dark reading (`cli/src/theme.ts`) — the TUI host
 * renders the same palette.
 */
export type Tone = "text" | "muted" | "faint" | "accent" | "ok" | "error" | "waiting" | "rule"

/** Tone → hex fg for the renderable layer (and color-asserting tests). */
export const TONE_HEX: Readonly<Record<Tone, string>> = {
  text: "#e8e8ee",
  muted: "#b8b8bd",
  faint: "#8a8a8f",
  accent: "#8787f3",
  ok: "#d7d7ff",
  error: "#d2d228",
  waiting: "#b4b442",
  rule: "#37373c",
}

/** Weight/shape flags a segment can carry beside its tone. */
export interface SegmentStyle {
  readonly bold?: boolean
  readonly italic?: boolean
  readonly underline?: boolean
  readonly strike?: boolean
}

/** One styled inline span. `atomic` marks unbreakable units (code spans, links). */
export interface Segment {
  readonly text: string
  readonly tone: Tone
  readonly style: SegmentStyle
  readonly atomic: boolean
}

/** One rendered row — styled segments joined left to right. */
export type StyledLine = readonly Segment[]

export function seg(
  text: string,
  tone: Tone = "text",
  style: SegmentStyle = {},
  atomic = false
): Segment {
  return { text, tone, style, atomic }
}

/** The hard-break marker segment (a sole `\n`). */
const BREAK: Segment = { text: "\n", tone: "text", style: {}, atomic: true }

/** A blank row OpenTUI keeps (an empty string row would collapse). */
export function blankLine(): StyledLine {
  return [seg(" ")]
}

export function lineText(line: StyledLine): string {
  return line.map((segment) => segment.text).join("")
}

export function lineLength(line: StyledLine): number {
  return line.reduce((total, segment) => total + segment.text.length, 0)
}

/** Renders every segment of `line` in `tone` — for single-tone rows. */
export function uniformLine(text: string, tone: Tone, style: SegmentStyle = {}): StyledLine {
  return text.length === 0 ? blankLine() : [seg(text, tone, style)]
}

/** Plain-text tail truncation (the `truncateTail` port — segment text is ANSI-free). */
export function truncateTail(text: string, maxWidth: number): string {
  if (text.length <= maxWidth) {
    return text
  }
  return "…" + text.slice(text.length - maxWidth + 1)
}

// ---------------------------------------------------------------------------
// Styled word wrap
// ---------------------------------------------------------------------------

/**
 * Greedy word wrap over styled segments (the `wrapRuns` port). The
 * first wrapped line carries `firstPrefix`, the rest indent under
 * `continuationPrefix`. A word longer than the room hard-splits by
 * characters (narrow viewports must not overflow) — the one
 * deliberate divergence from the Ink port, which lets long words
 * overflow.
 */
export function wrapSegments(
  segments: readonly Segment[],
  width: number,
  firstPrefix: readonly Segment[] = [],
  continuationPrefix: readonly Segment[] = []
): StyledLine[] {
  const prefixLength = () => lineLength(firstPrefix)
  const room = () => Math.max(1, width - prefixLength())
  const lines: StyledLine[] = []
  let prefix = firstPrefix
  let units: Segment[] = []
  let used = 0

  const flush = () => {
    lines.push([...prefix, ...joinUnits(units)])
    prefix = continuationPrefix
    units = []
    used = 0
  }

  /** Words re-join with plain spaces (the `units.join(" ")` port). */
  const joinUnits = (parts: readonly Segment[]): Segment[] => {
    if (parts.length <= 1) {
      return [...parts]
    }
    const joined: Segment[] = []
    parts.forEach((part, index) => {
      if (index > 0) {
        joined.push(seg(" ", part.tone, part.style))
      }
      joined.push(part)
    })
    return joined
  }

  const push = (unit: Segment) => {
    if (unit.text.length > room() && units.length === 0) {
      // Hard-split a lone overlong word by characters.
      let token = unit.text
      while (token.length > room()) {
        lines.push([...prefix, seg(token.slice(0, room()), unit.tone, unit.style)])
        prefix = continuationPrefix
        token = token.slice(room())
      }
      units = [seg(token, unit.tone, unit.style)]
      used = token.length
      return
    }
    if (units.length === 0) {
      units = [unit]
      used = unit.text.length
      return
    }
    if (used + 1 + unit.text.length > room()) {
      flush()
      prefix = continuationPrefix
      units = [unit]
      used = unit.text.length
      return
    }
    units.push(unit)
    used += 1 + unit.text.length
  }

  for (const segment of segments) {
    if (segment.atomic && segment.text === "\n") {
      flush()
      continue
    }
    if (segment.atomic) {
      push(segment)
      continue
    }
    const pieces = segment.text.split("\n")
    pieces.forEach((piece, index) => {
      if (index > 0) {
        flush()
      }
      for (const word of piece.split(/\s+/)) {
        if (word.length > 0) {
          push(seg(word, segment.tone, segment.style))
        }
      }
    })
  }
  if (units.length > 0) {
    lines.push([...prefix, ...joinUnits(units)])
  }
  return lines
}

/** Character-hard wrap for code bodies (continuation aligned, no word rules). */
export function hardWrap(text: string, width: number): string[] {
  if (width <= 0) {
    return [text]
  }
  const chunks: string[] = []
  for (let offset = 0; offset < text.length; offset += width) {
    chunks.push(text.slice(offset, offset + width))
  }
  return chunks.length > 0 ? chunks : [""]
}

// ---------------------------------------------------------------------------
// Markdown — inline runs (the `runsFromToken` port)
// ---------------------------------------------------------------------------

interface RunStyle extends SegmentStyle {
  readonly tone: Tone
}

function runsFromTokens(
  tokens: readonly Token[] | undefined,
  style: RunStyle
): Segment[] {
  if (!tokens) {
    return []
  }
  return tokens.flatMap((token) => runsFromToken(token, style))
}

function withStyle(style: RunStyle, extra: SegmentStyle): RunStyle {
  return { ...style, bold: style.bold || extra.bold, italic: style.italic || extra.italic, underline: style.underline || extra.underline, strike: style.strike || extra.strike }
}

function runsFromToken(token: Token, style: RunStyle): Segment[] {
  switch (token.type) {
    case "strong":
      return runsFromTokens(token.tokens, withStyle(style, { bold: true }))
    case "em":
      return runsFromTokens(token.tokens, withStyle(style, { italic: true }))
    case "del":
      return runsFromTokens(token.tokens, withStyle(style, { strike: true }))
    case "codespan":
      return [{ text: token.text, tone: "accent", style, atomic: true }]
    case "link": {
      const label = runsFromTokens(token.tokens, style)
      const href = safeDecode(token.href)
      const labelPlain = label.map((part) => part.text).join("")
      if (href && href !== labelPlain && !/^mailto:/.test(href)) {
        label.push({ text: `(${href})`, tone: "muted", style, atomic: true })
      }
      return label
    }
    case "image": {
      const image = token as Tokens.Image
      const alt = image.text.trim()
      return [
        {
          text: alt.length > 0 ? alt : safeDecode(image.href),
          tone: "muted",
          style,
          atomic: true,
        },
      ]
    }
    case "br":
      return [BREAK]
    case "escape":
      return [seg((token as Tokens.Escape).text, style.tone, style)]
    case "html":
      return [seg((token as Tokens.HTML).text.trim(), "muted", style)]
    case "text": {
      const text = token as Tokens.Text
      return text.tokens ? runsFromTokens(text.tokens, style) : [seg(text.text, style.tone, style)]
    }
    default: {
      const text = (token as Tokens.Generic).text
      return typeof text === "string" ? [seg(text, style.tone, style)] : []
    }
  }
}

function safeDecode(href: string): string {
  try {
    return decodeURI(href)
  } catch {
    return href
  }
}

// ---------------------------------------------------------------------------
// Markdown — blocks (the `blockLines` port)
// ---------------------------------------------------------------------------

export const DEFAULT_MARKDOWN_WIDTH = 80

/** Smallest width we bother wrapping at — below this everything overflows anyway. */
const MIN_WIDTH = 20

const LIST_BULLET = "."
const INDENT_UNIT = "  "

function headingLines(token: Tokens.Heading, width: number): StyledLine[] {
  const style: SegmentStyle =
    token.depth === 1
      ? { bold: true, underline: true }
      : token.depth === 2
        ? { bold: true }
        : {}
  const tone: Tone = token.depth <= 2 ? "text" : "accent"
  // The heading style wraps the whole line (Ink paints it around the
  // wrapped runs); inline segments keep their own tones — an accent
  // codespan inside an h3 stays readable against the accent heading.
  return wrapSegments(
    runsFromTokens(token.tokens, { tone, ...style }),
    width
  )
}

function paragraphLines(token: Tokens.Paragraph, width: number): StyledLine[] {
  return wrapSegments(runsFromTokens(token.tokens, { tone: "text" }), width)
}

/**
 * The dim `+- lang -+` code frame with hard-wrapped body — the
 * `codeBlockLines` port. `label` rides the top rule muted; body rows
 * carry the `rule` bar and a `muted` body; blank body rows keep the
 * bare bar.
 */
export function codeFrameLines(
  lang: string | undefined,
  code: string,
  width: number
): StyledLine[] {
  const prefix = `${INDENT_UNIT}| `
  const inner = Math.max(8, width - prefix.length)
  const rawLabel =
    lang && lang.trim().length > 0 ? ` ${lang.trim().split(/\s+/)[0]} ` : " "
  const bodyLines = code
    .replace(/\n+$/, "")
    .split("\n")
    .flatMap((line) => hardWrap(line, inner))
  const bodyWidth = Math.max(
    8,
    ...bodyLines.map((line) => line.length),
    rawLabel.length + 1
  )
  const fill = Math.max(2, bodyWidth - rawLabel.length + 1)
  return [
    [
      seg(`${INDENT_UNIT}+-`, "rule"),
      seg(rawLabel, "muted"),
      seg(`${"-".repeat(fill)}+`, "rule"),
    ],
    ...bodyLines.map((line) =>
      line.length === 0
        ? [seg(`${INDENT_UNIT}|`, "rule")]
        : [seg(prefix, "rule"), seg(line, "muted")]
    ),
    [seg(`${INDENT_UNIT}+${"-".repeat(bodyWidth + 2)}+`, "rule")],
  ]
}

function quoteLines(token: Tokens.Blockquote, width: number): StyledLine[] {
  const bar = [seg(`${INDENT_UNIT}| `, "muted")]
  return markdownBlocks(token.tokens, Math.max(MIN_WIDTH, width - 3)).map((line) =>
    lineText(line).trim().length === 0 ? [seg(`${INDENT_UNIT}|`, "muted")] : [...bar, ...line]
  )
}

function listLines(token: Tokens.List, width: number, depth: number): StyledLine[] {
  const indent = INDENT_UNIT.repeat(depth + 1)
  const lines: StyledLine[] = []
  let index = typeof token.start === "number" ? token.start : 1
  for (const item of token.items) {
    const markerPlain = token.ordered ? `${index}.` : LIST_BULLET
    const markerTone: Tone = token.ordered ? "muted" : "accent"
    const runs: Segment[] = []
    const nested: StyledLine[][] = []
    for (const sub of item.tokens) {
      if (sub.type === "checkbox") {
        continue // rendered via item.task / item.checked below
      }
      if (sub.type === "text") {
        const text = sub as Tokens.Text
        runs.push(
          ...(text.tokens
            ? runsFromTokens(text.tokens, { tone: "text" })
            : [seg(text.text, "text")])
        )
      } else if (sub.type === "list") {
        nested.push(listLines(sub as Tokens.List, width, depth + 1))
      } else {
        nested.push(
          blockLines(sub, Math.max(MIN_WIDTH, width - indent.length), depth)
        )
      }
    }
    if (item.task) {
      runs.unshift(seg(item.checked ? "[x] " : "[ ] ", "muted"))
    }
    lines.push(
      ...wrapSegments(
        runs,
        width,
        [seg(indent, "muted"), seg(markerPlain, markerTone), seg(" ", "muted")],
        [seg(indent + " ".repeat(markerPlain.length + 1), "muted")]
      )
    )
    for (const block of nested) {
      lines.push(...block)
    }
    index++
  }
  return lines
}

function tableLines(token: Tokens.Table, width: number): StyledLine[] {
  const columns = token.header.length
  if (columns === 0) {
    return []
  }
  const separator = INDENT_UNIT
  const overhead = 2 + separator.length * (columns - 1)
  const budget = Math.max(MIN_WIDTH, width - overhead)
  const cap = Math.max(4, Math.floor(budget / columns))

  const cell = (tokens: readonly Token[] | undefined): Segment[] =>
    runsFromTokens(tokens, { tone: "text" })

  const measure = (runs: readonly Segment[]): number =>
    runs.map((part) => part.text).join("").length

  const headerCells = token.header.map((column) => cell(column.tokens))
  const bodyCells = token.rows.map((row) => row.map((column) => cell(column.tokens)))

  const widths = headerCells.map((runs, columnIndex) =>
    Math.min(
      cap,
      Math.max(
        measure(runs),
        ...bodyCells.map((row) => measure(row[columnIndex] ?? []))
      )
    )
  )

  const renderCell = (runs: readonly Segment[], cellWidth: number, style: SegmentStyle): StyledLine[] =>
    wrapSegments(
      runs.map((part) => seg(part.text, part.tone, { ...part.style, ...style })),
      cellWidth
    )

  const headerRow = headerCells.map((runs, columnIndex) =>
    renderCell(runs, widths[columnIndex] ?? cap, { bold: true })
  )
  const bodyRows = bodyCells.map((row) =>
    row.map((runs, columnIndex) => renderCell(runs, widths[columnIndex] ?? cap, {}))
  )

  const lines: StyledLine[] = [...emitRow(headerRow, widths, cap, separator)]
  lines.push([
    seg(
      `${INDENT_UNIT}${widths.map((columnWidth) => "-".repeat(columnWidth)).join(separator)}`,
      "rule"
    ),
  ])
  for (const row of bodyRows) {
    lines.push(...emitRow(row, widths, cap, separator))
  }
  return lines
}

/** One table body row — cells padded to column width, separator-joined. */
function emitRow(
  cells: readonly StyledLine[][],
  widths: readonly number[],
  cap: number,
  separator: string
): StyledLine[] {
  const height = Math.max(...cells.map((cellLines) => cellLines.length))
  const lines: StyledLine[] = []
  for (let rowLine = 0; rowLine < height; rowLine++) {
    const parts: Segment[] = [seg("  ", "muted")]
    cells.forEach((cellLines, columnIndex) => {
      if (columnIndex > 0) {
        parts.push(seg(separator, "muted"))
      }
      const row = cellLines[rowLine] ?? []
      const used = lineLength(row)
      parts.push(...row)
      const cellWidth = widths[columnIndex] ?? cap
      if (used < cellWidth) {
        parts.push(seg(" ".repeat(cellWidth - used), "muted"))
      }
    })
    lines.push(parts)
  }
  return lines
}

function blockLines(token: Token, width: number, depth: number): StyledLine[] {
  switch (token.type) {
    case "space":
      return []
    case "heading":
      return headingLines(token as Tokens.Heading, width)
    case "paragraph":
      return paragraphLines(token as Tokens.Paragraph, width)
    case "code":
      return codeFrameLines(
        (token as Tokens.Code).lang,
        (token as Tokens.Code).text,
        width
      )
    case "blockquote":
      return quoteLines(token as Tokens.Blockquote, width)
    case "list":
      return listLines(token as Tokens.List, width, depth)
    case "table":
      return tableLines(token as Tokens.Table, width)
    case "hr":
      return [[seg(`${INDENT_UNIT}${"-".repeat(Math.max(4, width - 4))}`, "rule")]]
    case "html":
      return (token as Tokens.HTML).text
        .trim()
        .split("\n")
        .map((line) => uniformLine(line.length > 0 ? `${INDENT_UNIT}${line}` : line, "muted"))
    case "text": {
      const text = token as Tokens.Text
      if (text.tokens) {
        return wrapSegments(runsFromTokens(text.tokens, { tone: "text" }), width)
      }
      return text.text.length > 0
        ? wrapSegments([seg(text.text, "text")], width)
        : []
    }
    default: {
      const raw = (token as Tokens.Generic).raw?.trim()
      return raw ? raw.split("\n").map((line) => uniformLine(line, "text")) : []
    }
  }
}

/** Renders block tokens, one blank line between top-level blocks. */
function markdownBlocks(tokens: readonly Token[], width: number): StyledLine[] {
  const groups = tokens
    .map((token) => blockLines(token, width, 0))
    .filter((group) => group.length > 0)
  const lines: StyledLine[] = []
  for (const group of groups) {
    if (lines.length > 0) {
      lines.push(blankLine())
    }
    lines.push(...group)
  }
  return lines
}

// ---------------------------------------------------------------------------
// Markdown entry point
// ---------------------------------------------------------------------------

/**
 * Markdown source → styled lines wrapped to `width` columns (the
 * `renderMarkdownLines` port). Blank input renders nothing; a lexer
 * failure degrades to raw lines so a malformed stream chunk can never
 * blank the transcript.
 */
export function markdownLines(
  markdown: string,
  width: number = DEFAULT_MARKDOWN_WIDTH
): StyledLine[] {
  const source = markdown.replace(/\r\n?/g, "\n")
  if (source.trim().length === 0) {
    return []
  }
  const clamped = Math.max(MIN_WIDTH, width)
  let tokens: Token[]
  try {
    tokens = lexer(source)
  } catch {
    return source.split("\n").map((line) => uniformLine(line, "text"))
  }
  const lines = markdownBlocks(tokens, clamped)
  while (lines.length > 0 && lineText(lines[0] ?? []).trim().length === 0) {
    lines.shift()
  }
  while (
    lines.length > 0 &&
    lineText(lines[lines.length - 1] ?? []).trim().length === 0
  ) {
    lines.pop()
  }
  return lines
}

// ---------------------------------------------------------------------------
// Diff rendering (the `lib/diff.ts` port)
// ---------------------------------------------------------------------------

/** A well-formed unified-diff hunk header: `@@ -3,7 +3,8 @@ optional …`. */
const HUNK_HEADER = /^@@ -\d+(?:,\d+)? \+\d+(?:,\d+)? @@/

/** File-level metadata lines: `diff --git`, `index`, `---`, `+++`, `new file mode`… . */
function isFileHeader(line: string): boolean {
  return (
    line.startsWith("diff --git ") ||
    line.startsWith("index ") ||
    line.startsWith("--- ") ||
    line.startsWith("+++ ") ||
    line.startsWith("new file mode") ||
    line.startsWith("deleted file mode") ||
    line.startsWith("old mode") ||
    line.startsWith("new mode") ||
    line.startsWith("rename from") ||
    line.startsWith("rename to") ||
    line.startsWith("similarity index") ||
    line.startsWith("copy from") ||
    line.startsWith("copy to") ||
    line.startsWith("Binary files")
  )
}

/**
 * True when a code block should render as a structured diff: the
 * language tag says `diff`, or the body carries the unmistakable
 * signatures of a unified diff (conservative — a bare `@@` in prose
 * must not flip a code block into diff mode).
 */
export function isDiffContent(
  lang: string | undefined | null,
  code: string
): boolean {
  if ((lang ?? "").trim().toLowerCase() === "diff") {
    return true
  }
  const lines = code.split("\n")
  const first = lines[0] ?? ""
  if (first.startsWith("diff --git ")) {
    return true
  }
  if (first.startsWith("--- a/")) {
    return true
  }
  return lines.some((line) => HUNK_HEADER.test(line))
}

/** Plain-text tail ellipsis: keeps the diff marker + code lead, drops the tail. */
function fitPlain(line: string, width: number): string {
  if (width <= 0) {
    return line
  }
  return line.length > width ? line.slice(0, Math.max(1, width - 1)) + "…" : line
}

function diffLine(line: string, width: number): StyledLine {
  if (line.length === 0) {
    return blankLine()
  }
  if (line.startsWith("@@")) {
    return uniformLine(fitPlain(line, width), "accent")
  }
  if (isFileHeader(line)) {
    return uniformLine(fitPlain(line, width), "muted")
  }
  if (line.startsWith("+")) {
    return uniformLine(fitPlain(line, width), "ok")
  }
  if (line.startsWith("-")) {
    return uniformLine(fitPlain(line, width), "error")
  }
  // Context rows (and the `\ No newline` notice) are the background the
  // changed lines pop against — the faintest reading tier.
  return uniformLine(fitPlain(line, width), "faint")
}

/**
 * A unified diff body → styled lines, one per source line. One diff
 * line is ONE viewport row — a continuation wrap would lose the
 * `+`/`-` gutter alignment, so overlong lines ellipsize on the tail.
 */
export function diffLines(
  code: string,
  width: number = DEFAULT_MARKDOWN_WIDTH
): StyledLine[] {
  const body = code.replace(/\n$/, "")
  return body.split("\n").map((line) => diffLine(line, width))
}

// ---------------------------------------------------------------------------
// Approval rendering (issue #76) — pure seam for the entry pipeline
// ---------------------------------------------------------------------------

/**
 * The `* ` event marker every collapsed/expanded entry header leads with —
 * same convention as `entries.ts`. Imported indirectly via the entry pipeline;
 * redeclared here so this module is the single seam for styled approval
 * lines.
 */
const APPROVAL_EVENT_MARK = "* "

/**
 * One approval entry → a single styled summary line. The density
 * (`risk`) decides the shape:
 *
 * - `compact` — `* approval · approve / reject · <scope>`
 * - `domain`  — `* approval · <scope> · <requester>`
 * - `stacked` — `* approval · <N> nodes · <scope>`
 */
export function approvalSummary(entry: ApprovalEntry, i18n: I18nInstance): StyledLine {
  const segments: Segment[] = [seg(APPROVAL_EVENT_MARK, "faint")]
  segments.push(
    seg(`${tr(i18n, "transcript.entry.approval.label")} · `, "muted"),
  )
  switch (entry.risk) {
    case "compact":
      segments.push(
        seg(tr(i18n, "approval.action.approve"), "ok"),
        seg(" / ", "muted"),
        seg(tr(i18n, "approval.action.reject"), "error"),
        seg(" · ", "muted"),
        seg(entry.applicability.scope, "faint"),
      )
      break
    case "domain":
      // Domain renders the same verbs as compact — the legacy card
      // snapshot keys on these literals, and the in-line action verbs
      // are useful at every density.
      segments.push(
        seg(entry.applicability.scope, "muted"),
        seg(" · ", "muted"),
        seg(tr(i18n, "approval.action.approve"), "ok"),
        seg(" / ", "muted"),
        seg(tr(i18n, "approval.action.reject"), "error"),
        seg(" · ", "muted"),
        seg(entry.applicability.requester, "faint"),
      )
      break
    case "stacked": {
      const nodes = entry.pendingAction.kind === "stacked"
        ? entry.pendingAction.nodes
        : []
      segments.push(
        seg(
          interpolate(i18n, "transcript.entry.approval.stackedHeader", { nodes: nodes.length }),
          "muted",
        ),
        seg(" · ", "muted"),
        seg(tr(i18n, "approval.action.approve"), "ok"),
        seg(" / ", "muted"),
        seg(tr(i18n, "approval.action.reject"), "error"),
        seg(" · ", "muted"),
        seg(entry.applicability.scope, "faint"),
      )
      break
    }
  }
  return segments
}

/** `tr` with interpolation params — same loud-miss contract as `entries.ts`. */
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
    `i18n: missing or empty key '${key}' in locale '${i18n.language}' (namespace '${TUI_NAMESPACE}')`,
  )
}

/**
 * One approval entry → a styled detail block. The intent/scope/risk
 * lines mirror the legacy `InlineApprovalCard` shape so existing
 * snapshots and tests keep reading the same fields:
 *
 * - `intent: <text>` (when present)
 * - `scope: <text>` (when present)
 * - `risk: <text>` (when present)
 * - `plan:`
 *   `› <step>` per step (one node per row when stacked)
 * - `diff:` (when the plan payload carries diff lines)
 *   `+ <line>` / `- <line>` per line
 * - `decide: y = approve, n = reject`
 *
 * The block always renders in full — approvals are interactive, the
 * user needs every field visible without toggling ctrl+o.
 */
export function approvalDetail(
  entry: ApprovalEntry,
  context: ApprovalRenderContext
): StyledLine[] {
  const i18n = context.i18n
  const indent = "    "
  const lines: StyledLine[] = []

  // Intent, scope, risk — only when the plan carries the field.
  const intent = readIntent(entry)
  if (intent !== null) {
    lines.push([
      seg(indent, "faint"),
      seg(`${tr(i18n, "approval.intentPrefix")} `, "muted"),
      seg(intent, "text", { bold: true }),
    ])
  }
  const scope = readScope(entry)
  if (scope !== null) {
    lines.push([
      seg(indent, "faint"),
      seg(`${tr(i18n, "approval.scopePrefix")} `, "muted"),
      seg(scope, "muted"),
    ])
  }
  const risk = readRisk(entry)
  if (risk !== null) {
    lines.push([
      seg(indent, "faint"),
      seg(`${tr(i18n, "approval.riskPrefix")} `, "muted"),
      seg(risk, risk === "high" ? "error" : "muted"),
    ])
  }

  // Plan header + steps (domain) or per-node list (stacked).
  const steps = readSteps(entry)
  if (steps.length > 0 || entry.pendingAction.kind === "stacked") {
    const estimate = readEstimateMinutes(entry)
    const estimateSuffix = estimate !== null ? ` (~${estimate}m)` : ""
    lines.push([
      seg(indent, "faint"),
      seg(`${tr(i18n, "approval.planPrefix")}${estimateSuffix}`, "muted"),
    ])
    if (entry.pendingAction.kind === "stacked") {
      for (const node of entry.pendingAction.nodes) {
        lines.push([
          seg(`${indent}${indent} `, "faint"),
          seg(node.id, "faint"),
          seg(" ", "faint"),
          seg(node.profileKey, "text", { bold: true }),
          seg(" → ", "faint"),
          seg(node.brief.length > 0 ? node.brief : node.title, "muted"),
        ])
      }
    } else {
      const renderSteps = steps.length > 0
        ? steps
        : [tr(i18n, "approval.unreadablePlan")]
      for (const step of renderSteps) {
        lines.push([
          seg(`${indent}${indent} `, "faint"),
          seg(`${tr(i18n, "approval.stepPrefix")} ${step}`, "muted"),
        ])
      }
    }
  }

  // Diff (when present).
  const diff = readDiff(entry)
  if (diff.length > 0) {
    lines.push([
      seg(indent, "faint"),
      seg(tr(i18n, "approval.diffPrefix"), "muted"),
    ])
    for (const line of diff) {
      lines.push([
        seg(`${indent}${indent} `, "faint"),
        seg(line, "ok"),
      ])
    }
  }

  // The decide label is the literal legacy copy — y/n keep working
  // through the registered approval layer; the palette reaches the
  // same dispatch path. The literal stays so the existing card-shape
  // snapshots keep matching.
  lines.push([seg(indent, "faint"), seg(tr(i18n, "approval.decideLabel"), "muted")])

  return lines
}

/** Minimal context the approval renderer needs — mirrors `EntryRenderContext`. */
export interface ApprovalRenderContext {
  readonly i18n: I18nInstance
  readonly width: number
  readonly expanded: ReadonlySet<string>
}

// -- field readers ----------------------------------------------------------

function readIntent(entry: ApprovalEntry): string | null {
  const action = entry.pendingAction
  if (action.kind === "domain") {
    const payload = action.planPayload as { intent?: unknown } | undefined
    const intent = payload?.intent
    if (typeof intent === "string" && intent.trim().length > 0) {
      return intent.trim()
    }
    return action.effects.length > 0 ? action.effects : null
  }
  return action.effects.length > 0 ? action.effects : null
}

function readScope(entry: ApprovalEntry): string | null {
  if (entry.applicability.scope.length === 0) {
    return null
  }
  return entry.applicability.scope
}

function readRisk(entry: ApprovalEntry): string | null {
  const action = entry.pendingAction
  if (action.kind === "domain") {
    const payload = action.planPayload as { risk?: unknown } | undefined
    const risk = payload?.risk
    if (typeof risk === "string" && risk.trim().length > 0) {
      return risk.trim()
    }
  }
  return null
}

function readSteps(entry: ApprovalEntry): readonly string[] {
  const action = entry.pendingAction
  if (action.kind === "domain") {
    const payload = action.planPayload as
      | { planSteps?: unknown; steps?: unknown; nodes?: unknown }
      | undefined
    // Prefer legacy `planSteps` (or `steps`) when the array has content.
    // `payload.steps` is always an array (possibly empty); only fall back
    // to the nodes' briefs when the legacy arrays carry nothing.
    if (Array.isArray(payload?.planSteps) && payload.planSteps.length > 0) {
      return payload.planSteps.filter(
        (step): step is string => typeof step === "string"
      )
    }
    if (Array.isArray(payload?.steps) && payload.steps.length > 0) {
      return payload.steps.filter(
        (step): step is string => typeof step === "string"
      )
    }
    // The platform wire shape carries steps inside `nodes` when there is
    // no legacy `planSteps` array. Fall back to the node briefs.
    if (Array.isArray(payload?.nodes)) {
      return payload.nodes
        .filter(
          (node): node is { brief?: unknown } =>
            typeof node === "object" && node !== null
        )
        .map((node) =>
          typeof node.brief === "string" && node.brief.length > 0
            ? node.brief
            : ""
        )
        .filter((line) => line.length > 0)
    }
    return []
  }
  if (action.kind === "stacked") {
    return action.nodes.map((node) => node.brief)
  }
  return []
}

function readDiff(entry: ApprovalEntry): readonly string[] {
  const action = entry.pendingAction
  if (action.kind === "domain") {
    const payload = action.planPayload as
      | { diff?: unknown }
      | undefined
    if (typeof payload?.diff === "string" && payload.diff.length > 0) {
      return payload.diff.split("\n")
    }
    if (Array.isArray(payload?.diff)) {
      return payload.diff.filter((line): line is string => typeof line === "string")
    }
  }
  return []
}

function readEstimateMinutes(entry: ApprovalEntry): number | null {
  const action = entry.pendingAction
  if (action.kind === "domain") {
    const payload = action.planPayload as
      | { estimateMinutes?: unknown }
      | undefined
    const estimate = payload?.estimateMinutes
    if (typeof estimate === "number" && Number.isFinite(estimate) && estimate > 0) {
      return Math.round(estimate)
    }
  }
  return null
}

/**
 * Pure markdown → ANSI-line rendering. No React, no I/O: `marked`'s
 * lexer tokenizes the source, this module maps tokens to finished
 * ANSI-styled lines using the one-accent palette from `theme.ts`,
 * wrapping every paragraph, list item and code line to the given
 * terminal width.
 *
 * Style contract (option A, terminal-native): prose in the terminal
 * default, dim `│`-bordered code blocks with the language label on the
 * top rule, bright `#`/`##` and accent `###`+ headings, accent inline
 * code, accent bullets for lists, dim `▎` blockquote bars.
 *
 * Width math counts code points of the *stripped* text, so a line is
 * guaranteed to fit a monospace terminal of the given width for
 * narrow (1-column) glyphs; wide CJK content may still exceed it and
 * is left for Ink's own re-wrap.
 */
import { lexer, type Token, type Tokens } from "marked"
import { colors, paint, stripAnsi, symbols } from "../theme"

export const DEFAULT_MARKDOWN_WIDTH = 80

/** Smallest width we bother wrapping at — below this everything overflows anyway. */
const MIN_WIDTH = 20

// ---------------------------------------------------------------------------
// Inline runs — styled text segments before wrapping
// ---------------------------------------------------------------------------

/**
 * One styled inline segment. `atomic` marks unbreakable units (code
 * spans, links) that must not be split across wrapped lines.
 */
interface Run {
  readonly text: string
  readonly ansi: string
  readonly atomic: boolean
}

const BREAK: Run = { text: "\n", ansi: "", atomic: true }

function run(text: string, ansi = ""): Run {
  return { text, ansi, atomic: false }
}

/** Plain text split into runs, preserving hard newlines as breaks. */
function plainRuns(text: string, ansi = ""): Run[] {
  return text
    .split("\n")
    .flatMap((segment, index) =>
      index === 0 ? [run(segment, ansi)] : [BREAK, run(segment, ansi)]
    )
}

function runsFromTokens(
  tokens: readonly Token[] | undefined,
  ansi = ""
): Run[] {
  if (!tokens) {
    return []
  }
  return tokens.flatMap((token) => runsFromToken(token, ansi))
}

function runsFromToken(token: Token, ansi: string): Run[] {
  switch (token.type) {
    case "strong":
      return runsFromTokens(token.tokens, ansi + colors.bright)
    case "em":
      return runsFromTokens(token.tokens, ansi + colors.italic)
    case "del":
      return runsFromTokens(token.tokens, ansi + colors.strike)
    case "codespan":
      return [
        { text: token.text, ansi: ansi + colors.accent, atomic: true },
      ]
    case "link": {
      const label = runsFromTokens(token.tokens, ansi)
      const href = safeDecode(token.href)
      const labelPlain = label.map((part) => part.text).join("")
      if (href && href !== labelPlain && !/^mailto:/.test(href)) {
        label.push({ text: `(${href})`, ansi: ansi + colors.dim, atomic: true })
      }
      return label
    }
    case "image": {
      const image = token as Tokens.Image
      const alt = image.text.trim()
      return [
        {
          text: alt.length > 0 ? alt : safeDecode(image.href),
          ansi: ansi + colors.dim,
          atomic: true,
        },
      ]
    }
    case "br":
      return [BREAK]
    case "escape":
      return [run((token as Tokens.Escape).text, ansi)]
    case "html":
      return [run((token as Tokens.HTML).text.trim(), ansi + colors.dim)]
    case "text": {
      const text = token as Tokens.Text
      return text.tokens ? runsFromTokens(text.tokens, ansi) : plainRuns(text.text, ansi)
    }
    default: {
      const text = (token as Tokens.Generic).text
      return typeof text === "string" ? plainRuns(text, ansi) : []
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
// ANSI-aware word wrap
// ---------------------------------------------------------------------------

function paintRun(text: string, ansi: string): string {
  return ansi.length > 0 ? ansi + text + colors.reset : text
}

/**
 * Greedy word wrap over styled runs. Prefixes may themselves contain
 * ANSI (list markers), so the available width is measured on the
 * stripped prefix. A word longer than the line goes on its own line
 * and overflows — Ink's layout re-wraps what we could not.
 */
function wrapRuns(
  runs: readonly Run[],
  width: number,
  firstPrefix: string,
  continuationPrefix: string
): string[] {
  const lines: string[] = []
  let prefix = firstPrefix
  let units: string[] = []
  let used = 0

  const room = () => width - stripAnsi(prefix).length

  const flush = () => {
    lines.push(prefix + units.join(" "))
    prefix = continuationPrefix
    units = []
    used = 0
  }

  const push = (painted: string, visibleLength: number) => {
    if (units.length === 0) {
      units = [painted]
      used = visibleLength
      return
    }
    if (used + 1 + visibleLength > room()) {
      flush()
      units = [painted]
      used = visibleLength
      return
    }
    units.push(painted)
    used += 1 + visibleLength
  }

  for (const segment of runs) {
    if (segment.atomic) {
      if (segment.text === "\n") {
        flush()
        continue
      }
      push(paintRun(segment.text, segment.ansi), stripAnsi(segment.text).length)
      continue
    }
    for (const word of segment.text.split(/\s+/)) {
      if (word.length > 0) {
        push(paintRun(word, segment.ansi), word.length)
      }
    }
  }
  if (units.length > 0) {
    lines.push(prefix + units.join(" "))
  }
  return lines
}

/** Character-hard wrap for code (no word rules, continuation aligned). */
function hardWrap(line: string, width: number): string[] {
  if (width <= 0) {
    return [line]
  }
  const chunks: string[] = []
  for (let offset = 0; offset < line.length; offset += width) {
    chunks.push(line.slice(offset, offset + width))
  }
  return chunks.length > 0 ? chunks : [""]
}

function padVisible(text: string, width: number): string {
  const visible = stripAnsi(text).length
  return visible >= width ? text : text + " ".repeat(width - visible)
}

// ---------------------------------------------------------------------------
// Block renderers
// ---------------------------------------------------------------------------

function headingLines(token: Tokens.Heading, width: number): string[] {
  const style =
    token.depth === 1
      ? colors.bright + colors.underline
      : token.depth === 2
        ? colors.bright
        : colors.accent
  return wrapRuns(runsFromTokens(token.tokens), width, "", "").map(
    (line) => style + line + colors.reset
  )
}

function paragraphLines(token: Tokens.Paragraph, width: number): string[] {
  return wrapRuns(runsFromTokens(token.tokens), width, "", "")
}

function codeBlockLines(
  lang: string | undefined,
  code: string,
  width: number
): string[] {
  const prefix = "  │ "
  const inner = Math.max(8, width - prefix.length)
  const label =
    lang && lang.trim().length > 0 ? ` ${lang.trim().split(/\s+/)[0]} ` : " "
  const bodyLines = code
    .replace(/\n+$/, "")
    .split("\n")
    .flatMap((line) => hardWrap(line, inner))
  const bodyWidth = Math.max(
    8,
    ...bodyLines.map((line) => stripAnsi(line).length),
    label.length + 1
  )
  const fill = Math.max(2, bodyWidth - label.length + 1)
  const lines = [
    // Deck `rule` draws the frame; the language label rides it dimmed.
    paint(`  ┌─`, colors.rule) +
      paint(label, colors.dim) +
      paint(`${"─".repeat(fill)}┐`, colors.rule),
    ...bodyLines.map((line) =>
      line.length === 0
        ? `  ${paint("│", colors.rule)}`
        : `  ${paint("│", colors.rule)} ${paint(line, colors.muted)}`
    ),
    paint(`  └${"─".repeat(bodyWidth + 2)}┘`, colors.rule),
  ]
  return lines
}

function quoteLines(token: Tokens.Blockquote, width: number): string[] {
  const bar = `  ${paint("▎", colors.dim)} `
  const inner = renderBlocks(token.tokens, Math.max(MIN_WIDTH, width - 3))
  return inner.map((line) =>
    line.length === 0 ? `  ${paint("▎", colors.dim)}` : bar + line
  )
}

function listLines(token: Tokens.List, width: number, depth: number): string[] {
  const indent = "  ".repeat(depth + 1)
  const lines: string[] = []
  let index = typeof token.start === "number" ? token.start : 1
  for (const item of token.items) {
    const markerPlain = token.ordered ? `${index}.` : symbols.bullet
    const marker = token.ordered
      ? paint(markerPlain, colors.dim)
      : paint(markerPlain, colors.accent)
    const runs: Run[] = []
    const nested: string[][] = []
    for (const sub of item.tokens) {
      if (sub.type === "checkbox") {
        continue // rendered via item.task / item.checked below
      }
      if (sub.type === "text") {
        const text = sub as Tokens.Text
        runs.push(...(text.tokens ? runsFromTokens(text.tokens) : plainRuns(text.text)))
      } else if (sub.type === "list") {
        nested.push(listLines(sub as Tokens.List, width, depth + 1))
      } else {
        nested.push(blockLines(sub, Math.max(MIN_WIDTH, width - indent.length), depth))
      }
    }
    if (item.task) {
      runs.unshift({
        text: item.checked ? "[x] " : "[ ] ",
        ansi: colors.dim,
        atomic: false,
      })
    }
    lines.push(
      ...wrapRuns(
        runs,
        width,
        `${indent}${marker} `,
        indent + " ".repeat(markerPlain.length + 1)
      )
    )
    for (const block of nested) {
      lines.push(...block)
    }
    index++
  }
  return lines
}

function tableLines(token: Tokens.Table, width: number): string[] {
  const columns = token.header.length
  if (columns === 0) {
    return []
  }
  const separator = "  "
  const overhead = 2 + separator.length * (columns - 1)
  const budget = Math.max(MIN_WIDTH, width - overhead)
  const cap = Math.max(4, Math.floor(budget / columns))

  const cell = (tokens: readonly Token[] | undefined): Run[] =>
    runsFromTokens(tokens)

  const measure = (runs: readonly Run[]): number =>
    stripAnsi(runs.map((part) => part.text).join("")).length

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

  const renderCell = (runs: readonly Run[], width: number, style: string) =>
    wrapRuns(runs, width, "", "").map((line) => style + line + colors.reset)

  const rows: string[][][] = [
    headerCells.map((runs, columnIndex) =>
      renderCell(runs, widths[columnIndex] ?? cap, colors.bright)
    ),
    ...bodyCells.map((row) =>
      row.map((runs, columnIndex) => renderCell(runs, widths[columnIndex] ?? cap, ""))
    ),
  ]

  const lines: string[] = []

  const emit = (cells: string[][]): string[] => {
    const height = Math.max(...cells.map((cellLinesList) => cellLinesList.length))
    const out: string[] = []
    for (let rowLine = 0; rowLine < height; rowLine++) {
      out.push(
        "  " +
          cells
            .map((cellLinesList, columnIndex) =>
              padVisible(cellLinesList[rowLine] ?? "", widths[columnIndex] ?? cap)
            )
            .join(separator)
      )
    }
    return out
  }

  lines.push(...emit(rows[0] ?? []))
  lines.push(
    paint(
      "  " +
        widths.map((columnWidth) => "─".repeat(columnWidth)).join(separator),
      colors.rule
    )
  )
  for (const row of rows.slice(1)) {
    lines.push(...emit(row))
  }
  return lines
}

function blockLines(token: Token, width: number, depth: number): string[] {
  switch (token.type) {
    case "space":
      return []
    case "heading":
      return headingLines(token as Tokens.Heading, width)
    case "paragraph":
      return paragraphLines(token as Tokens.Paragraph, width)
    case "code":
      return codeBlockLines(
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
      return [
        paint("  " + "─".repeat(Math.max(4, width - 4)), colors.rule),
      ]
    case "html":
      return (token as Tokens.HTML).text
        .trim()
        .split("\n")
        .map((line) => paint(line.length > 0 ? "  " + line : line, colors.dim))
    case "text": {
      const text = token as Tokens.Text
      return text.tokens
        ? wrapRuns(runsFromTokens(text.tokens), width, "", "")
        : plainRuns(text.text).length > 0
          ? wrapRuns(plainRuns(text.text), width, "", "")
          : []
    }
    default: {
      const raw = (token as Tokens.Generic).raw?.trim()
      return raw ? raw.split("\n") : []
    }
  }
}

/** Renders block tokens, one blank line between top-level blocks. */
function renderBlocks(tokens: readonly Token[], width: number): string[] {
  const groups = tokens
    .map((token) => blockLines(token, width, 0))
    .filter((group) => group.length > 0)
  const lines: string[] = []
  for (const group of groups) {
    if (lines.length > 0) {
      lines.push("")
    }
    lines.push(...group)
  }
  return lines
}

// ---------------------------------------------------------------------------
// Entry point
// ---------------------------------------------------------------------------

/**
 * Markdown source → finished ANSI lines, wrapped to `width` columns.
 * Blank input renders nothing; a lexer failure degrades to raw lines
 * so a malformed stream chunk can never blank the transcript.
 */
export function renderMarkdownLines(
  markdown: string,
  width: number = DEFAULT_MARKDOWN_WIDTH
): string[] {
  const source = markdown.replace(/\r\n?/g, "\n")
  if (source.trim().length === 0) {
    return []
  }
  const clamped = Math.max(MIN_WIDTH, width)
  let tokens: Token[]
  try {
    tokens = lexer(source)
  } catch {
    return source.split("\n")
  }
  const lines = renderBlocks(tokens, clamped)
  while (
    lines.length > 0 &&
    stripAnsi(lines[0] ?? "").trim().length === 0
  ) {
    lines.shift()
  }
  while (
    lines.length > 0 &&
    stripAnsi(lines[lines.length - 1] ?? "").trim().length === 0
  ) {
    lines.pop()
  }
  return lines
}

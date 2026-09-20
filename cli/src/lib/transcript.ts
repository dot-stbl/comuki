/**
 * Transcript flattening: the session's blocks → one array of finished
 * ANSI lines, in order, ready for the scrolling viewport. Everything
 * here is pure (no React, no Ink) — message rendering is reused from
 * `lib/format.ts`, the live stream from `lib/markdown.ts`, so the
 * viewport shows byte-identical text to what the components rendered.
 *
 * Spacing is enforced here at the seams: the user echo carries its
 * own leading blank and sits tight against the following event
 * cluster (no extra seam); one blank after the event cluster before
 * the answer lives inside `renderParts`. Never two blanks in a row.
 *
 * `wrapVisible` is the safety net of the fixed-height viewport: any
 * line wider than the terminal (long user echo, unwrappable word) is
 * ANSI-aware hard-wrapped so exactly `height` rendered rows fit the
 * budget and the footer/prompt can never be pushed off-screen.
 */
import { renderMessage, renderPendingPlan } from "./format"
import { renderRunsFeedPanel, type RunsFeedPanel } from "./runsfeed"
import { renderMarkdownLines } from "./markdown"
import { colors, gutter, paint, stripAnsi, symbols } from "../theme"
import type { ChatBlock } from "./sessions"

export const LIVE_CURSOR = "_"
export const TYPING_LABEL = "thinking"
export const EXPAND_HINT = "* press ctrl+o to expand thinking"

/** Role of one flattened transcript row — drives the viewport slab bg. */
export type TranscriptRole =
  | "user"
  | "assistant"
  | "event"
  | "pulse"
  | "approval"
  | "alert"
  | "rule"
  | "blank"

/**
 * Classifies a flattened line from its visible prefix. User rows lead
 * with `>` after the gutter; event rows with `*`; rule rows
 * with a signed diff marker; blank is empty; everything else is the
 * assistant card.
 */
export function classifyLine(plain: string): TranscriptRole {
  const visible = stripAnsi(plain)
  const trimmed = visible.trimStart()
  if (trimmed.length === 0) {
    return "blank"
  }
  const lead = trimmed[0]
  if (lead === ">") {
    return "user"
  }
  if (trimmed.startsWith("[approval]")) {
    return "approval"
  }
  if (/^\[(error|warn|info)\]/.test(trimmed)) {
    return "alert"
  }
  if (/^[|/\\-] thinking$/.test(trimmed)) {
    return "pulse"
  }
  if (lead === "*" || trimmed.startsWith(symbols.event)) {
    return "event"
  }
  if (visible.startsWith("     ")) {
    return "event"
  }
  if (lead === "-" || lead === "+") {
    return "rule"
  }
  return "assistant"
}

/**
 * Right-pads `line` to `width` visible columns so an Ink
 * `backgroundColor` fills the whole slab, not just the glyphs.
 */
export function padVisible(line: string, width: number): string {
  if (width <= 0) {
    return line
  }
  const visible = stripAnsi(line).length
  if (visible >= width) {
    return line
  }
  return line + " ".repeat(width - visible)
}

// ---------------------------------------------------------------------------
// ANSI-aware hard wrap
// ---------------------------------------------------------------------------

const RESET = colors.reset

/**
 * Wraps one finished line to `width` visible columns. SGR escape
 * sequences are zero-width; a wrapped continuation re-applies the SGR
 * state active at the break so colors survive the wrap.
 */
export function wrapVisible(line: string, width: number): string[] {
  if (width <= 0) {
    return [line]
  }
  const chunks: string[] = []
  let current = ""
  let carried = ""
  let visible = 0
  let index = 0
  while (index < line.length) {
    if (line.charCodeAt(index) === 0x1b) {
      const match = /^\x1b\[[0-9;]*m/.exec(line.slice(index))
      if (match) {
        const sequence = match[0]
        carried = sequence === RESET ? "" : carried + sequence
        current += sequence
        index += sequence.length
        continue
      }
      // Non-SGR escape (cursor moves etc.) — pass through, zero width.
      current += line[index]
      index += 1
      continue
    }
    if (visible >= width) {
      // Close the SGR run only when a color is actually open — plain
      // chunks must not gain a trailing reset they never had.
      chunks.push(carried.length > 0 ? current + RESET : current)
      current = carried
      visible = 0
    }
    const char = String.fromCodePoint(line.codePointAt(index) ?? 0x20)
    current += char
    visible += 1
    index += char.length
  }
  chunks.push(current)
  return chunks
}

// ---------------------------------------------------------------------------
// Live stream + typing spinner
// ---------------------------------------------------------------------------

/**
 * The growing live tail as finished lines: markdown-rendered, with the
 * ASCII cursor `_` riding the write head (the last line). Empty stream
 * renders nothing; the viewport slab carries assistant identity.
 */
export function liveLines(liveText: string, width: number): string[] {
  if (liveText.trim().length === 0) {
    return []
  }
  const innerWidth = Math.max(8, width - gutter.length)
  let lines = renderMarkdownLines(liveText, innerWidth)
  if (lines.length === 0) {
    lines = [""]
  }
  const last = lines.length - 1
  const body = lines.map((line, index) =>
    index === last ? line + paint(LIVE_CURSOR, colors.accent) : line
  )
  return body.map((line) =>
    line.length > 0 ? gutter + line : line
  )
}

/** One-line spinner pulse before the assistant stream begins. */
export function typingLines(
  frame: number,
  label: string = TYPING_LABEL
): readonly string[] {
  const count = symbols.spinnerFrames.length
  const index = ((frame % count) + count) % count
  const glyph = symbols.spinnerFrames[index] ?? symbols.spinnerFrames[0]
  return [
    `${paint("[", colors.dim)}${paint(glyph, colors.accent)}${paint("]", colors.dim)} ${paint(label, colors.dim)}`,
  ]
}

// ---------------------------------------------------------------------------
// ctrl+o expand hint
// ---------------------------------------------------------------------------

/**
 * True when the active transcript holds thinking parts that currently
 * render collapsed — the one condition under which the viewport shows
 * its top hint line.
 */
export function hasCollapsedThinking(
  blocks: readonly ChatBlock[],
  expanded: boolean
): boolean {
  if (expanded) {
    return false
  }
  return blocks.some(
    (block) =>
      block.kind === "message" &&
      block.message.parts != null &&
      block.message.parts.some((part) => part.kind === "thinking")
  )
}

/** The hint as a right-aligned dim line for the top of a `width` viewport. */
export function expandHintLine(width: number): string {
  const pad = Math.max(1, width - stripAnsi(EXPAND_HINT).length - 1)
  return paint(" ".repeat(pad) + EXPAND_HINT, colors.dim)
}

// ---------------------------------------------------------------------------
// Flatten
// ---------------------------------------------------------------------------

/** The transcript-relevant slice of a session (see `Session` in sessions.ts). */
export interface TranscriptSnapshot {
  readonly blocks: readonly ChatBlock[]
  readonly awaitingApproval: boolean
  readonly pendingPlan: unknown
  /** The pinned `/runs` panel; null/undefined renders nothing. */
  readonly runsFeed?: RunsFeedPanel | null
  readonly thinking: boolean
  readonly liveText: string
  /** ctrl+o per-tab toggle — thinking/tool parts collapse when false (default). */
  readonly expanded?: boolean
}

/**
 * Blocks → flat lines, in transcript order: history messages, raw line
 * blocks, the pending approval card, the pinned `/runs` panel, the
 * typing spinner + live stream, then any global notices. Block seams
 * carry exactly one blank line; every line is wrapped to `width`.
 */
export function flattenTranscript(
  snapshot: TranscriptSnapshot | undefined,
  width: number,
  typingFrame: number,
  notices: readonly string[] = [],
  now: Date = new Date()
): string[] {
  const lines: string[] = []
  let tightNext = false
  const push = (line: string) => {
    const blank = line.trim().length === 0
    if (blank) {
      // Never two blanks in a row, never a leading blank.
      if (lines.length === 0 || lines[lines.length - 1] === "") {
        return
      }
      lines.push("")
      return
    }
    lines.push(line)
  }
  const firstContent = (rendered: readonly string[]): string | undefined =>
    rendered.find((line) => line.trim().length > 0)
  const looksLikeEvent = (rendered: readonly string[]): boolean => {
    const first = firstContent(rendered)
    return first !== undefined && /^\s*\*/.test(stripAnsi(first))
  }
  const pushAll = (rendered: readonly string[]) => {
    // One blank between blocks unless a side already provides it.
    // User echo sits tight into an event cluster — no extra seam —
    // but still breathes before an assistant answer / other chrome.
    const skipSeam = tightNext && looksLikeEvent(rendered)
    if (
      !skipSeam &&
      lines.length > 0 &&
      lines[lines.length - 1] !== "" &&
      rendered.length > 0 &&
      rendered[0].trim().length > 0
    ) {
      lines.push("")
    }
    tightNext = false
    for (const line of rendered) {
      push(line)
    }
  }
  if (snapshot) {
    const options = { expanded: snapshot.expanded === true }
    for (const block of snapshot.blocks) {
      if (block.kind === "message") {
        pushAll(renderMessage(block.message, width, options))
        if (block.message.role === "user") {
          tightNext = true
        }
      } else {
        pushAll(block.lines)
      }
    }
    if (snapshot.awaitingApproval) {
      pushAll(renderPendingPlan(snapshot.pendingPlan, width))
    }
    if (snapshot.runsFeed != null) {
      pushAll(renderRunsFeedPanel(snapshot.runsFeed, now))
    }
    if (snapshot.thinking) {
      pushAll(typingLines(typingFrame))
      pushAll(liveLines(snapshot.liveText, width))
    }
  }
  pushAll(notices)
  return lines.flatMap((line) => wrapVisible(line, width))
}

// ---------------------------------------------------------------------------
// ctrl+f transcript search — pure match + highlight math
// ---------------------------------------------------------------------------

/**
 * Line indexes of every flattened line containing `query` — plain
 * substring, case-insensitive, ANSI-stripped before matching (the
 * flattened lines carry SGR paint). A blank query matches nothing.
 */
export function findMatches(
  lines: readonly string[],
  query: string
): number[] {
  const needle = query.trim().toLowerCase()
  if (needle.length === 0) {
    return []
  }
  const matches: number[] = []
  for (let index = 0; index < lines.length; index += 1) {
    if (stripAnsi(lines[index] ?? "").toLowerCase().includes(needle)) {
      matches.push(index)
    }
  }
  return matches
}

/**
 * Cycles a match cursor by `step` (enter → +1, shift+enter → −1) around
 * a `count`-long match list; 0 when there is nothing to cycle.
 */
export function nextMatchIndex(
  current: number,
  count: number,
  step: number = 1
): number {
  if (count <= 0) {
    return 0
  }
  return (((current + step) % count) + count) % count
}

const INVERSE_ON = "\x1b[7m"
const INVERSE_OFF = "\x1b[27m"
const UNDERLINE_OFF = "\x1b[24m"
const SGR_SEQUENCE = /\x1b\[[0-9;]*m/g

/**
 * Wraps every occurrence of `query` in inverse video — underline joins
 * for the ACTIVE match, so `3/7`'s current one reads apart from the
 * rest. The wraps are additive SGR (on/off pairs), so the line's own
 * colours survive untouched. SGR-scoped (same escape class the
 * renderers emit); a blank query returns the line unchanged.
 */
export function highlightLine(
  line: string,
  query: string,
  active: boolean = false
): string {
  const needle = query.trim().toLowerCase()
  if (needle.length === 0) {
    return line
  }
  const on = active ? INVERSE_ON + colors.underline : INVERSE_ON
  const off = active ? INVERSE_OFF + UNDERLINE_OFF : INVERSE_OFF

  // Walk the raw line once: SGR runs are zero-width, every other code
  // point carries a plain index. `rawAt` maps plain → raw position.
  const rawAt: number[] = []
  let plain = ""
  let raw = 0
  while (raw < line.length) {
    SGR_SEQUENCE.lastIndex = raw
    const escape = SGR_SEQUENCE.exec(line)
    if (escape && escape.index === raw) {
      raw += escape[0].length
      continue
    }
    const char = String.fromCodePoint(line.codePointAt(raw) ?? 0x20)
    rawAt[plain.length] = raw
    plain += char
    raw += char.length
  }

  const lowered = plain.toLowerCase()
  const spans: Array<[start: number, end: number]> = []
  for (
    let from = lowered.indexOf(needle);
    from !== -1;
    from = lowered.indexOf(needle, from + needle.length)
  ) {
    spans.push([from, from + needle.length])
  }
  if (spans.length === 0) {
    return line
  }

  // Second walk rebuilds the line, flipping the wraps on/off at the
  // span boundaries — non-overlapping spans make one pass sufficient.
  let out = ""
  let spanIndex = 0
  let inside = false
  let plainIndex = 0
  raw = 0
  while (raw < line.length) {
    SGR_SEQUENCE.lastIndex = raw
    const escape = SGR_SEQUENCE.exec(line)
    if (escape && escape.index === raw) {
      out += escape[0]
      raw += escape[0].length
      continue
    }
    const span = spans[spanIndex]
    if (!inside && span && rawAt[span[0]] === raw) {
      out += on
      inside = true
    }
    const char = String.fromCodePoint(line.codePointAt(raw) ?? 0x20)
    out += char
    raw += char.length
    plainIndex += 1
    if (inside && plainIndex === span[1]) {
      out += off
      inside = false
      spanIndex += 1
    }
  }
  return out
}

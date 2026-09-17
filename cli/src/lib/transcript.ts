/**
 * Transcript flattening: the session's blocks → one array of finished
 * ANSI lines, in order, ready for the scrolling viewport. Everything
 * here is pure (no React, no Ink) — message rendering is reused from
 * `lib/format.ts`, the live stream from `lib/markdown.ts`, so the
 * viewport shows byte-identical text to what the components rendered.
 *
 * `wrapVisible` is the safety net of the fixed-height viewport: any
 * line wider than the terminal (long user echo, unwrappable word) is
 * ANSI-aware hard-wrapped so exactly `height` rendered rows fit the
 * budget and the footer/prompt can never be pushed off-screen.
 */
import { renderMessage, renderPendingPlan } from "./format"
import { renderMarkdownLines } from "./markdown"
import { colors, paint, symbols } from "../theme"
import type { ChatBlock } from "./sessions"

export const LIVE_CURSOR = "▌"
export const TYPING_LABEL = "comuki thinking"
export const APPROVAL_HINT = "  type approve or reject [reason] to decide"

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
 * block cursor `▌` riding the write head (the last line). Empty stream
 * renders nothing.
 */
export function liveLines(liveText: string, width: number): string[] {
  if (liveText.trim().length === 0) {
    return []
  }
  let lines = renderMarkdownLines(liveText, width)
  if (lines.length === 0) {
    lines = [""]
  }
  const last = lines.length - 1
  return lines.map((line, index) =>
    index === last ? line + paint(LIVE_CURSOR, colors.accent) : line
  )
}

/** `     ⠋ comuki thinking` — the spinner row for an in-flight turn. */
export function typingLine(frame: number, label: string = TYPING_LABEL): string {
  const spinner =
    symbols.spinnerFrames[
      ((frame % symbols.spinnerFrames.length) + symbols.spinnerFrames.length) %
        symbols.spinnerFrames.length
    ] ?? symbols.spinnerFrames[0]
  return `     ${paint(spinner, colors.accent)} ${label}`
}

// ---------------------------------------------------------------------------
// Flatten
// ---------------------------------------------------------------------------

/** The transcript-relevant slice of a session (see `Session` in sessions.ts). */
export interface TranscriptSnapshot {
  readonly blocks: readonly ChatBlock[]
  readonly awaitingApproval: boolean
  readonly pendingPlan: unknown
  readonly thinking: boolean
  readonly liveText: string
}

/**
 * Blocks → flat lines, in transcript order: history messages, raw line
 * blocks, the pending approval card, the typing spinner + live stream,
 * then any global notices. Every line is wrapped to `width`.
 */
export function flattenTranscript(
  snapshot: TranscriptSnapshot | undefined,
  width: number,
  typingFrame: number,
  notices: readonly string[] = []
): string[] {
  const lines: string[] = []
  if (snapshot) {
    for (const block of snapshot.blocks) {
      if (block.kind === "message") {
        lines.push(...renderMessage(block.message, width))
      } else {
        lines.push(...block.lines)
      }
    }
    if (snapshot.awaitingApproval) {
      lines.push(...renderPendingPlan(snapshot.pendingPlan))
      lines.push(paint(APPROVAL_HINT, colors.dim))
    }
    if (snapshot.thinking) {
      lines.push(typingLine(typingFrame))
      lines.push(...liveLines(snapshot.liveText, width))
    }
  }
  lines.push(...notices)
  return lines.flatMap((line) => wrapVisible(line, width))
}

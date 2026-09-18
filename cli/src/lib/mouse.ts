/**
 * SGR mouse tracking — Ink 5 has no mouse API, so the CLI owns the
 * DECSET enable/disable sequences and the CSI `<b;x;yM/m` parser.
 *
 * Coordinates are 1-based cells (the SGR mouse protocol). Button 0 is
 * the left button; higher bits encode modifiers / motion and are
 * ignored for v1 click targeting.
 */
import { stripAnsi } from "../theme"

/** Enable SGR mouse: click tracking + SGR coords + button-event (drag). */
export const ENABLE_MOUSE = "\x1b[?1000h\x1b[?1006h\x1b[?1002h"

/** Matching disable — always written on unmount so the terminal is left clean. */
export const DISABLE_MOUSE = "\x1b[?1000l\x1b[?1006l\x1b[?1002l"

export interface SgrMouseEvent {
  readonly button: number
  readonly x: number
  readonly y: number
  readonly press: boolean
}

/**
 * Parses one stdin chunk for an SGR mouse report. A chunk may carry
 * extra bytes after the terminator (keystrokes that arrived in the
 * same read); those are ignored. Returns null when the chunk is not
 * an SGR mouse sequence.
 */
export function parseSgrMouse(chunk: string): SgrMouseEvent | null {
  // Ink's parseKeypress strips the leading ESC on some CSI forms, so
  // the same report may arrive as `\x1b[<…M` or `[<…M`.
  const match = /(?:\x1b)?\[<(\d+);(\d+);(\d+)([Mm])/.exec(chunk)
  if (!match) {
    return null
  }
  const button = Number(match[1])
  const x = Number(match[2])
  const y = Number(match[3])
  if (
    !Number.isFinite(button) ||
    !Number.isFinite(x) ||
    !Number.isFinite(y) ||
    x < 1 ||
    y < 1
  ) {
    return null
  }
  return { button, x, y, press: match[4] === "M" }
}

/**
 * True when a stdin chunk is an SGR mouse report. Ink's `useInput` may
 * strip the leading ESC, so we also accept the CSI body alone.
 */
export function isSgrMouseChunk(chunk: string): boolean {
  return parseSgrMouse(chunk) !== null
}

export interface TabHitSegment {
  readonly index: number
  readonly start: number
  readonly end: number
}

export interface TabHitSession {
  readonly name: string
  readonly awaitingApproval: boolean
  readonly unread: boolean
}

export type MouseTarget =
  | { readonly kind: "tab"; readonly index: number }
  | { readonly kind: "approve" }
  | { readonly kind: "reject" }
  | { readonly kind: "none" }

export interface MouseLayout {
  /** 1-based row of the tab strip; null when the strip is not painted. */
  readonly tabRow: number | null
  readonly sessions: readonly TabHitSession[]
  /** 1-based row of the first transcript viewport line. */
  readonly transcriptTop: number
  readonly hasHint: boolean
  readonly visibleLines: readonly string[]
  readonly awaitingApproval: boolean
}

/**
 * TabBar paints a two-space indent, then up to 9 tabs separated by a
 * single space, then ` [+]`. Segments are 1-based cell ranges so they
 * compare directly with SGR `x`.
 */
export function tabHitSegments(
  sessions: readonly TabHitSession[]
): readonly TabHitSegment[] {
  const visible = sessions.slice(0, 9)
  const segments: TabHitSegment[] = []
  // Two-space indent → first tab starts at cell 3 (1-based).
  let cursor = 3
  for (let index = 0; index < visible.length; index++) {
    const session = visible[index]
    if (!session) {
      continue
    }
    if (index > 0) {
      cursor += 1
    }
    const width = tabCellWidth(index, session)
    segments.push({ index, start: cursor, end: cursor + width })
    cursor += width
  }
  return segments
}

/** Width of `[n] name` plus badges — `n` is 1-based so index 8 is `[9]`. */
export function tabCellWidth(index: number, session: TabHitSession): number {
  return (
    `[${index + 1}] ${session.name}`.length +
    (session.awaitingApproval ? 2 : 0) +
    (session.unread ? 2 : 0)
  )
}

/** Tab index under 1-based `x`, or null when the click missed every tab. */
export function hitTestTab(
  x: number,
  sessions: readonly TabHitSession[]
): number | null {
  for (const segment of tabHitSegments(sessions)) {
    if (x >= segment.start && x < segment.end) {
      return segment.index
    }
  }
  return null
}

/**
 * Approve / reject click on a visible transcript line. The plan card
 * paints `approve · reject [reason]`; a click on a line that contains
 * the word maps to the matching slash. Prefer the word whose start is
 * closer to `x` when both appear (they always do on the hint line).
 */
export function hitTestApproval(
  line: string,
  x: number
): "approve" | "reject" | null {
  const plain = stripVisible(line)
  const approveAt = wordStart(plain, "approve")
  const rejectAt = wordStart(plain, "reject")
  if (approveAt < 0 && rejectAt < 0) {
    return null
  }
  // SGR x is 1-based; line cells are 0-based.
  const cell = x - 1
  if (approveAt >= 0 && rejectAt >= 0) {
    const approveEnd = approveAt + "approve".length
    const rejectEnd = rejectAt + "reject".length
    const inApprove = cell >= approveAt && cell < approveEnd
    const inReject = cell >= rejectAt && cell < rejectEnd
    if (inApprove && !inReject) {
      return "approve"
    }
    if (inReject && !inApprove) {
      return "reject"
    }
    // Click on the ` · ` gutter — closer edge of the two words wins.
    const approveDist = distanceToSpan(cell, approveAt, approveEnd)
    const rejectDist = distanceToSpan(cell, rejectAt, rejectEnd)
    return approveDist <= rejectDist ? "approve" : "reject"
  }
  return approveAt >= 0 ? "approve" : "reject"
}

/**
 * Maps a click onto a tab or the approve/reject hint. Status-bar clicks
 * (and anything else) resolve to `none` — v1 does nothing with them.
 */
export function resolveMouseClick(
  click: { readonly x: number; readonly y: number },
  layout: MouseLayout
): MouseTarget {
  if (layout.tabRow !== null && click.y === layout.tabRow) {
    const index = hitTestTab(click.x, layout.sessions)
    return index === null ? { kind: "none" } : { kind: "tab", index }
  }
  if (!layout.awaitingApproval || layout.visibleLines.length === 0) {
    return { kind: "none" }
  }
  const localY = click.y - layout.transcriptTop
  if (localY < 0) {
    return { kind: "none" }
  }
  const lineIndex = localY - (layout.hasHint ? 1 : 0)
  if (lineIndex < 0 || lineIndex >= layout.visibleLines.length) {
    return { kind: "none" }
  }
  const line = layout.visibleLines[lineIndex]
  if (line === undefined) {
    return { kind: "none" }
  }
  const hit = hitTestApproval(line, click.x)
  return hit === null ? { kind: "none" } : { kind: hit }
}

/** Visible (ANSI-stripped) prefix of a line, used for hit-testing. */
function stripVisible(text: string): string {
  return stripAnsi(text)
}

function wordStart(haystack: string, needle: string): number {
  return haystack.indexOf(needle)
}

function distanceToSpan(cell: number, start: number, end: number): number {
  if (cell < start) {
    return start - cell
  }
  if (cell >= end) {
    return cell - (end - 1)
  }
  return 0
}

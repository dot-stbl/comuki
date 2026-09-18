/**
 * The scrolling transcript viewport — a fixed-height window over the
 * flattened transcript lines. All the geometry lives in the pure
 * `lib/viewport.ts`; this component only paints the sliced window,
 * the dim ctrl+o expand hint pinned as the viewport's first row
 * (while collapsed thinking blocks exist) and, while follow is
 * suspended with fresh output below, the dim `↓ new messages`
 * indicator as the last line, and — while ctrl+f search is open —
 * the inverse-video match highlights (`lib/transcript.ts`). Each
 * reserved row is taken out of the content budget, so none steal a
 * transcript line.
 */
import { Box, Text } from "ink"
import React from "react"
import { NEW_MESSAGES_INDICATOR, viewportSlice } from "../lib/viewport"
import { highlightLine } from "../lib/transcript"

/** What the viewport highlights while the ctrl+f search is open. */
export interface TranscriptHighlight {
  /** The live query (ANSI-stripped, case-insensitive, substring). */
  readonly query: string
  /** Absolute line index of the active match; null when none. */
  readonly activeLine: number | null
}

export interface TranscriptViewportProps {
  /** Flattened, wrapped transcript lines (`flattenTranscript`). */
  readonly lines: readonly string[]
  /** Viewport height in terminal rows. */
  readonly height: number
  /** Lines hidden below the window; 0 = following the bottom. */
  readonly offset: number
  /** New output arrived while `offset > 0` — show the indicator. */
  readonly newBelow: boolean
  /** Dim hint line pinned above the window (ctrl+o expand hint); null = off. */
  readonly hint?: string | null
  /** ctrl+f match highlighting; null/absent = off. */
  readonly highlight?: TranscriptHighlight | null
}

export function TranscriptViewport({
  lines,
  height,
  offset,
  newBelow,
  hint = null,
  highlight = null,
}: TranscriptViewportProps) {
  const indicator = offset > 0 && newBelow
  const budget = height - (indicator ? 1 : 0) - (hint !== null ? 1 : 0)
  const visible = viewportSlice(lines, budget, offset)
  // The absolute index of the first visible row — the highlight's
  // activeLine speaks in transcript coordinates, not window rows.
  const firstIndex = Math.max(0, lines.length - offset - visible.length)
  const marking = highlight !== null && highlight.query.trim().length > 0
  return (
    <Box flexDirection="column">
      {hint !== null ? <Text>{hint}</Text> : null}
      {visible.map((line, index) => (
        <Text key={index}>
          {marking
            ? highlightLine(
                line,
                highlight.query,
                firstIndex + index === highlight.activeLine
              )
            : line}
        </Text>
      ))}
      {indicator ? (
        <Text dimColor>{"  " + NEW_MESSAGES_INDICATOR}</Text>
      ) : null}
    </Box>
  )
}

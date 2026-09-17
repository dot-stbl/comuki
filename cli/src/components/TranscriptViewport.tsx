/**
 * The scrolling transcript viewport — a fixed-height window over the
 * flattened transcript lines. All the geometry lives in the pure
 * `lib/viewport.ts`; this component only paints the sliced window,
 * the dim ctrl+o expand hint pinned as the viewport's first row
 * (while collapsed thinking blocks exist), and, while follow is
 * suspended with fresh output below, the dim `↓ new messages`
 * indicator as the last line. Each reserved row is taken out of the
 * content budget, so neither steals a transcript line.
 */
import { Box, Text } from "ink"
import React from "react"
import { NEW_MESSAGES_INDICATOR, viewportSlice } from "../lib/viewport"

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
}

export function TranscriptViewport({
  lines,
  height,
  offset,
  newBelow,
  hint = null,
}: TranscriptViewportProps) {
  const indicator = offset > 0 && newBelow
  const budget = height - (indicator ? 1 : 0) - (hint !== null ? 1 : 0)
  const visible = viewportSlice(lines, budget, offset)
  return (
    <Box flexDirection="column">
      {hint !== null ? <Text>{hint}</Text> : null}
      {visible.map((line, index) => (
        <Text key={index}>{line}</Text>
      ))}
      {indicator ? (
        <Text dimColor>{"  " + NEW_MESSAGES_INDICATOR}</Text>
      ) : null}
    </Box>
  )
}

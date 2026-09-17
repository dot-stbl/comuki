/**
 * The scrolling transcript viewport — a fixed-height window over the
 * flattened transcript lines. All the geometry lives in the pure
 * `lib/viewport.ts`; this component only paints the sliced window and,
 * while follow is suspended with fresh output below, the dim
 * `↓ new messages` indicator as the viewport's last line (it reserves
 * one row of the window, so the newest hidden line is never claimed).
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
}

export function TranscriptViewport({
  lines,
  height,
  offset,
  newBelow,
}: TranscriptViewportProps) {
  const indicator = offset > 0 && newBelow
  const visible = viewportSlice(
    lines,
    indicator ? height - 1 : height,
    offset
  )
  return (
    <Box flexDirection="column">
      {visible.map((line, index) => (
        <Text key={index}>{line}</Text>
      ))}
      {indicator ? (
        <Text dimColor>{"  " + NEW_MESSAGES_INDICATOR}</Text>
      ) : null}
    </Box>
  )
}

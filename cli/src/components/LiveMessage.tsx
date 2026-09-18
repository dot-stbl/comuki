/**
 * The growing assistant block while a turn streams in: the live chunk
 * tail rendered through the markdown path (`liveLines` in
 * `lib/transcript.ts` — the same source the scrolling viewport
 * flattens), with a trailing ASCII cursor `_` marking the write head.
 * The finalized message never passes through here — on turn complete
 * the REST result replaces the live block with a plain `ChatMessage`.
 *
 * The rendered tail is clipped to the last `maxLines` lines (with a
 * dim `…` leader) so a long stream cannot push the prompt off-screen;
 * `liveText` itself is capped at 4000 chars in `lib/sessions.ts`.
 */
import { Box, Text } from "ink"
import React from "react"
import { liveLines, LIVE_CURSOR } from "../lib/transcript"

export { LIVE_CURSOR }
export const LIVE_MAX_LINES = 12

export interface LiveMessageProps {
  readonly liveText: string
  readonly width?: number
  readonly maxLines?: number
}

export function LiveMessage({
  liveText,
  width = 80,
  maxLines = LIVE_MAX_LINES,
}: LiveMessageProps) {
  if (liveText.trim().length === 0) {
    return null
  }
  let lines = liveLines(liveText, width)
  const clipped = lines.length > maxLines
  if (clipped) {
    lines = lines.slice(lines.length - maxLines)
  }
  return (
    <Box flexDirection="column">
      {clipped ? <Text dimColor>  ...</Text> : null}
      {lines.map((line, index) => (
        <Text key={index}>{line}</Text>
      ))}
    </Box>
  )
}

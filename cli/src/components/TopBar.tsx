/** One quiet orientation row above the conversation. */
import { Box, Text } from "ink"
import React from "react"
import type { HarnessMode } from "../lib/harness-layout"
import { MARK_COMPACT } from "../lib/mark"
import { palette } from "../theme"

export interface TopBarProps {
  readonly width: number
  readonly mode: HarnessMode
  readonly session?: string
  readonly activity?: string
  readonly attentionCount?: number
}

export function TopBar({
  width,
  mode,
  session,
  activity,
  attentionCount = 0,
}: TopBarProps) {
  const left =
    mode === "compact"
      ? `comuki / ${session ?? "new session"}`
      : `${MARK_COMPACT}  ${session ?? "new session"}`
  const right = [
    activity,
    attentionCount > 0 ? `${attentionCount} need attention` : undefined,
  ]
    .filter((value): value is string => value !== undefined)
    .join(" / ")

  return (
    <Box width={width} height={1} justifyContent="space-between">
      <Text bold color={palette.text}>
        {fitTail(left, Math.max(1, width - right.length - 3))}
      </Text>
      {right.length > 0 ? (
        <Text dimColor>{fitTail(right, Math.floor(width * 0.4))}</Text>
      ) : null}
    </Box>
  )
}

/** Tail truncation is predictable in a fixed terminal cell grid. */
export function fitTail(value: string, width: number): string {
  if (width <= 0) {
    return ""
  }
  if (value.length <= width) {
    return value
  }
  if (width <= 3) {
    return value.slice(0, width)
  }
  return `${value.slice(0, width - 3)}...`
}

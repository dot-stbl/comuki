/** Stable two-zone chrome for the full-screen chat harness. */
import { Box, Text } from "ink"
import React from "react"
import { contextMeterLabel, DEFAULT_CONTEXT_WINDOW } from "../lib/context"
import type { HarnessMode } from "../lib/harness-layout"
import type { HubConnectionState } from "../lib/signalr"
import { MARK_COMPACT } from "../lib/mark"
import { palette } from "../theme"
import {
  connectionLabel,
  hostFromUrl,
  latencyColor,
  latencyLabel,
  latencyTone,
} from "./StatusLine"
import { Fill } from "./Fill"

export interface TopBarProps {
  readonly width: number
  readonly mode: HarnessMode
  readonly session?: string
  readonly identity: string
  readonly project?: string
  readonly profile?: string
  readonly connection: HubConnectionState
  readonly serverUrl: string
  readonly latencyMs?: number | null
  readonly contextUsed?: number
  readonly contextWindow?: number
  readonly tick?: number
}

export function TopBar({
  width,
  mode,
  session,
  identity,
  project,
  profile,
  connection,
  serverUrl,
  latencyMs,
  contextUsed,
  contextWindow,
  tick = 0,
}: TopBarProps) {
  const host = hostFromUrl(serverUrl) ?? "host unavailable"
  const sessionLabel = session ?? "new session"
  const primaryLeft =
    mode === "compact"
      ? `${MARK_COMPACT} comuki / ${sessionLabel}`
      : `${MARK_COMPACT}  comuki  /  ${sessionLabel}`
  const connectionText = `[${connectionLabel(connection, tick)}]`
  const primaryRight =
    typeof latencyMs === "number"
      ? `${connectionText} ${latencyLabel(latencyMs)}`
      : connectionText
  const secondaryLeft =
    mode === "compact"
      ? `${project ?? "no project"} / ${profile ?? identity}`
      : `project ${project ?? "none"}  /  profile ${profile ?? "default"}  /  ${identity}`
  const secondaryRight =
    typeof contextUsed === "number"
      ? contextMeterLabel(
          contextUsed,
          contextWindow ?? DEFAULT_CONTEXT_WINDOW
        )
      : host

  return (
    <Fill width={width} height={2} color={palette.rail}>
      <Box flexDirection="column" width={width} height={2}>
        <ChromeRow
          width={width}
          left={primaryLeft}
          right={primaryRight}
          strong
          rightColor={
            typeof latencyMs === "number"
              ? latencyColor(latencyTone(latencyMs))
              : connection === "live"
                ? palette.ok
                : connection === "reconnecting"
                  ? palette.waiting
                  : undefined
          }
        />
        <ChromeRow
          width={width}
          left={secondaryLeft}
          right={secondaryRight}
        />
      </Box>
    </Fill>
  )
}

interface ChromeRowProps {
  readonly width: number
  readonly left: string
  readonly right: string
  readonly strong?: boolean
  readonly rightColor?: string
}

function ChromeRow({
  width,
  left,
  right,
  strong = false,
  rightColor,
}: ChromeRowProps) {
  const room = Math.max(1, width - 4)
  const rightText = fitTail(right, Math.max(0, Math.floor(room * 0.38)))
  const leftRoom = Math.max(1, room - rightText.length - 1)
  const leftText = fitTail(left, leftRoom)
  const gap = Math.max(1, room - leftText.length - rightText.length)
  return (
    <Text backgroundColor={palette.rail}>
      {"  "}
      <Text bold={strong} color={strong ? palette.text : undefined}>
        {leftText}
      </Text>
      {" ".repeat(gap)}
      <Text color={rightColor} dimColor={rightColor === undefined}>
        {rightText}
      </Text>
      {"  "}
    </Text>
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

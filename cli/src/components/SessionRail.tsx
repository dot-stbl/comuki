/** Wide-terminal session navigation with explicit text statuses. */
import { Box, Text } from "ink"
import React from "react"
import type { Session } from "../lib/sessions"
import { palette } from "../theme"
import { Fill } from "./Fill"

export interface SessionRailProps {
  readonly sessions: readonly Session[]
  readonly activeIndex: number
  readonly width: number
  readonly height: number
}

export function SessionRail({
  sessions,
  activeIndex,
  width,
  height,
}: SessionRailProps) {
  return (
    <Fill width={width} height={height} color={palette.lane}>
      <Box width={width} height={height} flexDirection="column">
        <Text bold backgroundColor={palette.lane}>{"  sessions"}</Text>
        <Text dimColor backgroundColor={palette.lane}>{`  ${sessions.length} open / tab cycles`}</Text>
        <Box marginTop={1} flexDirection="column">
          {sessions.slice(0, Math.max(0, height - 5)).map((session, index) => {
            const active = index === activeIndex
            const state = session.awaitingApproval
              ? "approval"
              : session.status === "thinking"
                ? "thinking"
                : session.unread
                  ? "unread"
                  : session.status
            const nameRoom = Math.max(4, width - 6)
            const name =
              session.name.length > nameRoom
                ? `${session.name.slice(0, Math.max(1, nameRoom - 3))}...`
                : session.name
            return (
              <Box key={session.id} flexDirection="column" width={width}>
                <Text
                  bold={active}
                  color={active ? palette.brand : undefined}
                  dimColor={!active}
                  backgroundColor={active ? palette.raised : palette.lane}
                >
                  {`${active ? ">" : " "} [${index + 1}] ${name}`.padEnd(width)}
                </Text>
                <Text
                  color={
                    state === "approval"
                      ? palette.waiting
                      : state === "thinking"
                        ? palette.brand
                        : undefined
                  }
                  dimColor={state !== "approval" && state !== "thinking"}
                  backgroundColor={active ? palette.raised : palette.lane}
                >
                  {`      ${state}`.padEnd(width)}
                </Text>
              </Box>
            )
          })}
        </Box>
        <Box flexGrow={1} />
        <Text dimColor backgroundColor={palette.lane}>{"  ctrl+n new"}</Text>
        <Text dimColor backgroundColor={palette.lane}>{"  esc overview"}</Text>
      </Box>
    </Fill>
  )
}

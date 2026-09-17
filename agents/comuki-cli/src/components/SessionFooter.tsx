/**
 * Bottom line: mini badges for every session (dot = needs attention)
 * with the hotkey legend on the right. The active badge stays bright;
 * a yellow dot marks unread output, an accent dot a thinking turn.
 */
import { Box, Text } from "ink"
import React from "react"
import type { Session } from "../lib/sessions"

export interface SessionFooterProps {
  readonly sessions: readonly Session[]
  readonly activeIndex: number
}

export function SessionFooter({ sessions, activeIndex }: SessionFooterProps) {
  return (
    <Box>
      <Text dimColor>{"  "}</Text>
      {sessions.slice(0, 9).map((session, index) => {
        const active = index === activeIndex
        const dot =
          session.status === "thinking" ? (
            <Text color="#8787f3">●</Text>
          ) : session.unread ? (
            <Text color="yellow">●</Text>
          ) : (
            <Text dimColor>○</Text>
          )
        return (
          <React.Fragment key={session.id}>
            {index > 0 ? <Text> </Text> : null}
            <Text dimColor={!active} bold={active}>
              {dot}
              {index + 1} {session.name.slice(0, 12)}
            </Text>
          </React.Fragment>
        )
      })}
      <Text dimColor> esc · tab · ctrl+n</Text>
    </Box>
  )
}

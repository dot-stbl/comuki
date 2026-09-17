/**
 * Bottom line: mini badges for every session (dot = needs attention) with
 * the hotkey legend pushed to the right edge. The active badge stays
 * bright; a yellow dot marks unread output, an accent dot a thinking
 * turn.
 *
 * Renders full-width: badges left-aligned, legend right-aligned via the
 * flex `space-between` parent. The row stays one terminal line tall.
 */
import { Box, Text } from "ink"
import React from "react"
import type { Session } from "../lib/sessions"
import { palette } from "../theme"

export interface SessionFooterProps {
  readonly sessions: readonly Session[]
  readonly activeIndex: number
}

export function SessionFooter({ sessions, activeIndex }: SessionFooterProps) {
  return (
    <Box width="100%" justifyContent="space-between">
      <Box>
        <Text dimColor>{"  "}</Text>
        {sessions.slice(0, 9).map((session, index) => {
          const active = index === activeIndex
          const dot =
            session.status === "thinking" ? (
              <Text color={palette.brand}>●</Text>
            ) : session.unread ? (
              <Text color={palette.waiting}>●</Text>
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
      </Box>
      <Text dimColor> esc · tab · pgup/pgdn · ctrl+n </Text>
    </Box>
  )
}

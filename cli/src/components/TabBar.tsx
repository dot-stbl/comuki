/**
 * Session tab strip — full-width row at the top of the chat: `[1] identity-refactor
 * [2] readme-fix [+]`. Terminal-native (no box chrome): the active tab
 * wears the accent color and bold, others dim, a yellow dot marks
 * unread output, `+` hints ctrl+n.
 *
 * The strip self-fills the parent width via `<Box width="100%">` so the
 * trailing labels stay anchored left regardless of how wide the parent
 * flex container is.
 */
import { Box, Text } from "ink"
import React from "react"
import type { Session } from "../lib/sessions"
import { palette } from "../theme"

export interface TabBarProps {
  readonly sessions: readonly Session[]
  readonly activeIndex: number
}

export function TabBar({ sessions, activeIndex }: TabBarProps) {
  return (
    <Box width="100%" justifyContent="flex-start">
      <Text>{"  "}</Text>
      {sessions.slice(0, 9).map((session, index) => {
        const active = index === activeIndex
        const label = `[${index + 1}] ${session.name}`
        return (
          <React.Fragment key={session.id}>
            {index > 0 ? <Text> </Text> : null}
            <Text
              color={active ? palette.brand : undefined}
              dimColor={!active}
              bold={active}
            >
              {label}
              {session.unread ? <Text color="yellow"> ●</Text> : null}
            </Text>
          </React.Fragment>
        )
      })}
      <Text dimColor> [+]</Text>
    </Box>
  )
}

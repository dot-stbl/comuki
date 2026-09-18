/**
 * Session tab strip — full-width row at the top of the chat: `[1] identity-refactor
 * [2] readme-fix [+]`. Terminal-native (no box chrome): the active tab
 * wears the accent color and bold, others dim, a yellow dot marks
 * unread output, an accent `!` marks a plan awaiting approval, `+`
 * hints ctrl+n.
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

/**
 * The `!` badge a tab wears while its plan awaits an approval
 * decision — the same mark the esc overview groups under "waiting
 * approval". Pure: `null` when there is nothing to decide.
 */
export function approvalBadge(
  session: Pick<Session, "awaitingApproval">
): string | null {
  return session.awaitingApproval ? "!" : null
}

export function TabBar({ sessions, activeIndex }: TabBarProps) {
  return (
    <Box width="100%" justifyContent="flex-start">
      <Text>{"  "}</Text>
      {sessions.slice(0, 9).map((session, index) => {
        const active = index === activeIndex
        const label = `[${index + 1}] ${session.name}`
        const badge = approvalBadge(session)
        return (
          <React.Fragment key={session.id}>
            {index > 0 ? <Text> </Text> : null}
            <Text
              color={active ? palette.brand : undefined}
              dimColor={!active}
              bold={active}
            >
              {label}
              {badge !== null ? (
                <Text color={palette.brand}> !</Text>
              ) : null}
              {session.unread ? <Text color={palette.waiting}> ●</Text> : null}
            </Text>
          </React.Fragment>
        )
      })}
      <Text dimColor> [+]</Text>
    </Box>
  )
}

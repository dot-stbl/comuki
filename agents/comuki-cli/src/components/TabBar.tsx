/**
 * Session tab strip — `[1] identity-refactor [2] readme-fix [+]`.
 * Terminal-native (no box chrome): active tab in the accent color,
 * others dimmed, a yellow dot marks unread output, `+` hints ctrl+n.
 */
import { Text } from "ink"
import React from "react"
import type { Session } from "../lib/sessions"

export interface TabBarProps {
  readonly sessions: readonly Session[]
  readonly activeIndex: number
}

export function TabBar({ sessions, activeIndex }: TabBarProps) {
  const tabs = sessions.slice(0, 9).map((session, index) => {
    const active = index === activeIndex
    const label = `[${index + 1}] ${session.name}`
    return (
      <React.Fragment key={session.id}>
        {index > 0 ? <Text> </Text> : null}
        <Text
          color={active ? "#8787f3" : undefined}
          dimColor={!active}
          bold={active}
        >
          {label}
          {session.unread ? <Text color="yellow"> ●</Text> : null}
        </Text>
      </React.Fragment>
    )
  })
  return (
    <Text>
      {"  "}
      {tabs}
      <Text dimColor> [+]</Text>
    </Text>
  )
}

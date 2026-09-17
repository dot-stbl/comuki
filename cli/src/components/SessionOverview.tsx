/**
 * The `esc` overlay: every open session in one list with status and
 * age, `+ new session` at the bottom. Keys: `1`–`9` select, `ctrl+n`
 * new, `esc` back. Rendered instead of the transcript while open
 * (terminal-native: no floating panes, the list IS the screen).
 */
import { Box, Text, useInput } from "ink"
import React from "react"
import { ageFromMs, padVisible } from "../lib/format"
import type { Session } from "../lib/sessions"
import { palette } from "../theme"

export interface SessionOverviewProps {
  readonly sessions: readonly Session[]
  readonly activeIndex: number
  readonly onSelect: (index: number) => void
  readonly onNewSession: () => void
  readonly onClose: () => void
}

function statusGlyph(status: Session["status"]): {
  readonly glyph: string
  readonly color: string | undefined
} {
  switch (status) {
    case "thinking":
      return { glyph: "● thinking", color: palette.brand }
    case "running":
      return { glyph: "● running", color: "yellow" }
    case "done":
      return { glyph: "✓ done", color: "green" }
    default:
      return { glyph: "○ idle", color: undefined }
  }
}

export function SessionOverview({
  sessions,
  activeIndex,
  onSelect,
  onNewSession,
  onClose,
}: SessionOverviewProps) {
  useInput((input, key) => {
    if (key.escape) {
      onClose()
      return
    }
    if (key.ctrl && input === "n") {
      onNewSession()
      return
    }
    if (!key.ctrl && !key.meta && input >= "1" && input <= "9") {
      const index = Number(input) - 1
      if (index < sessions.length) {
        onSelect(index)
      }
    }
  })

  return (
    <Box flexDirection="column" alignItems="center" paddingY={1}>
      <Text bold color={palette.brand}>
        SESSIONS
      </Text>
      <Box flexDirection="column" paddingTop={1}>
        {sessions.slice(0, 9).map((session, index) => {
          const glyph = statusGlyph(session.status)
          const active = index === activeIndex
          return (
            <Text key={session.id} dimColor={!active} bold={active}>
              {`  ${padVisible(String(index + 1), 3)}${padVisible(session.name, 22)}`}
              <Text color={glyph.color}>{padVisible(glyph.glyph, 12)}</Text>
              {`${session.unread ? "● " : ""}${ageFromMs(Date.now() - session.createdAt)} ago`}
            </Text>
          )
        })}
        <Text dimColor>{"   + new session"}</Text>
      </Box>
      <Box paddingTop={1}>
        <Text dimColor>1-9 select · ctrl+n new · esc back</Text>
      </Box>
    </Box>
  )
}

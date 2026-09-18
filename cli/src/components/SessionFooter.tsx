/**
 * Bottom chrome: session badges on the left, a compact expand hint on
 * the right. Expanded, a dim top rule plus one row per action sits
 * above the prompt — height is subtracted from the transcript viewport
 * the same way PromptInput rows are.
 *
 * Keyboard (arrows / enter / esc) and mouse live in the shell; this
 * component is presentational. Dichromat: the highlight is brand
 * (lavender) plus bold, never colour-alone.
 */
import { Box, Text } from "ink"
import React from "react"
import type { FooterAction } from "../lib/footer-actions"
import { FOOTER_EXPAND_HINT } from "../lib/footer-actions"
import type { Session } from "../lib/sessions"
import { palette } from "../theme"

export interface SessionFooterProps {
  readonly sessions: readonly Session[]
  readonly activeIndex: number
  readonly expanded: boolean
  readonly selectedIndex: number
  readonly actions: readonly FooterAction[]
  readonly onToggle: () => void
  readonly onSelect: (index: number) => void
  readonly onActivate: (id: string) => void
  readonly onMouseClick?: (x: number, y: number) => void
}

export function SessionFooter({
  sessions,
  activeIndex,
  expanded,
  selectedIndex,
  actions,
}: SessionFooterProps) {
  return (
    <Box flexDirection="column" width="100%">
      {expanded ? (
        <ExpandedList actions={actions} selectedIndex={selectedIndex} />
      ) : null}
      <CollapsedRow sessions={sessions} activeIndex={activeIndex} />
    </Box>
  )
}

function CollapsedRow({
  sessions,
  activeIndex,
}: {
  readonly sessions: readonly Session[]
  readonly activeIndex: number
}) {
  return (
    <Box width="100%" justifyContent="space-between">
      <Box>
        <Text dimColor backgroundColor={palette.rail}>
          {"  "}
        </Text>
        {sessions.slice(0, 9).map((session, index) => {
          const active = index === activeIndex
          const dot =
            session.status === "thinking" ? (
              <Text color={palette.brand} backgroundColor={palette.rail}>
                o
              </Text>
            ) : session.unread ? (
              <Text color={palette.waiting} backgroundColor={palette.rail}>
                o
              </Text>
            ) : (
              <Text dimColor backgroundColor={palette.rail}>
                .
              </Text>
            )
          return (
            <React.Fragment key={session.id}>
              {index > 0 ? (
                <Text backgroundColor={palette.rail}> </Text>
              ) : null}
              <Text
                dimColor={!active}
                bold={active}
                backgroundColor={palette.rail}
              >
                {dot}
                {index + 1} {session.name.slice(0, 12)}
              </Text>
            </React.Fragment>
          )
        })}
      </Box>
      <Text dimColor backgroundColor={palette.rail}>
        {` ${FOOTER_EXPAND_HINT} `}
      </Text>
    </Box>
  )
}

function ExpandedList({
  actions,
  selectedIndex,
}: {
  readonly actions: readonly FooterAction[]
  readonly selectedIndex: number
}) {
  return (
    <Box flexDirection="column" width="100%">
      <Text bold backgroundColor={palette.raised}>
        {"  actions"}
        <Text dimColor backgroundColor={palette.raised}>
          {"  arrows select / enter run / esc close"}
        </Text>
      </Text>
      {actions.map((action, index) => {
        const selected = index === selectedIndex
        const hint = action.hint === undefined ? "" : `  ${action.hint}`
        return (
          <Text
            key={action.id}
            dimColor={!selected}
            backgroundColor={selected ? palette.raised : palette.rail}
          >
            {selected ? (
              <Text color={palette.brand} bold backgroundColor={palette.raised}>
                {`  > ${action.label}`}
              </Text>
            ) : (
              `    ${action.label}`
            )}
            {hint.length > 0 ? (
              <Text dimColor backgroundColor={palette.rail}>
                {hint}
              </Text>
            ) : null}
          </Text>
        )
      })}
    </Box>
  )
}

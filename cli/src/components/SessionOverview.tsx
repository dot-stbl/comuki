/**
 * The `esc` overlay: every open session in one list with status and
 * age, `+ new session` at the bottom. Keys: `1`–`9` select, `ctrl+n`
 * new, `esc` back. Rendered instead of the transcript while open
 * (terminal-native: no floating panes, the list IS the screen).
 *
 * Sessions with a plan awaiting decision lead the screen under
 * `! waiting approval` (the same badge the tab strip wears); each row
 * also carries its summed token usage, right-aligned and dim.
 */
import { Box, Text, useInput } from "ink"
import React, { useState } from "react"
import {
  ageFromMs,
  extractPlanNodes,
  padVisible,
  truncateTail,
} from "../lib/format"
import { isSgrMouseChunk } from "../lib/mouse"
import { filterSessions, type ChatBlock, type Session } from "../lib/sessions"
import { palette } from "../theme"
import { Fill } from "./Fill"

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
      return { glyph: "o thinking", color: palette.brand }
    case "running":
      return { glyph: "o running", color: palette.waiting }
    case "done":
      return { glyph: "✓ done", color: palette.ok }
    default:
      return { glyph: ". idle", color: undefined }
  }
}

/** Fixed row width — the token column right-aligns against it. */
const OVERVIEW_WIDTH = 64

// ---------------------------------------------------------------------------
// Pure derivations — token totals + the waiting-approval section
// ---------------------------------------------------------------------------

/** Summed token usage of one session's message metas. */
export interface TokenTotals {
  readonly tokensIn: number
  readonly tokensOut: number
}

/**
 * Sums `meta.tokensIn` / `meta.tokensOut` over a session's message
 * blocks (the per-message wire metas). Null when no message carried
 * token numbers — nothing to render in the usage column.
 */
export function sessionTokenTotals(
  blocks: readonly ChatBlock[]
): TokenTotals | null {
  let tokensIn = 0
  let tokensOut = 0
  let seen = false
  for (const block of blocks) {
    if (block.kind !== "message") {
      continue
    }
    const meta = block.message.meta
    if (meta === null) {
      continue
    }
    if (typeof meta.tokensIn === "number") {
      tokensIn += meta.tokensIn
      seen = true
    }
    if (typeof meta.tokensOut === "number") {
      tokensOut += meta.tokensOut
      seen = true
    }
  }
  return seen ? { tokensIn, tokensOut } : null
}

function tokensCompact(total: number): string {
  return total < 1000 ? String(total) : `${(total / 1000).toFixed(1)}k`
}

/** `4.1k→1.2k` — the dim right-aligned usage column of an overview row. */
export function formatTokenTotals(totals: TokenTotals): string {
  return `${tokensCompact(totals.tokensIn)}→${tokensCompact(totals.tokensOut)}`
}

/** One `waiting approval` row — the tab to jump to plus its first plan step. */
export interface WaitingApprovalRow {
  /** Index into the session list — the `1`–`9` key that jumps there. */
  readonly index: number
  readonly name: string
  readonly firstStep: string
}

/**
 * Sessions whose plan awaits a decision, in tab order, each with the
 * first step of its pending plan as the teaser (collapsed to one
 * line). The number keys the overview already handles do the jump.
 */
export function waitingApprovalRows(
  sessions: readonly Session[]
): WaitingApprovalRow[] {
  const rows: WaitingApprovalRow[] = []
  for (let index = 0; index < sessions.length; index++) {
    const session = sessions[index]
    if (!session.awaitingApproval) {
      continue
    }
    const brief = extractPlanNodes(session.pendingPlan)[0]?.brief ?? ""
    const firstStep = brief.split("\n", 1)[0]?.trim() ?? ""
    rows.push({
      index,
      name: session.name,
      firstStep: firstStep.length > 0 ? firstStep : "(no plan)",
    })
  }
  return rows
}

// ---------------------------------------------------------------------------

export function SessionOverview({
  sessions,
  activeIndex,
  onSelect,
  onNewSession,
  onClose,
}: SessionOverviewProps) {
  const [query, setQuery] = useState("")
  const visible = filterSessions(sessions, query)

  useInput((input, key) => {
    if (isSgrMouseChunk(input)) {
      return
    }
    if (key.escape) {
      onClose()
      return
    }
    if (key.ctrl && input === "n") {
      onNewSession()
      return
    }
    if (key.backspace || key.delete) {
      if (query.length > 0) {
        setQuery(query.slice(0, -1))
      }
      return
    }
    if (!key.ctrl && !key.meta && input >= "1" && input <= "9") {
      const rank = Number(input) - 1
      const chosen = visible[rank]
      if (chosen) {
        const index = sessions.findIndex((session) => session.id === chosen.id)
        if (index >= 0) {
          onSelect(index)
        }
      }
      return
    }
    if (
      !key.ctrl &&
      !key.meta &&
      input.length === 1 &&
      /[A-Za-z]/.test(input)
    ) {
      setQuery(query + input)
    }
  })

  const waiting = waitingApprovalRows(sessions)
  const numbered = visible.slice(0, 9)
  const contentHeight =
    5 +
    numbered.length +
    (query.length > 0 ? 1 : 0) +
    (waiting.length > 0 ? waiting.length + 2 : 0)
  const sheetWidth = OVERVIEW_WIDTH + 4
  const sheetHeight = contentHeight + 2

  return (
    <Fill width={sheetWidth} height={sheetHeight} color={palette.rail}>
      <Box width={sheetWidth} height={sheetHeight} padding={1}>
        <Fill
          width={OVERVIEW_WIDTH + 2}
          height={contentHeight}
          color={palette.lane}
        >
          <Box
            width={OVERVIEW_WIDTH + 2}
            height={contentHeight}
            flexDirection="column"
            alignItems="center"
            paddingX={1}
          >
            <Text bold color={palette.brand}>
              SESSIONS
            </Text>
            {query.length > 0 ? (
              <Text dimColor>{`filter: ${query}`}</Text>
            ) : null}
            {waiting.length > 0 ? (
              <Box flexDirection="column" width={OVERVIEW_WIDTH} paddingTop={1}>
                <Text bold color={palette.brand}>
                  ! waiting approval
                </Text>
                {waiting.map((row) => (
                  <Text
                    key={sessions[row.index]?.id ?? row.index}
                    dimColor
                  >{`  [${row.index + 1}] ${padVisible(row.name, 20)}${truncateTail(
                    row.firstStep,
                    32
                  )}`}</Text>
                ))}
              </Box>
            ) : null}
            <Box flexDirection="column" paddingTop={1}>
              {numbered.map((session, index) => {
                const glyph = statusGlyph(session.status)
                const originalIndex = sessions.findIndex(
                  (candidate) => candidate.id === session.id
                )
                const active = originalIndex === activeIndex
                const totals = sessionTokenTotals(session.blocks)
                return (
                  <Box
                    key={session.id}
                    width={OVERVIEW_WIDTH}
                    justifyContent="space-between"
                  >
                    <Text dimColor={!active} bold={active}>
                      {`  ${padVisible(String(index + 1), 3)}${padVisible(
                        session.name,
                        22
                      )}`}
                      <Text color={glyph.color}>{padVisible(glyph.glyph, 12)}</Text>
                      {`${session.unread ? "o " : ""}${ageFromMs(
                        Date.now() - session.createdAt
                      )} ago`}
                    </Text>
                    {totals !== null ? (
                      <Text dimColor>{formatTokenTotals(totals)}</Text>
                    ) : null}
                  </Box>
                )
              })}
              <Text dimColor>{"   + new session"}</Text>
            </Box>
            <Box paddingTop={1}>
              <Text dimColor>1-9 select · ctrl+n new · esc back</Text>
            </Box>
          </Box>
        </Fill>
      </Box>
    </Fill>
  )
}

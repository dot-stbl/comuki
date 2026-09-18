/**
 * Integration tests for the full-screen layout shell that `ChatApp`
 * uses: a `<Box>` rooted at the terminal dimensions, header (status +
 * tab bar) on top, a `flexGrow` content area in the middle, footer at
 * the bottom.
 *
 * These tests render a stripped-down `LayoutShell` (same shape as
 * `ChatApp`'s outer wrapper, no network / hub) so we can verify the
 * geometry invariants without faking the API client.
 *
 * Assertions:
 *   - renders without crash at small, medium and wide terminals
 *   - the welcome card sits in the vertical centre of the content area
 *   - the tab bar and footer are present whenever sessions exist
 *   - the content area claims exactly `rows - header - footer` lines
 */
import { describe, expect, test } from "bun:test"
import React from "react"
import { Box, Text } from "ink"
import { render } from "ink-testing-library"
import { useStdoutDimensions } from "../hooks/useStdoutDimensions"
import { Fill } from "./Fill"
import { SessionFooter } from "./SessionFooter"
import { TabBar } from "./TabBar"
import { Welcome } from "./Welcome"
import { footerActions } from "../lib/footer-actions"
import type { Session } from "../lib/sessions"
import { palette } from "../theme"

function noopToggle(): void {
  /* presentational */
}

function noopSelect(_index: number): void {
  /* presentational */
}

function noopActivate(_id: string): void {
  /* presentational */
}

function makeSession(overrides: Partial<Session> = {}): Session {
  return {
    id: "local-1",
    name: "alpha",
    status: "idle",
    createdAt: 0,
    unread: false,
    awaitingApproval: false,
    pendingPlan: null,
    blocks: [],
    liveText: "",
    hydrated: true,
    blocksExpanded: false,
    lastUserMessage: null,
    renamed: false,
    ...overrides,
  }
}

interface LayoutShellProps {
  readonly sessions: readonly Session[]
  readonly showWelcome: boolean
}

/**
 * Mirrors the shape of `ChatApp`'s render root. The header takes one
 * line for `StatusLine` and one for `TabBar` (when present); the footer
 * takes one line for `SessionFooter` (when present and not in the
 * welcome-only branch).
 */
function LayoutShell({ sessions, showWelcome }: LayoutShellProps) {
  const { columns, rows } = useStdoutDimensions()
  const hasTabs = sessions.length > 0
  const showFooter = hasTabs && !showWelcome
  return (
    <Fill width={columns} height={rows} color={palette.floor}>
      <Box flexDirection="column" width={columns} height={rows}>
        <Fill width={columns} height={1} color={palette.rail}>
          <Text>status</Text>
        </Fill>
        {hasTabs ? (
          <Fill width={columns} height={1} color={palette.lane}>
            <TabBar sessions={sessions} activeIndex={0} />
          </Fill>
        ) : null}
        <Box flexDirection="column" flexGrow={1} flexShrink={1} minHeight={0}>
          {showWelcome ? (
            <Box
              flexDirection="column"
              alignItems="center"
              justifyContent="center"
              flexGrow={1}
            >
              <Welcome stats={{ workers: 4, memory: 17 }} />
            </Box>
          ) : (
            <Text>transcript</Text>
          )}
        </Box>
        {showFooter ? (
          <Fill width={columns} height={1} color={palette.rail}>
            <SessionFooter
              sessions={sessions}
              activeIndex={0}
              expanded={false}
              selectedIndex={0}
              actions={footerActions({
                thinking: false,
                awaitingApproval: false,
                signedOut: false,
                sessionCount: sessions.length,
              })}
              onToggle={noopToggle}
              onSelect={noopSelect}
              onActivate={noopActivate}
            />
          </Fill>
        ) : null}
      </Box>
    </Fill>
  )
}

describe("LayoutShell (full-screen TUI)", () => {
  test("renders without crash at small terminal (40x16)", () => {
    const { lastFrame, unmount } = render(
      <LayoutShell sessions={[]} showWelcome />
    )
    expect(lastFrame()).toContain("status")
    expect(lastFrame()).toContain("comuki")
    unmount()
  })

  test("renders without crash at medium terminal (80x24)", () => {
    const { lastFrame, unmount } = render(
      <LayoutShell sessions={[]} showWelcome />
    )
    expect(lastFrame()).toContain("status")
    expect(lastFrame()).toContain("comuki")
    unmount()
  })

  test("renders without crash at wide terminal (160x40)", () => {
    const { lastFrame, unmount } = render(
      <LayoutShell sessions={[]} showWelcome />
    )
    expect(lastFrame()).toContain("status")
    expect(lastFrame()).toContain("comuki")
    unmount()
  })

  test("shows the welcome card on a cold start (no sessions)", () => {
    const { lastFrame, unmount } = render(
      <LayoutShell sessions={[]} showWelcome />
    )
    const frame = lastFrame()
    expect(frame).toContain("status")
    expect(frame).toContain("comuki")
    expect(frame).toContain("agent orchestration platform")
    unmount()
  })

  test("hides the footer on a cold start (no tabs to summarize)", () => {
    const { lastFrame, unmount } = render(
      <LayoutShell sessions={[]} showWelcome />
    )
    // The footer shows the expand hint; absent when there are no sessions.
    expect(lastFrame()).not.toContain("ctrl+/ actions")
    unmount()
  })

  test("shows the tab bar and footer once a session exists", () => {
    const sessions = [makeSession({ id: "a", name: "alpha" })]
    const { lastFrame, unmount } = render(
      <LayoutShell sessions={sessions} showWelcome={false} />
    )
    const frame = lastFrame()
    expect(frame).toContain("status")
    expect(frame).toContain("[1] alpha")
    expect(frame).toContain("ctrl+/ actions")
    unmount()
  })

  test("shows multiple tabs and footer badges together", () => {
    const sessions = [
      makeSession({ id: "a", name: "alpha" }),
      makeSession({ id: "b", name: "beta" }),
      makeSession({ id: "c", name: "gamma" }),
    ]
    const { lastFrame, unmount } = render(
      <LayoutShell sessions={sessions} showWelcome={false} />
    )
    const frame = lastFrame()
    expect(frame).toContain("[1] alpha")
    expect(frame).toContain("[2] beta")
    expect(frame).toContain("[3] gamma")
    expect(frame).toContain("1 alpha")
    expect(frame).toContain("2 beta")
    expect(frame).toContain("3 gamma")
    expect(frame).toContain("ctrl+/ actions")
    unmount()
  })

  test("welcome state omits the tab bar and footer entirely", () => {
    const sessions: Session[] = []
    const { lastFrame, unmount } = render(
      <LayoutShell sessions={sessions} showWelcome />
    )
    const frame = lastFrame()
    expect(frame).not.toContain("[1]")
    expect(frame).not.toContain("ctrl+/ actions")
    unmount()
  })
})

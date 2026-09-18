/**
 * Component-level tests for the chrome bits that make up the full-screen
 * TUI: the welcome card, the tab strip, and the footer. We render each
 * one with `ink-testing-library` and assert that the expected text
 * appears in the captured frame.
 *
 * The test stdout from ink-testing-library is fixed at 80×24 by default
 * — fine for shape assertions, not for layout math.
 */
import { describe, expect, test } from "bun:test"
import React from "react"
import { render } from "ink-testing-library"
import { SessionFooter } from "./SessionFooter"
import { TabBar } from "./TabBar"
import { Welcome } from "./Welcome"
import { footerActions, type FooterAction } from "../lib/footer-actions"
import { MARK_WELCOME } from "../lib/mark"
import type { Session } from "../lib/sessions"

const IDLE_ACTIONS: readonly FooterAction[] = footerActions({
  thinking: false,
  awaitingApproval: false,
  signedOut: false,
  sessionCount: 1,
})

function noopToggle(): void {
  /* presentational — chrome tests never fire these */
}

function noopSelect(_index: number): void {
  /* presentational */
}

function noopActivate(_id: string): void {
  /* presentational */
}

function renderFooter(
  sessions: readonly Session[],
  activeIndex: number,
  extras: {
    readonly expanded?: boolean
    readonly selectedIndex?: number
    readonly actions?: readonly FooterAction[]
  } = {}
) {
  return (
    <SessionFooter
      sessions={sessions}
      activeIndex={activeIndex}
      expanded={extras.expanded ?? false}
      selectedIndex={extras.selectedIndex ?? 0}
      actions={extras.actions ?? IDLE_ACTIONS}
      onToggle={noopToggle}
      onSelect={noopSelect}
      onActivate={noopActivate}
    />
  )
}

function makeSession(overrides: Partial<Session> = {}): Session {
  return {
    id: "local-1",
    name: "identity-refactor",
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

describe("Welcome", () => {
  test("renders without crash, shows the wordmark", () => {
    const { lastFrame, unmount } = render(<Welcome />)
    const frame = lastFrame()
    expect(frame).toContain("comuki")
    expect(frame).toContain("Describe the outcome you want")
    expect(frame).toContain(MARK_WELCOME[4]?.trim())
    unmount()
  })

  test("does not render a dashboard stats wall", () => {
    const { lastFrame, unmount } = render(<Welcome />)
    expect(lastFrame()).not.toContain("workers")
    unmount()
  })
})

describe("TabBar", () => {
  test("renders no labels when there are no sessions", () => {
    const { lastFrame, unmount } = render(
      <TabBar sessions={[]} activeIndex={-1} />
    )
    const frame = lastFrame()
    expect(frame).toContain("[+]")
    expect(frame).not.toContain("[1]")
    unmount()
  })

  test("renders one tab per session, active tab in accent", () => {
    const sessions = [
      makeSession({ id: "a", name: "alpha" }),
      makeSession({ id: "b", name: "beta" }),
    ]
    const { lastFrame, unmount } = render(
      <TabBar sessions={sessions} activeIndex={1} />
    )
    const frame = lastFrame()
    expect(frame).toContain("[1] alpha")
    expect(frame).toContain("[2] beta")
    expect(frame).toContain("[+]")
    unmount()
  })

  test("marks unread with a dot", () => {
    const sessions = [makeSession({ id: "a", name: "alpha", unread: true })]
    const { lastFrame, unmount } = render(
      <TabBar sessions={sessions} activeIndex={0} />
    )
    expect(lastFrame()).toContain("o")
    unmount()
  })
})

describe("SessionFooter", () => {
  test("renders the expand hint even when there are no sessions", () => {
    const { lastFrame, unmount } = render(renderFooter([], -1))
    expect(lastFrame()).toContain("ctrl+/ actions")
    unmount()
  })

  test("renders one badge per session", () => {
    const sessions = [
      makeSession({ id: "a", name: "alpha" }),
      makeSession({ id: "b", name: "beta" }),
      makeSession({ id: "c", name: "gamma" }),
    ]
    const { lastFrame, unmount } = render(renderFooter(sessions, 1))
    const frame = lastFrame()
    expect(frame).toContain("1 alpha")
    expect(frame).toContain("2 beta")
    expect(frame).toContain("3 gamma")
    unmount()
  })

  test("thinking session shows the accent dot", () => {
    const sessions = [
      makeSession({ id: "a", name: "alpha", status: "thinking" }),
    ]
    const { lastFrame, unmount } = render(renderFooter(sessions, 0))
    expect(lastFrame()).toContain("o")
    unmount()
  })

  test("collapsed footer hides the action list", () => {
    const { lastFrame, unmount } = render(renderFooter([], -1))
    const frame = lastFrame() ?? ""
    expect(frame).toContain("ctrl+/ actions")
    expect(frame).not.toContain("new session")
    expect(frame).not.toContain("quit")
    unmount()
  })

  test("expanded footer lists actions with the selected row marked", () => {
    const { lastFrame, unmount } = render(
      renderFooter([], -1, { expanded: true, selectedIndex: 0 })
    )
    const frame = lastFrame() ?? ""
    expect(frame).toContain("new session")
    expect(frame).toContain("overview")
    expect(frame).toContain("verbose")
    expect(frame).toContain("copy last")
    expect(frame).toContain("search")
    expect(frame).toContain("help")
    expect(frame).toContain("quit")
    expect(frame).toContain("> new session")
    unmount()
  })
})

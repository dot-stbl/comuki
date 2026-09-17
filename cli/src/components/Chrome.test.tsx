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
import type { Session } from "../lib/sessions"

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
    expect(frame).toContain("agent orchestration platform")
    unmount()
  })

  test("omits the stats line when stats are absent", () => {
    const { lastFrame, unmount } = render(<Welcome />)
    expect(lastFrame()).not.toContain("workers")
    unmount()
  })

  test("renders the stats line when stats are provided", () => {
    const { lastFrame, unmount } = render(
      <Welcome stats={{ workers: 12, memory: 348 }} />
    )
    const frame = lastFrame()
    expect(frame).toContain("workers 12")
    expect(frame).toContain("memory 348")
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
    // ● is a colored glyph; assert its presence without asserting color.
    expect(lastFrame()).toContain("●")
    unmount()
  })
})

describe("SessionFooter", () => {
  test("renders the hotkey legend even when there are no sessions", () => {
    const { lastFrame, unmount } = render(
      <SessionFooter sessions={[]} activeIndex={-1} />
    )
    expect(lastFrame()).toContain("esc")
    expect(lastFrame()).toContain("tab")
    expect(lastFrame()).toContain("ctrl+n")
    unmount()
  })

  test("renders one badge per session", () => {
    const sessions = [
      makeSession({ id: "a", name: "alpha" }),
      makeSession({ id: "b", name: "beta" }),
      makeSession({ id: "c", name: "gamma" }),
    ]
    const { lastFrame, unmount } = render(
      <SessionFooter sessions={sessions} activeIndex={1} />
    )
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
    const { lastFrame, unmount } = render(
      <SessionFooter sessions={sessions} activeIndex={0} />
    )
    expect(lastFrame()).toContain("●")
    unmount()
  })
})

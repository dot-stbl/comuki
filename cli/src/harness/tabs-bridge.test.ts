import { describe, expect, it } from "bun:test"
import {
  initialHarnessState,
  pendingSessionId,
  sessionId as harnessSessionId,
  type HarnessSession,
  type HarnessState,
} from "./state"
import { reduceHarness } from "./reducer"
import {
  PENDING_PREFIX,
  type Session,
} from "../lib/session-state"
import { mirrorTabsFromHarness } from "./tabs-bridge"

function harnessSession(overrides: Partial<HarnessSession> = {}): HarnessSession {
  return {
    identity: { kind: "remote", id: harnessSessionId("session-1") },
    projectId: null,
    title: "First",
    createdAtUnixMs: 1,
    renamed: false,
    unread: false,
    turn: { kind: "idle" },
    transcriptLoad: { kind: "not-loaded" },
    transcript: [],
    queue: [],
    history: [],
    lastUserMessage: null,
    pendingPlan: null,
    ...overrides,
  }
}

function harnessStateFrom(sessions: readonly HarnessSession[]): HarnessState {
  return {
    ...initialHarnessState(),
    sessions,
    activeSessionId: sessions[0]?.identity.id ?? null,
  }
}

function legacySession(overrides: Partial<Session> = {}): Session {
  return {
    id: "session-1",
    name: "Legacy name",
    status: "idle",
    createdAt: 1,
    unread: false,
    awaitingApproval: false,
    pendingPlan: null,
    blocks: [{ kind: "lines", key: "b1", lines: ["hello"] }],
    liveText: "",
    hydrated: false,
    blocksExpanded: false,
    lastUserMessage: "what?",
    renamed: false,
    history: ["h1"],
    ...overrides,
  }
}

describe("mirrorTabsFromHarness", () => {
  it("preserves session order from the harness", () => {
    const a = harnessSession({ identity: { kind: "remote", id: harnessSessionId("a") } })
    const b = harnessSession({ identity: { kind: "remote", id: harnessSessionId("b") } })
    const c = harnessSession({ identity: { kind: "remote", id: harnessSessionId("c") } })
    const harness = harnessStateFrom([a, b, c])

    const tabs = mirrorTabsFromHarness(harness, {
      sessions: [],
      activeIndex: -1,
    })

    expect(tabs.sessions.map((session) => session.id)).toEqual(["a", "b", "c"])
  })

  it("translates activeSessionId to its index in the projected tabs", () => {
    const a = harnessSession({ identity: { kind: "remote", id: harnessSessionId("a") } })
    const b = harnessSession({ identity: { kind: "remote", id: harnessSessionId("b") } })
    const harness: HarnessState = {
      ...harnessStateFrom([a, b]),
      activeSessionId: harnessSessionId("b"),
    }

    const tabs = mirrorTabsFromHarness(harness, {
      sessions: [],
      activeIndex: -1,
    })

    expect(tabs.activeIndex).toBe(1)
  })

  it("uses -1 when harness.activeSessionId is null", () => {
    const a = harnessSession({ identity: { kind: "remote", id: harnessSessionId("a") } })
    const harness: HarnessState = {
      ...harnessStateFrom([a]),
      activeSessionId: null,
    }

    const tabs = mirrorTabsFromHarness(harness, {
      sessions: [],
      activeIndex: -1,
    })

    expect(tabs.activeIndex).toBe(-1)
    expect(tabs.sessions).toHaveLength(1)
  })

  it("marks unread from harness and ignores the previous tabs value", () => {
    const unread = harnessSession({
      identity: { kind: "remote", id: harnessSessionId("session-1") },
      unread: true,
    })
    const previous = legacySession({ id: "session-1", unread: false })

    const tabs = mirrorTabsFromHarness(harnessStateFrom([unread]), {
      sessions: [previous],
      activeIndex: 0,
    })

    expect(tabs.sessions[0]?.unread).toBe(true)
  })

  it("hydrates only when transcriptLoad.kind === 'loaded'", () => {
    const loaded = harnessSession({
      transcriptLoad: { kind: "loaded" },
    })

    const tabsLoaded = mirrorTabsFromHarness(harnessStateFrom([loaded]), {
      sessions: [legacySession({ id: "session-1", hydrated: false })],
      activeIndex: 0,
    })
    expect(tabsLoaded.sessions[0]?.hydrated).toBe(true)

    const notLoaded = harnessSession({
      transcriptLoad: { kind: "loading" },
    })
    const tabsLoading = mirrorTabsFromHarness(harnessStateFrom([notLoaded]), {
      sessions: [legacySession({ id: "session-1", hydrated: true })],
      activeIndex: 0,
    })
    expect(tabsLoading.sessions[0]?.hydrated).toBe(false)
  })

  it("carries blocks / liveText / runsFeed through from the previous tabs", () => {
    const harness = harnessSession({
      identity: { kind: "remote", id: harnessSessionId("session-1") },
    })
    const previous = legacySession({
      id: "session-1",
      blocks: [{ kind: "lines", key: "k", lines: ["carried over"] }],
      liveText: "still streaming",
    })

    const tabs = mirrorTabsFromHarness(harnessStateFrom([harness]), {
      sessions: [previous],
      activeIndex: 0,
    })

    expect(tabs.sessions[0]?.blocks).toEqual([
      { kind: "lines", key: "k", lines: ["carried over"] },
    ])
    expect(tabs.sessions[0]?.liveText).toBe("still streaming")
  })

  it("drops sessions that disappeared from the harness", () => {
    const harness = harnessStateFrom([])

    const tabs = mirrorTabsFromHarness(harness, {
      sessions: [legacySession({ id: "session-1" })],
      activeIndex: 0,
    })

    expect(tabs.sessions).toEqual([])
    expect(tabs.activeIndex).toBe(-1)
  })

  it("matches the reducer's neighbour-focus after close", () => {
    // Two sessions, second is closed → focus falls to the first.
    const remote = harnessSession({
      identity: { kind: "remote", id: harnessSessionId("session-1") },
    })
    const second = harnessSession({
      identity: { kind: "remote", id: harnessSessionId("session-2") },
    })
    const opened = reduceHarness(harnessStateFrom([remote, second]), {
      type: "session-focused",
      sessionId: harnessSessionId("session-2"),
    }).state
    const afterClose = reduceHarness(opened, {
      type: "session-closed",
      sessionId: harnessSessionId("session-2"),
    }).state

    const tabs = mirrorTabsFromHarness(afterClose, {
      sessions: [
        legacySession({ id: "session-1" }),
        legacySession({ id: "session-2" }),
      ],
      activeIndex: 1,
    })

    expect(tabs.sessions.map((session) => session.id)).toEqual(["session-1"])
    expect(tabs.activeIndex).toBe(0)
  })

  it("projects a session that exists in the harness but not in tabs", () => {
    const fresh = harnessSession({
      identity: { kind: "pending", id: pendingSessionId(`${PENDING_PREFIX}1`) },
      title: "",
    })

    const tabs = mirrorTabsFromHarness(harnessStateFrom([fresh]), {
      sessions: [],
      activeIndex: -1,
    })

    expect(tabs.sessions).toHaveLength(1)
    expect(tabs.sessions[0]?.id).toBe(`${PENDING_PREFIX}1`)
    expect(tabs.sessions[0]?.name).toBe("session")
    expect(tabs.sessions[0]?.hydrated).toBe(false)
  })

  it("uses the harness title when it is non-empty, falls back to previous", () => {
    const withTitle = harnessSession({
      title: "Real title",
    })

    const tabs = mirrorTabsFromHarness(harnessStateFrom([withTitle]), {
      sessions: [legacySession({ id: "session-1", name: "Legacy name" })],
      activeIndex: 0,
    })
    expect(tabs.sessions[0]?.name).toBe("Real title")

    const blank = harnessSession({
      title: "",
    })
    const tabsBlank = mirrorTabsFromHarness(harnessStateFrom([blank]), {
      sessions: [legacySession({ id: "session-1", name: "Legacy name" })],
      activeIndex: 0,
    })
    expect(tabsBlank.sessions[0]?.name).toBe("Legacy name")
  })
})

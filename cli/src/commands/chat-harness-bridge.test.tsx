/**
 * Thin integration test for the HarnessEngine migration step 1.
 *
 * chat.tsx owns the bridge between the new harness engine and the
 * legacy `tabs` render model; this file proves the same wiring holds
 * end-to-end without booting the full Ink shell (no SignalR, no
 * `ComukiClient`, no `ClientKernel`).
 *
 * `HarnessChatBridge` mirrors the slice the real component carries
 * in step 1: a `tabs` state shaped like `SessionsState` + an effect
 * that subscribes to a `HarnessEngineAdapter` and pipes every
 * snapshot through `mirrorTabsFromHarness`. The harness state is
 * seeded by the test — populated enough to demonstrate the focus /
 * unread invariant and the close / neighbour-focus invariant the
 * reducer already covers in its pure tests. Anything outside that
 * scope (turn lifecycle, approval, connection) is not in this test's
 * contract.
 */
import { describe, expect, it } from "bun:test"
import React, { useEffect, useRef, useState } from "react"
import { render } from "ink-testing-library"
import { HarnessEngineAdapter } from "../harness/engine-adapter"
import { mirrorTabsFromHarness } from "../harness/tabs-bridge"
import { reduceHarness } from "../harness/reducer"
import {
  initialHarnessState,
  sessionId as harnessSessionId,
  type HarnessState,
  type HarnessSession,
} from "../harness/state"

import type { HarnessEngineInterface } from "../harness/engine-types"
import type { Session, SessionsState } from "../lib/session-state"
import type { WorkspaceDocument } from "../harness/workspace"
import type {
  ConversationPort,
  ApprovalPort,
  RealtimePort,
  WorkspaceStore,
} from "../harness/effect-runner"
import type {
  HarnessMessage,
  SessionId,
} from "../harness/state"
import type { OpenedConversation, TurnOutcome } from "../harness/effect-runner"
import { stripAnsi } from "../theme"

function buildRecordingPorts() {
  const setSubscriptionsCalls: string[][] = []
  const writes: WorkspaceDocument[] = []
  const realtime: RealtimePort = {
    setSubscriptions: async (
      sessionIds: readonly SessionId[],
      _signal: AbortSignal,
    ) => {
      setSubscriptionsCalls.push(sessionIds.map((id) => String(id)))
    },
  }
  const workspace: WorkspaceStore = {
    read: async () => null,
    write: async (value: WorkspaceDocument, _signal: AbortSignal) => {
      writes.push(value)
    },
  }
  const conversation: ConversationPort = {
    openConversation: async (): Promise<OpenedConversation> => {
      throw new Error("unused in step 1")
    },
    submitTurn: async (
      _sessionId: SessionId,
      _message: string,
      _commandId: string | undefined,
      _signal: AbortSignal,
    ): Promise<TurnOutcome> => {
      throw new Error("unused in step 1")
    },
    cancelTurn: async (
      _sessionId: SessionId,
      _commandId: string | undefined,
      _signal: AbortSignal,
    ): Promise<void> => {
      throw new Error("unused in step 1")
    },
    loadConversation: async (
      _sessionId: SessionId,
      _signal: AbortSignal,
    ): Promise<readonly HarnessMessage[]> => {
      throw new Error("unused in step 1")
    },
  }
  const approval: ApprovalPort = {
    decide: async (
      _sessionId: SessionId,
      _approved: boolean,
      _reason: string | undefined,
      _commandId: string | undefined,
      _signal: AbortSignal,
    ): Promise<TurnOutcome> => {
      throw new Error("unused in step 1")
    },
  }
  return { realtime, workspace, conversation, approval, setSubscriptionsCalls, writes }
}

function harnessSession(overrides: Partial<HarnessSession> = {}): HarnessSession {
  return {
    identity: { kind: "remote", id: harnessSessionId("session-a") },
    projectId: null,
    title: "A",
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

function harnessStateFromSeed(sessions: HarnessSession[]): HarnessState {
  return {
    ...initialHarnessState(),
    sessions,
    activeSessionId: sessions[0]?.identity.id ?? null,
  }
}

function legacyTab(overrides: Partial<Session> = {}): Session {
  return {
    id: "session-a",
    name: "A",
    status: "idle",
    createdAt: 1,
    unread: false,
    awaitingApproval: false,
    pendingPlan: null,
    blocks: [],
    liveText: "",
    hydrated: false,
    blocksExpanded: false,
    lastUserMessage: null,
    renamed: false,
    history: [],
    ...overrides,
  }
}

interface HarnessChatBridgeProps {
  readonly engine: HarnessEngineInterface
  readonly seedTabs: SessionsState
}

function HarnessChatBridge({ engine, seedTabs }: HarnessChatBridgeProps) {
  const [tabs, setTabs] = useState<SessionsState>(seedTabs)
  const engineRef = useRef<HarnessEngineInterface>(engine)

  useEffect(() => {
    engineRef.current = engine
  }, [engine])

  useEffect(() => {
    return engine.subscribe((state) => {
      setTabs((current) => mirrorTabsFromHarness(state, current))
    })
  }, [engine])

  return React.createElement(
    "ink-box",
    { flexDirection: "column" },
    React.createElement(
      "ink-text",
      null,
      `${tabs.sessions.length}-sessions/${tabs.activeIndex}-active`
    ),
    React.createElement(
      "ink-text",
      null,
      tabs.sessions
        .map((session) =>
          `${session.id}:${session.unread ? "unread" : "ok"}${
            session.id === tabs.sessions[tabs.activeIndex]?.id ? "*" : ""
          }`
        )
        .join(" | ")
    ),
  )
}

async function flushMicrotasks(): Promise<void> {
  await new Promise((resolve) => setTimeout(resolve, 0))
  await new Promise((resolve) => setTimeout(resolve, 0))
}

describe("HarnessChatBridge — HarnessEngine migration step 1", () => {
  it("focus clears the background output's unread dot via the reducer", async () => {
    const ports = buildRecordingPorts()
    const remoteA = harnessSessionId("session-a")
    const remoteB = harnessSessionId("session-b")
    const harness = harnessStateFromSeed([
      harnessSession({
        identity: { kind: "remote", id: remoteA },
        unread: true,
        title: "A",
      }),
      harnessSession({
        identity: { kind: "remote", id: remoteB },
        unread: true,
        title: "B",
      }),
    ])
    const seedTabs: SessionsState = {
      sessions: [legacyTab({ id: "session-a", unread: true }), legacyTab({ id: "session-b", unread: true, name: "B" })],
      activeIndex: 0,
    }
    const engine = new HarnessEngineAdapter({
      initial: harness,
      ports,
    })

    const rendered = render(
      React.createElement(HarnessChatBridge, {
        engine,
        seedTabs,
      }),
    )
    await flushMicrotasks()

    await engine.dispatch({ type: "session-focused", sessionId: remoteB })
    await flushMicrotasks()

    const frame = stripAnsi(rendered.lastFrame() ?? "")
    expect(frame).toContain("2-sessions/1-active")
    expect(frame).toContain("session-b:ok")
    expect(frame).toContain("session-a:unread")
    rendered.unmount()
  })

  it("close removes the focused session and focuses its neighbour", async () => {
    const ports = buildRecordingPorts()
    const remoteA = harnessSessionId("session-a")
    const remoteB = harnessSessionId("session-b")
    const initialState = harnessStateFromSeed([
      harnessSession({
        identity: { kind: "remote", id: remoteA },
        title: "A",
      }),
      harnessSession({
        identity: { kind: "remote", id: remoteB },
        title: "B",
      }),
    ])
    // The reducer's neighbour-focus contract: closing the active
    // session falls back to the session at the same index (or the
    // last one if the closed was rightmost). Mirror via the reducer
    // itself for the seed state — active = remoteB.
    const focused = reduceHarness(initialState, {
      type: "session-focused",
      sessionId: remoteB,
    }).state
    const seedTabs: SessionsState = {
      sessions: [
        legacyTab({ id: "session-a", name: "A" }),
        legacyTab({ id: "session-b", name: "B" }),
      ],
      activeIndex: 1,
    }
    const engine = new HarnessEngineAdapter({
      initial: focused,
      ports,
    })

    const rendered = render(
      React.createElement(HarnessChatBridge, {
        engine,
        seedTabs,
      }),
    )
    await flushMicrotasks()

    await engine.dispatch({ type: "session-closed", sessionId: remoteB })
    await flushMicrotasks()

    const frame = stripAnsi(rendered.lastFrame() ?? "")
    expect(frame).toContain("1-sessions/0-active")
    expect(frame).toContain("session-a:ok")
    expect(frame).not.toContain("session-b")

    expect(ports.writes.length).toBe(1)
    expect(ports.setSubscriptionsCalls).toEqual([["session-a"]])
    rendered.unmount()
  })

  it("renders the initial tabs untouched before any dispatch", async () => {
    const ports = buildRecordingPorts()
    const remoteA = harnessSessionId("session-a")
    const harness = harnessStateFromSeed([
      harnessSession({
        identity: { kind: "remote", id: remoteA },
        title: "A",
        unread: false,
      }),
    ])
    const seedTabs: SessionsState = {
      sessions: [legacyTab({ id: "session-a", name: "A" })],
      activeIndex: 0,
    }
    const engine = new HarnessEngineAdapter({
      initial: harness,
      ports,
    })

    const rendered = render(
      React.createElement(HarnessChatBridge, {
        engine,
        seedTabs,
      }),
    )
    await flushMicrotasks()

    const frame = stripAnsi(rendered.lastFrame() ?? "")
    expect(frame).toContain("1-sessions/0-active")
    expect(frame).toContain("session-a:ok")
    rendered.unmount()
  })

  it("a no-op close on an absent session leaves tabs untouched", async () => {
    const ports = buildRecordingPorts()
    const remoteA = harnessSessionId("session-a")
    const harness = harnessStateFromSeed([
      harnessSession({
        identity: { kind: "remote", id: remoteA },
        title: "A",
      }),
    ])
    const seedTabs: SessionsState = {
      sessions: [legacyTab({ id: "session-a", name: "A" })],
      activeIndex: 0,
    }
    const engine = new HarnessEngineAdapter({
      initial: harness,
      ports,
    })

    const rendered = render(
      React.createElement(HarnessChatBridge, {
        engine,
        seedTabs,
      }),
    )
    await flushMicrotasks()

    // close a session that the harness doesn't have.
    await engine.dispatch({
      type: "session-closed",
      sessionId: harnessSessionId("missing"),
    })
    await flushMicrotasks()

    const frame = stripAnsi(rendered.lastFrame() ?? "")
    expect(frame).toContain("1-sessions/0-active")
    expect(frame).toContain("session-a:ok")
    // Reducer `unchanged(state)` — no effects, no writes, no subs.
    expect(ports.writes).toEqual([])
    expect(ports.setSubscriptionsCalls).toEqual([])
    rendered.unmount()
  })
})

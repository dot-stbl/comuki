import { describe, expect, it } from "bun:test"
import { reduceHarness } from "../harness/reducer"
import {
  initialHarnessState,
  pendingSessionId,
  sessionId,
  turnRequestId,
} from "../harness/state"
import { fakeFeed, fakePorts } from "./fakes"
import { createClientKernel } from "./kernel"
import type { HarnessEffect } from "../harness/effects"
import type { HarnessEvent } from "../harness/events"

const remoteId = sessionId("server-1")
const pendingId = pendingSessionId("local-1")

async function until(predicate: () => boolean): Promise<void> {
  for (let attempt = 0; attempt < 200 && !predicate(); attempt += 1) {
    await new Promise((resolve) => setTimeout(resolve, 1))
  }
  expect(predicate()).toBeTrue()
}

describe("determinism and duplicate rejection", () => {
  it("replays the same event sequence into identical states and effects", () => {
    const script: readonly HarnessEvent[] = [
      {
        type: "remote-session-adopted",
        pendingSessionId: pendingId,
        sessionId: remoteId,
        projectId: null,
        title: "Task",
      },
      {
        type: "turn-queued",
        sessionId: remoteId,
        requestId: turnRequestId("cmd-1"),
        message: "Hello",
        commandId: "cmd-1",
        echoText: "Hello",
      },
      {
        type: "thinking-started",
        sessionId: remoteId,
        requestId: turnRequestId("cmd-1"),
      },
      {
        type: "thinking-chunk-received",
        sessionId: remoteId,
        requestId: turnRequestId("cmd-1"),
        text: "partial",
      },
      {
        type: "turn-completed",
        sessionId: remoteId,
        requestId: turnRequestId("cmd-1"),
        messages: [
          { id: "m1", role: "assistant", content: "Done", createdAtUnixMs: 5 },
        ],
        awaitingApproval: false,
      },
    ]

    const runOnce = () => {
      const opened = reduceHarness(initialHarnessState(), {
        type: "pending-session-opened",
        pendingSessionId: pendingId,
        projectId: null,
        createdAtUnixMs: 1,
      })
      const effects: HarnessEffect[] = [...opened.effects]
      let current = opened.state
      for (const event of script) {
        const transition = reduceHarness(current, event)
        current = transition.state
        effects.push(...transition.effects)
      }
      return { state: current, effects }
    }

    const first = runOnce()
    const second = runOnce()

    expect(second.state).toEqual(first.state)
    expect(second.effects).toEqual(first.effects)
    expect(first.state.sessions[0]?.transcript.map((message) => message.id)).toEqual([
      "echo-cmd-1",
      "m1",
    ])
  })

  it("never issues a second submit for a repeated command id", async () => {
    const fake = fakePorts()
    const { port: feedPort } = fakeFeed()
    const kernel = createClientKernel({
      ports: fake.ports,
      feed: feedPort,
      now: () => 1,
    })
    kernel.start()
    await kernel.whenIdle()
    kernel.dispatch({ kind: "open-session" })
    // Issue #77 — bring the hub online AFTER the session exists so
    // the subscriptions effect sees the open set.
    kernel.accept({ type: "connection-established" })
    const pending = pendingSessionId("local-1-0")
    for (const attempt of [1, 2, 3]) {
      void attempt
      kernel.dispatch({
        kind: "submit-turn",
        sessionId: pending,
        message: "Hello",
        commandId: "cmd-1",
        echoText: "Hello",
      })
    }

    await until(() => fake.conversation.submits.length > 0)
    // Give a would-be duplicate several macrotask turns to land — a
    // fixed sleep would be timing-dependent, this bounds the wait.
    for (let turn = 0; turn < 5; turn += 1) {
      await new Promise((resolve) => setTimeout(resolve, 1))
    }
    expect(fake.conversation.submits).toHaveLength(1)
    fake.conversation.submits[0]?.resolve({
      messages: [],
      awaitingApproval: false,
    })
    await kernel.whenIdle()
  })

  it("applies a duplicate server completion exactly once", async () => {
    const fake = fakePorts({
      version: 2,
      activeSessionId: "server-1",
      sessions: [
        { id: "server-1", title: "T", renamed: false, createdAtUnixMs: 1, lastStatus: "done", history: [] },
      ],
      drafts: [],
      cursors: {},
      outbound: [],
    })
    const kernel = createClientKernel({ ports: fake.ports, now: () => 1 })
    kernel.start()
    await kernel.whenIdle()

    const completion: HarnessEvent = {
      type: "turn-completed",
      sessionId: remoteId,
      requestId: turnRequestId("cmd-1"),
      messages: [
        { id: "m1", role: "assistant", content: "once", createdAtUnixMs: 5 },
      ],
      awaitingApproval: false,
    }
    kernel.accept(completion)
    kernel.accept(completion)
    kernel.accept({ ...completion })

    const session = kernel.snapshot().state.sessions[0]
    expect(session?.transcript).toHaveLength(1)
  })

  it("does not duplicate transcript on a duplicated feed chunk sequence", async () => {
    const { ports } = fakePorts(null)
    const feed = fakeFeed()
    const kernel = createClientKernel({ ports, feed: feed.port, now: () => 1 })
    kernel.start()
    await kernel.whenIdle()

    // Chunks without an in-flight turn are stale — text never lands.
    feed.push({ kind: "chunk", sessionId: "server-9", seq: 1, text: "x", receivedAtUnixMs: 1 })
    feed.push({ kind: "chunk", sessionId: "server-9", seq: 1, text: "x", receivedAtUnixMs: 2 })
    await until(() => Object.keys(kernel.snapshot().state.cursors).length === 1)

    const state = kernel.snapshot().state
    expect(state.sessions).toHaveLength(0)
    expect(state.cursors["server-9"]).toBe(2)
  })
})

import { describe, expect, it } from "bun:test"
import { fakeFeed, fakePorts } from "./fakes"
import { createClientKernel } from "./kernel"
import { sessionId, pendingSessionId, turnRequestId } from "../harness/state"

const remoteId = sessionId("server-1")

/** Waits for a condition on the microtask/macrotask boundary. */
async function until(predicate: () => boolean): Promise<void> {
  for (let attempt = 0; attempt < 200 && !predicate(); attempt += 1) {
    await new Promise((resolve) => setTimeout(resolve, 1))
  }
  expect(predicate()).toBeTrue()
}

function legacyDoc(): unknown {
  return {
    activeSessionId: "server-1",
    sessions: [
      {
        id: "server-1",
        name: "Restored",
        status: "done",
        createdAt: 10,
        history: ["old message"],
      },
    ],
  }
}

function kernelWith(initialWorkspace: unknown = null) {
  const fake = fakePorts(initialWorkspace)
  const feed = fakeFeed()
  let clock = 1_000
  const kernel = createClientKernel({
    ports: fake.ports,
    feed: feed.port,
    now: () => clock,
  })
  return { fake, feed, kernel, tick: (ms: number) => (clock += ms) }
}

describe("ClientKernel bootstrap", () => {
  it("restores the workspace from a legacy v1 document", async () => {
    const { fake, kernel } = kernelWith(legacyDoc())
    kernel.start()
    await kernel.whenIdle()

    const session = kernel.snapshot().state.sessions[0]
    expect(session?.identity).toEqual({ kind: "remote", id: remoteId })
    expect(session?.title).toBe("Restored")
    expect(session?.history).toEqual(["old message"])
    expect(kernel.snapshot().state.activeSessionId).toBe(remoteId)
    expect(fake.workspace.written.length).toBe(0)
  })

  it("notifies subscribers with rising revisions", async () => {
    const { kernel } = kernelWith(null)
    const revisions: number[] = []
    kernel.subscribe((snapshot) => revisions.push(snapshot.revision))
    kernel.start()
    await kernel.whenIdle()
    kernel.dispatch({ kind: "open-session" })
    expect(revisions.length).toBeGreaterThan(0)
    expect([...revisions].sort((left, right) => left - right)).toEqual(revisions)
  })
})

describe("ClientKernel crown flow", () => {
  it("runs submit → adopt → stream → authoritative completion", async () => {
    const { fake, feed, kernel } = kernelWith(null)
    kernel.start()
    await kernel.whenIdle()

    kernel.dispatch({ kind: "open-session" })
    kernel.dispatch({
      kind: "submit-turn",
      sessionId: pendingSessionId("local-1000-0"),
      message: "plan the refactor",
      commandId: "cmd-1",
      echoText: "plan the refactor",
      titleHint: "plan the refactor",
    })
    await until(() => fake.conversation.submits.length === 1)
    await until(() =>
      kernel.snapshot().state.sessions[0]?.identity.kind === "remote"
    )

    const adopted = kernel.snapshot().state.sessions[0]
    expect(adopted?.title).toBe("plan the refactor")

    // Live chunks stream into the in-flight turn.
    feed.push({
      kind: "chunk",
      sessionId: "server-1",
      seq: 1,
      text: "part one ",
      receivedAtUnixMs: 1_001,
    })
    feed.push({
      kind: "chunk",
      sessionId: "server-1",
      seq: 2,
      text: "part two",
      receivedAtUnixMs: 1_002,
    })
    await until(() => {
      const turn = kernel.snapshot().state.sessions[0]?.turn
      return turn?.kind === "thinking" && turn.accumulatedText === "part one part two"
    })

    // The REST result is authoritative — it replaces the stream.
    fake.conversation.submits[0]?.resolve({
      messages: [
        { id: "m1", role: "assistant", content: "The plan", createdAtUnixMs: 2_000 },
      ],
      awaitingApproval: false,
    })
    await kernel.whenIdle()

    const session = kernel.snapshot().state.sessions[0]
    expect(session?.turn).toEqual({ kind: "idle" })
    expect(session?.transcript.map((message) => message.id)).toEqual([
      "echo-cmd-1",
      "m1",
    ])
    expect(session?.lastUserMessage).toBe("plan the refactor")
  })

  it("queues a second message while a turn is in flight and drains it after", async () => {
    const { fake, kernel } = kernelWith(legacyDoc())
    kernel.start()
    await kernel.whenIdle()

    kernel.dispatch({
      kind: "submit-turn",
      sessionId: remoteId,
      message: "first",
      commandId: "cmd-1",
      echoText: "first",
    })
    await until(() => fake.conversation.submits.length === 1)
    kernel.dispatch({
      kind: "submit-turn",
      sessionId: remoteId,
      message: "second",
      commandId: "cmd-2",
      echoText: "second",
    })
    expect(fake.conversation.submits.length).toBe(1)

    fake.conversation.submits[0]?.resolve({
      messages: [],
      awaitingApproval: false,
    })
    await until(() => fake.conversation.submits.length === 2)
    expect(fake.conversation.submits[1]?.message).toBe("second")
    fake.conversation.submits[1]?.resolve({
      messages: [],
      awaitingApproval: false,
    })
    await kernel.whenIdle()
    expect(kernel.snapshot().state.sessions[0]?.queue).toHaveLength(0)
  })

  it("decides approvals online and never queues them", async () => {
    const { fake, kernel } = kernelWith(legacyDoc())
    kernel.start()
    await kernel.whenIdle()

    // No approval pending — the intent must translate to nothing.
    kernel.dispatch({
      kind: "decide-approval",
      sessionId: remoteId,
      approved: true,
      commandId: "cmd-approve",
    })
    expect(fake.approval.decisions).toHaveLength(0)

    kernel.dispatch({
      kind: "submit-turn",
      sessionId: remoteId,
      message: "do it",
      commandId: "cmd-1",
      echoText: "do it",
    })
    await until(() => fake.conversation.submits.length === 1)
    fake.conversation.submits[0]?.resolve({
      messages: [],
      awaitingApproval: true,
      pendingPlan: { nodes: [] },
    })
    await until(
      () => kernel.snapshot().state.sessions[0]?.turn.kind === "awaiting-approval"
    )

    kernel.dispatch({
      kind: "decide-approval",
      sessionId: remoteId,
      approved: false,
      reason: "nope",
      commandId: "cmd-approve",
    })
    await until(() => fake.approval.decisions.length === 1)
    expect(fake.approval.decisions[0]?.approved).toBeFalse()
    expect(fake.approval.decisions[0]?.reason).toBe("nope")

    fake.approval.decisions[0]?.resolve({
      messages: [
        { id: "m2", role: "assistant", content: "ok, stopped", createdAtUnixMs: 3_000 },
      ],
      awaitingApproval: false,
    })
    await kernel.whenIdle()
    expect(kernel.snapshot().state.sessions[0]?.turn).toEqual({ kind: "idle" })
    expect(kernel.snapshot().state.sessions[0]?.pendingPlan).toBeNull()
  })

  it("cancels an in-flight turn from the client side", async () => {
    const { fake, kernel } = kernelWith(legacyDoc())
    kernel.start()
    await kernel.whenIdle()

    kernel.dispatch({
      kind: "submit-turn",
      sessionId: remoteId,
      message: "long turn",
      commandId: "cmd-1",
      echoText: "long turn",
    })
    await until(() => fake.conversation.submits.length === 1)

    kernel.dispatch({ kind: "cancel-turn", sessionId: remoteId })
    await until(() => fake.conversation.cancelCalls.length === 1)

    // The abort rejects the in-flight submit with an AbortError, which
    // lands as a typed failure.
    await until(() => {
      const turn = kernel.snapshot().state.sessions[0]?.turn
      return turn?.kind === "failed" && turn.error.kind === "aborted"
    })
  })

  it("re-subscribes the open sessions after a reconnect", async () => {
    const { fake, feed, kernel } = kernelWith(legacyDoc())
    kernel.start()
    await kernel.whenIdle()

    feed.push({ kind: "connection", event: "started" })
    await until(() => fake.realtime.callsList.length === 1)
    expect(fake.realtime.callsList[0]).toEqual([remoteId])

    feed.push({ kind: "connection", event: "reconnecting" })
    feed.push({ kind: "connection", event: "reconnected" })
    await until(() => fake.realtime.callsList.length === 2)
    expect(kernel.snapshot().state.connection.kind).toBe("connected")
  })

  it("closes a local tab without archiving the server session", async () => {
    const { fake, kernel } = kernelWith(legacyDoc())
    kernel.start()
    await kernel.whenIdle()

    kernel.dispatch({ kind: "close-session", sessionId: remoteId })
    await kernel.whenIdle()

    expect(kernel.snapshot().state.sessions).toHaveLength(0)
    const lastWrite = fake.workspace.written.at(-1) as {
      sessions: readonly { id: string }[]
    }
    expect(lastWrite.sessions).toEqual([])
  })
})

describe("ClientKernel robustness", () => {
  it("ignores unknown feed messages while still advancing the cursor", async () => {
    const { feed, kernel } = kernelWith(legacyDoc())
    kernel.start()
    await kernel.whenIdle()

    feed.push({ kind: "unknown", sessionId: "server-1", receivedAtUnixMs: 5 })
    feed.push({
      kind: "turn-complete",
      sessionId: "server-1",
      outcome: "weird-future-outcome",
      receivedAtUnixMs: 6,
    })
    await until(() => kernel.snapshot().state.cursors["server-1"] === 6)
    expect(kernel.snapshot().state.sessions[0]?.turn).toEqual({ kind: "idle" })
  })

  it("rejects stale completions for a different in-flight request", async () => {
    const { fake, kernel } = kernelWith(legacyDoc())
    kernel.start()
    await kernel.whenIdle()

    kernel.dispatch({
      kind: "submit-turn",
      sessionId: remoteId,
      message: "current",
      commandId: "cmd-1",
      echoText: "current",
    })
    await until(() => fake.conversation.submits.length === 1)

    // A late completion for a request that is not in flight.
    kernel.accept({
      type: "turn-completed",
      sessionId: remoteId,
      requestId: turnRequestId("cmd-0"),
      messages: [
        { id: "stale", role: "assistant", content: "stale", createdAtUnixMs: 1 },
      ],
      awaitingApproval: false,
    })

    const session = kernel.snapshot().state.sessions[0]
    expect(session?.transcript.some((message) => message.id === "stale")).toBeFalse()
    expect(session?.turn.kind).toBe("thinking")
  })
})

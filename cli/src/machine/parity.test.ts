/**
 * TUI-parity test for machine mode (issue #80).
 *
 * The same `ClientEvent` sequence that drives the kernel (and the TUI
 * renders from) must produce a semantically equivalent NDJSON
 * envelope stream. This file proves the contract by replaying the
 * "crown flow" twice — once through the kernel, once through the
 * machine mapping + `NdjsonOutput` adapter — and asserting that the
 * observable contents agree.
 *
 * Two describe blocks:
 *
 *   1. "machine parity with the TUI crown flow" — both projections,
 *      ordering, schemaVersion, correlationId, occurredAtUnixMs, and
 *      the 1..N monotonic sequence.
 *
 *   2. "unknown-payload safety" — the mapping's whitelist must keep
 *      private payloads (`pendingPlan`, user message content) off the
 *      wire, even when the harness event carries them.
 *
 * Source: src/tui/host.test.ts (makeKernel pattern),
 *         src/kernel/fakes.ts (fakePorts/fakeFeed),
 *         src/harness/reducer.test.ts (crown-flow fixtures),
 *         src/machine/output.test.ts (capturing streams).
 */
import { afterEach, describe, expect, it } from "bun:test"
import {
  pendingSessionId,
  sessionId,
  turnRequestId,
  type HarnessMessage,
  type HarnessSession,
  type HarnessState,
} from "../harness/state"
import { createClientKernel, type ClientEvent, type ClientKernel } from "../kernel"
import { fakeFeed, fakePorts } from "../kernel/fakes"
import type { MachineClock } from "./envelopes"
import { harnessEventToMachineEvent } from "./mapping"
import { NdjsonOutput } from "./output"
import type { MachineStreams } from "./port"

const FIXED_NOW = 1_700_000_000_000

/**
 * Fixed-time, sequential-id clock — deterministic envelopes without
 * touching `crypto.randomUUID()` / `Date.now()`.
 */
function fakeClock(): MachineClock {
  let n = 1
  return {
    now: () => FIXED_NOW,
    newId: () => `id-${n++}`,
  }
}

/** Mirrors output.test.ts — captures stdout/stderr into arrays. */
function capturingStreams(): {
  readonly streams: MachineStreams
  readonly outWrites: readonly string[]
  readonly errWrites: readonly string[]
} {
  const outWrites: string[] = []
  const errWrites: string[] = []
  return {
    streams: {
      out: {
        write: (text) => {
          outWrites.push(text)
        },
      },
      err: {
        write: (text) => {
          errWrites.push(text)
        },
      },
    },
    outWrites,
    errWrites,
  }
}

function findCrownSession(
  state: HarnessState,
  remoteSessionId: string
): HarnessSession | undefined {
  return state.sessions.find(
    (session) =>
      session.identity.kind === "remote" && session.identity.id === remoteSessionId
  )
}

/** Crown-flow event fixtures, copied from reducer.test.ts conventions. */
const PENDING = pendingSessionId("local-crown")
const REMOTE = sessionId("s-crown")
const REQUEST = turnRequestId("turn-crown")

/**
 * User echo content intentionally matches the `turn-queued` message
 * text below. The unknown-payload safety block asserts neither copy
 * leaks into the machine envelope stream — a stronger guard than
 * using placeholder text.
 */
const USER_ECHO: HarnessMessage = {
  id: "m-echo",
  role: "user",
  content: "crown question",
  createdAtUnixMs: 1,
}
const ASSISTANT_ANSWER: HarnessMessage = {
  id: "m-answer",
  role: "assistant",
  content: "final answer",
  createdAtUnixMs: 1,
}

const CROWN_EVENTS: readonly ClientEvent[] = [
  {
    type: "pending-session-opened",
    pendingSessionId: PENDING,
    projectId: null,
    createdAtUnixMs: 1,
  },
  {
    type: "remote-session-adopted",
    pendingSessionId: PENDING,
    sessionId: REMOTE,
    projectId: null,
    title: "Crown",
  },
  {
    type: "turn-queued",
    sessionId: REMOTE,
    requestId: REQUEST,
    message: "crown question",
  },
  {
    type: "thinking-started",
    sessionId: REMOTE,
    requestId: REQUEST,
  },
  {
    type: "thinking-chunk-received",
    sessionId: REMOTE,
    requestId: REQUEST,
    text: "chunk-a",
  },
  {
    type: "thinking-chunk-received",
    sessionId: REMOTE,
    requestId: REQUEST,
    text: "chunk-b",
  },
  {
    type: "thinking-chunk-received",
    sessionId: REMOTE,
    requestId: REQUEST,
    text: "chunk-c",
  },
  {
    type: "turn-completed",
    sessionId: REMOTE,
    requestId: REQUEST,
    messages: [USER_ECHO, ASSISTANT_ANSWER],
    awaitingApproval: false,
    pendingPlan: { secret: "SECRET-PLAN-PAYLOAD" },
  },
]

describe("machine parity with the TUI crown flow", () => {
  let kernel: ClientKernel | undefined
  let feed: ReturnType<typeof fakeFeed> | undefined

  afterEach(() => {
    kernel?.stop()
    feed?.end()
    kernel = undefined
    feed = undefined
  })

  it("drives the kernel and the ndjson adapter to semantically equivalent output", () => {
    // ---- SIDE A: TUI projection via kernel + fakePorts/fakeFeed ----
    const ports = fakePorts()
    feed = fakeFeed()
    kernel = createClientKernel({
      ports: ports.ports,
      feed: feed.port,
    })
    // No start() — accept() works without it; the feed isn't consumed.

    // 1. pending-session-opened
    kernel.accept(CROWN_EVENTS[0]!)
    // 2. remote-session-adopted
    kernel.accept(CROWN_EVENTS[1]!)
    // 3. turn-queued — submits the queued turn to the (fake) REST port
    kernel.accept(CROWN_EVENTS[2]!)
    // 4. thinking-started — turn enters thinking with empty accumulated text
    kernel.accept(CROWN_EVENTS[3]!)

    {
      const crown = findCrownSession(kernel.snapshot().state, "s-crown")
      expect(crown).toBeDefined()
      expect(crown?.turn.kind).toBe("thinking")
      if (crown?.turn.kind === "thinking") {
        expect(crown.turn.accumulatedText).toBe("")
        expect(crown.turn.requestId).toBe(REQUEST)
      }
    }

    // 5. three thinking-chunk-received events in order — the kernel
    //    concats each text into `accumulatedText`, which is what the
    //    TUI renders as the live "thinking" line.
    kernel.accept(CROWN_EVENTS[4]!) // chunk-a
    {
      const crown = findCrownSession(kernel.snapshot().state, "s-crown")
      expect(crown?.turn.kind).toBe("thinking")
      if (crown?.turn.kind === "thinking") {
        const text = crown.turn.accumulatedText
        expect(text.includes("chunk-a")).toBe(true)
        expect(text.indexOf("chunk-a")).toBe(0)
      }
    }

    kernel.accept(CROWN_EVENTS[5]!) // chunk-b
    {
      const crown = findCrownSession(kernel.snapshot().state, "s-crown")
      expect(crown?.turn.kind).toBe("thinking")
      if (crown?.turn.kind === "thinking") {
        const text = crown.turn.accumulatedText
        const idxA = text.indexOf("chunk-a")
        const idxB = text.indexOf("chunk-b")
        expect(idxA).toBe(0)
        expect(idxB).toBeGreaterThan(idxA)
      }
    }

    kernel.accept(CROWN_EVENTS[6]!) // chunk-c
    {
      const crown = findCrownSession(kernel.snapshot().state, "s-crown")
      expect(crown?.turn.kind).toBe("thinking")
      if (crown?.turn.kind === "thinking") {
        const text = crown.turn.accumulatedText
        const idxA = text.indexOf("chunk-a")
        const idxB = text.indexOf("chunk-b")
        const idxC = text.indexOf("chunk-c")
        expect(idxA).toBe(0)
        expect(idxB).toBeGreaterThan(idxA)
        expect(idxC).toBeGreaterThan(idxB)
      }
    }

    // 6. turn-completed — turn returns to idle; transcript carries
    //    the assistant's authoritative "final answer".
    kernel.accept(CROWN_EVENTS[7]!)
    {
      const crown = findCrownSession(kernel.snapshot().state, "s-crown")
      expect(crown).toBeDefined()
      // The TUI no longer renders a "thinking" prefix — turn is idle
      // (or awaiting-approval); "thinking" specifically is forbidden.
      expect(crown?.turn.kind).not.toBe("thinking")
      const assistantAnswer = crown?.transcript.find(
        (message) => message.role === "assistant" && message.content === "final answer"
      )
      expect(assistantAnswer).toBeDefined()
      // Transcript count === messageCount the machine envelope carries.
      expect(crown?.transcript.length).toBe(2)
    }

    // ---- SIDE B: machine projection via harnessEventToMachineEvent + NdjsonOutput ----
    const cap = capturingStreams()
    const out = new NdjsonOutput({ clock: fakeClock(), streams: cap.streams })
    out.start("parity-crown")

    for (const event of CROWN_EVENTS) {
      const machine = harnessEventToMachineEvent(event)
      if (machine !== null) {
        out.emit(machine)
      }
    }
    out.complete(null)

    expect(cap.errWrites.length).toBe(0)

    const lines = cap.outWrites.map(
      (line) => JSON.parse(line) as Record<string, unknown>
    )

    // ---- PARITY ASSERTIONS ----

    // Total envelope count: 1 started + 5 mapped events + 1 terminal = 7.
    expect(lines.length).toBe(7)

    // Line 0 — command.started (the correlation root).
    expect(lines[0]?.kind).toBe("command.started")
    expect(lines[0]?.command).toBe("parity-crown")

    // Line 1 — session.created with sessionId "s-crown".
    const sessionCreated = lines[1]?.event as Record<string, unknown>
    expect(sessionCreated.event).toBe("session.created")
    expect(sessionCreated.sessionId).toBe("s-crown")

    // Lines 2..4 — turn.chunk with texts in the same order the
    // kernel's accumulatedText observed them.
    const chunkA = lines[2]?.event as Record<string, unknown>
    const chunkB = lines[3]?.event as Record<string, unknown>
    const chunkC = lines[4]?.event as Record<string, unknown>
    expect(chunkA.event).toBe("turn.chunk")
    expect(chunkA.sessionId).toBe("s-crown")
    expect(chunkA.text).toBe("chunk-a")
    expect(chunkB.event).toBe("turn.chunk")
    expect(chunkB.sessionId).toBe("s-crown")
    expect(chunkB.text).toBe("chunk-b")
    expect(chunkC.event).toBe("turn.chunk")
    expect(chunkC.sessionId).toBe("s-crown")
    expect(chunkC.text).toBe("chunk-c")

    // Parity: the chunk order on the wire === the chunk order the
    // kernel/TUI streamed.
    const chunkTexts = [chunkA.text, chunkB.text, chunkC.text]
    expect(chunkTexts).toEqual(["chunk-a", "chunk-b", "chunk-c"])

    // Line 5 — turn.completed with messageCount 2 (the two
    // HarnessMessage entries: user echo + assistant answer).
    const completed = lines[5]?.event as Record<string, unknown>
    expect(completed.event).toBe("turn.completed")
    expect(completed.sessionId).toBe("s-crown")
    expect(completed.messageCount).toBe(2)

    // Line 6 — terminal command.completed is present and last.
    // (The real oneshot rides the authoritative reply text in this
    // envelope's result; here complete(null) only asserts presence +
    // order, which is the parity contract this test owns.)
    const terminal = lines[6] as Record<string, unknown>
    expect(terminal.kind).toBe("command.completed")
    expect(terminal.exitCode).toBe(0)
    expect(lines[lines.length - 1]).toBe(terminal)

    // Every line: schemaVersion === 1, correlationId identical,
    // occurredAtUnixMs is unix ms (the fixed 1_700_000_000_000, NOT
    // divided to seconds).
    const correlationId = lines[0]?.correlationId as string
    expect(typeof correlationId).toBe("string")
    expect(correlationId.length).toBeGreaterThan(0)
    for (const line of lines) {
      expect(line.schemaVersion).toBe(1)
      expect(line.correlationId).toBe(correlationId)
      expect(line.occurredAtUnixMs).toBe(FIXED_NOW)
    }

    // Sequence numbers on command.event lines are 1..N monotonic,
    // where N === 5 (1 session.created + 3 turn.chunk + 1
    // turn.completed). The spec text "1..4" was a count slip —
    // actual mapped event count is 5. Asserting 1..5 keeps the test
    // honest about the wire shape.
    for (let seq = 1; seq <= 5; seq += 1) {
      const line = lines[seq] as Record<string, unknown>
      expect(line.kind).toBe("command.event")
      expect(line.sequence).toBe(seq)
    }
  })
})

describe("unknown-payload safety", () => {
  it("turn-completed mapping carries only the public surface — no pendingPlan or message content", () => {
    const mapped = harnessEventToMachineEvent({
      type: "turn-completed",
      sessionId: REMOTE,
      requestId: REQUEST,
      messages: [USER_ECHO, ASSISTANT_ANSWER],
      awaitingApproval: false,
      pendingPlan: { secret: "SECRET-PLAN-PAYLOAD" },
    })

    expect(mapped).toBeDefined()
    if (mapped === null || mapped.event !== "turn.completed") {
      throw new Error("expected mapped turn.completed event")
    }
    expect(mapped.event).toBe("turn.completed")
    expect(mapped.sessionId).toBe("s-crown")
    expect(mapped.messageCount).toBe(2)

    // pendingPlan must NOT leak — neither as a top-level field nor
    // as a substring in the serialised envelope.
    const keys = Object.keys(mapped).sort()
    expect(keys).toEqual(["event", "messageCount", "sessionId"])
    const json = JSON.stringify(mapped)
    expect(json.includes("SECRET-PLAN-PAYLOAD")).toBe(false)
    // Message content must NOT leak either — only the count rides.
    expect(json.includes("final answer")).toBe(false)
    expect(json.includes("crown question")).toBe(false)
  })

  it("ndjson envelope stream carries neither pendingPlan nor the user echo text", () => {
    const cap = capturingStreams()
    const out = new NdjsonOutput({ clock: fakeClock(), streams: cap.streams })
    out.start("parity-safety")

    for (const event of CROWN_EVENTS) {
      const machine = harnessEventToMachineEvent(event)
      if (machine !== null) {
        out.emit(machine)
      }
    }
    out.complete(null)

    // The full capture — every envelope on stdout. The marker must
    // be absent: pendingPlan only rides turn-completed in the harness
    // event, and the mapper strips it. The user question rides
    // turn-queued, which maps to null, AND lives inside a
    // HarnessMessage carried by turn-completed — neither path reaches
    // a machine envelope.
    const capture = cap.outWrites.join("")
    expect(capture.includes("SECRET-PLAN-PAYLOAD")).toBe(false)
    expect(capture.includes("crown question")).toBe(false)
    // err must remain untouched — no diagnostic bleed.
    expect(cap.errWrites.length).toBe(0)
  })

  it("returns null for events with no machine equivalent (e.g. draft-saved)", () => {
    expect(
      harnessEventToMachineEvent({
        type: "draft-saved",
        sessionId: REMOTE,
        text: "x",
        savedAtUnixMs: 1,
      })
    ).toBeNull()
  })
})
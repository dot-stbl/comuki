/**
 * Machine-mode output adapters (issue #80) — both adapters speak the
 * same envelope factories from `./envelopes`, differ only in *when*
 * they emit on stdout.
 *
 *   JsonOutput   — one pretty envelope on terminal; nothing else.
 *   NdjsonOutput — one compact line per envelope (started / event /
 *                  terminal); newline-terminated NDJSON.
 *
 * Both are pure over the injected clock + streams. We never touch
 * `process` here — a capturing seam (out/err → string arrays) lets
 * the assertions stay white-box.
 */
import { describe, expect, it } from "bun:test"
import type {
  MachineClock,
  MachineError,
  MachineEvent,
  MachineResult,
} from "./envelopes"
import { JsonOutput, NdjsonOutput } from "./output"
import type { MachineStreams } from "./port"

/** Fixed-time, sequential-id clock — deterministic envelopes, no `crypto`. */
function fakeClock(): MachineClock {
  let n = 1
  return {
    now: () => 1_700_000_000_000,
    newId: () => `id-${n++}`,
  }
}

interface CapturingStreams {
  readonly streams: MachineStreams
  readonly outWrites: readonly string[]
  readonly errWrites: readonly string[]
}

function capturingStreams(): CapturingStreams {
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

/** Narrow an `unknown` JSON value to a string (or null), no `as`. */
function readString(value: unknown): string | null {
  return typeof value === "string" ? value : null
}

const SESSION_CREATED: MachineEvent = {
  event: "session.created",
  sessionId: "s1",
}

const TURN_SUBMITTED: MachineEvent = {
  event: "turn.submitted",
  sessionId: "s1",
  requestId: "r1",
}

describe("JsonOutput", () => {
  it("writes exactly one pretty envelope on complete", () => {
    const cap = capturingStreams()
    const out = new JsonOutput({ clock: fakeClock(), streams: cap.streams })

    out.start("status")
    out.emit(SESSION_CREATED)
    const result: MachineResult = { identity: { kind: "user", label: "brad" } }
    out.complete(result)

    expect(cap.outWrites.length).toBe(1)
    expect(cap.errWrites.length).toBe(0)
    const written = cap.outWrites[0] ?? ""
    expect(written.endsWith("\n")).toBe(true)

    const parsed = JSON.parse(written) as Record<string, unknown>
    expect(parsed.kind).toBe("command.completed")
    expect(parsed.schemaVersion).toBe(1)
    const correlationId = readString(parsed.correlationId)
    expect(correlationId).not.toBeNull()
    if (correlationId !== null) {
      expect(correlationId.length).toBeGreaterThan(0)
    }
    expect(out.correlationId === correlationId).toBe(true)
    expect(out.command).toBe("status")
    expect(out.exitCode).toBe(0)
  })

  it("emits no stdout for intermediate events", () => {
    const cap = capturingStreams()
    const out = new JsonOutput({ clock: fakeClock(), streams: cap.streams })

    out.start("status")
    out.emit(SESSION_CREATED)
    out.emit(TURN_SUBMITTED)
    out.emit(SESSION_CREATED)

    expect(cap.outWrites.length).toBe(0)
    expect(out.correlationId).not.toBeNull()
    expect(out.exitCode).toBe(0)
  })

  it("treats the terminal as exactly-once: further calls are no-ops", () => {
    const cap = capturingStreams()
    const out = new JsonOutput({ clock: fakeClock(), streams: cap.streams })

    out.start("status")
    out.complete(null)
    out.emit(SESSION_CREATED)
    out.complete({ ok: true })
    out.fail({ code: "runtime.network", message: "down" })

    expect(cap.outWrites.length).toBe(1)
    expect(cap.errWrites.length).toBe(0)
    expect(out.exitCode).toBe(0)
  })

  it("emits command.failed with runtime.exitCode=1 for runtime.*", () => {
    const cap = capturingStreams()
    const out = new JsonOutput({ clock: fakeClock(), streams: cap.streams })

    out.start("status")
    out.fail({ code: "runtime.network", message: "down" })

    const parsed = JSON.parse(cap.outWrites[0] ?? "{}") as Record<string, unknown>
    expect(parsed.kind).toBe("command.failed")
    const error = parsed.error as MachineError
    expect(error.code).toBe("runtime.network")
    expect(error.message).toBe("down")
    expect(parsed.exitCode).toBe(1)
    expect(out.exitCode).toBe(1)
  })

  it("emits command.failed with denial.exitCode=2 for usage.*", () => {
    const cap = capturingStreams()
    const out = new JsonOutput({ clock: fakeClock(), streams: cap.streams })

    out.start("status")
    out.fail({ code: "usage.flag", message: "bad flag" })

    const parsed = JSON.parse(cap.outWrites[0] ?? "{}") as Record<string, unknown>
    expect(parsed.kind).toBe("command.failed")
    expect(parsed.exitCode).toBe(2)
    expect(out.exitCode).toBe(2)
  })
})

describe("NdjsonOutput", () => {
  it("writes one NDJSON line per envelope (started + event + event + terminal)", () => {
    const cap = capturingStreams()
    const out = new NdjsonOutput({ clock: fakeClock(), streams: cap.streams })

    out.start("oneshot")
    out.emit(SESSION_CREATED)
    out.emit(TURN_SUBMITTED)
    out.complete(null)

    expect(cap.outWrites.length).toBe(4)
    expect(cap.errWrites.length).toBe(0)

    const lines = cap.outWrites.map((line) => JSON.parse(line) as Record<string, unknown>)

    expect(lines[0]?.kind).toBe("command.started")
    expect(lines[0]?.command).toBe("oneshot")

    expect(lines[1]?.kind).toBe("command.event")
    expect(lines[1]?.sequence).toBe(1)
    const firstEvent = lines[1]?.event as MachineEvent
    expect(firstEvent.event).toBe("session.created")
    expect(firstEvent.sessionId).toBe("s1")

    expect(lines[2]?.kind).toBe("command.event")
    expect(lines[2]?.sequence).toBe(2)
    const secondEvent = lines[2]?.event as MachineEvent
    expect(secondEvent.event).toBe("turn.submitted")
    expect(secondEvent.sessionId).toBe("s1")

    expect(lines[3]?.kind).toBe("command.completed")
    expect(lines[3]?.exitCode).toBe(0)
    expect(out.exitCode).toBe(0)

    // Every line is a single \n-terminated compact JSON object.
    for (const line of cap.outWrites) {
      expect(line.endsWith("\n")).toBe(true)
      expect(line.includes(" ")).toBe(false)
    }

    // Correlation root is shared by every line in this stream.
    const correlationId = readString(lines[0]?.correlationId)
    expect(correlationId).not.toBeNull()
    for (const line of lines) {
      expect(line.correlationId === correlationId).toBe(true)
    }
    expect(out.correlationId === correlationId).toBe(true)
    expect(out.command).toBe("oneshot")
  })

  it("treats the terminal as exactly-once: further calls are no-ops", () => {
    const cap = capturingStreams()
    const out = new NdjsonOutput({ clock: fakeClock(), streams: cap.streams })

    out.start("oneshot")
    out.emit(SESSION_CREATED)
    out.complete(null)
    out.emit(TURN_SUBMITTED)
    out.complete({ ok: true })
    out.fail({ code: "runtime.network", message: "down" })

    // started + one event + terminal = 3 lines; no extras after terminal.
    expect(cap.outWrites.length).toBe(3)
    expect(out.exitCode).toBe(0)
  })

  it("emits a terminal command.failed line for runtime.*", () => {
    const cap = capturingStreams()
    const out = new NdjsonOutput({ clock: fakeClock(), streams: cap.streams })

    out.start("oneshot")
    out.fail({ code: "runtime.network", message: "down" })

    const lines = cap.outWrites.map((line) => JSON.parse(line) as Record<string, unknown>)
    const terminal = lines[lines.length - 1] as Record<string, unknown>
    expect(terminal.kind).toBe("command.failed")
    expect(terminal.exitCode).toBe(1)
    const error = terminal.error as MachineError
    expect(error.code).toBe("runtime.network")
    expect(error.message).toBe("down")
    expect(out.exitCode).toBe(1)
  })

  it("maps denial-prefixed codes (usage.*/auth.*/denied.*) to exitCode=2", () => {
    const cases: ReadonlyArray<{ readonly code: string; readonly exitCode: number }> = [
      { code: "usage.flag", exitCode: 2 },
      { code: "auth.missing", exitCode: 2 },
      { code: "denied.policy", exitCode: 2 },
    ]
    for (const { code, exitCode } of cases) {
      const cap = capturingStreams()
      const out = new NdjsonOutput({ clock: fakeClock(), streams: cap.streams })
      out.start("oneshot")
      out.fail({ code, message: "nope" })
      expect(out.exitCode).toBe(exitCode)
      const lines = cap.outWrites.map((line) => JSON.parse(line) as Record<string, unknown>)
      const terminal = lines[lines.length - 1] as Record<string, unknown>
      expect(terminal.kind).toBe("command.failed")
      expect(terminal.exitCode).toBe(exitCode)
    }
  })
})

describe("diagnostic", () => {
  it("writes to err only, never out", () => {
    const cap = capturingStreams()
    const out = new JsonOutput({ clock: fakeClock(), streams: cap.streams })

    out.start("status")
    out.diagnostic("boom")

    expect(cap.outWrites.length).toBe(0)
    expect(cap.errWrites).toEqual(["boom\n"])
  })

  it("works the same for NdjsonOutput", () => {
    const cap = capturingStreams()
    const out = new NdjsonOutput({ clock: fakeClock(), streams: cap.streams })

    out.diagnostic("warn")

    expect(cap.outWrites.length).toBe(0)
    expect(cap.errWrites).toEqual(["warn\n"])
  })
})

describe("stdout/err discipline", () => {
  it("never writes an envelope on err (JsonOutput happy path)", () => {
    const cap = capturingStreams()
    const out = new JsonOutput({ clock: fakeClock(), streams: cap.streams })
    out.start("status")
    out.emit(SESSION_CREATED)
    out.complete({ ok: true })
    expect(cap.errWrites.length).toBe(0)
  })

  it("never writes an envelope on err (NdjsonOutput happy path)", () => {
    const cap = capturingStreams()
    const out = new NdjsonOutput({ clock: fakeClock(), streams: cap.streams })
    out.start("oneshot")
    out.emit(SESSION_CREATED)
    out.complete(null)
    expect(cap.errWrites.length).toBe(0)
  })

  it("never writes an envelope on err (NdjsonOutput fail path)", () => {
    const cap = capturingStreams()
    const out = new NdjsonOutput({ clock: fakeClock(), streams: cap.streams })
    out.start("oneshot")
    out.fail({ code: "runtime.network", message: "down" })
    expect(cap.errWrites.length).toBe(0)
  })
})
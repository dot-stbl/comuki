/**
 * Machine-mode oneshot (issue #80) — the envelope flow of
 * `comuki -m … --format json|ndjson` over a fake OneshotClient:
 * session create-or-reuse, submit, terminal result, failure mapping
 * and the usage.empty-message denial. Mirrors the tmpdir persistence
 * pattern of commands/oneshot.test.ts and the capturing-streams
 * pattern of machine/output.test.ts.
 */
import { afterEach, describe, expect, it } from "bun:test"
import { mkdtemp, rm } from "node:fs/promises"
import { tmpdir } from "node:os"
import { join } from "node:path"
import type { OneshotClient } from "../commands/oneshot"
import type {
  ChatSessionView,
  ChatTurnResultView,
} from "../lib/client"
import type { MachineClock } from "./envelopes"
import type { MachineStreams } from "./port"
import { runOneshotMachine } from "./oneshot-machine"

function fakeClock(): MachineClock {
  let counter = 1
  return {
    now: () => 1_700_000_000_000,
    newId: () => `id-${counter++}`,
  }
}

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

const session = (id: string, title: string): ChatSessionView => ({
  id,
  projectId: null,
  title,
  status: "active",
  createdAt: "2026-09-18T00:00:00Z",
  updatedAt: "2026-09-18T00:00:00Z",
})

const turn = (content: string): ChatTurnResultView => ({
  messages: [
    {
      id: "m1",
      role: "user",
      content: "hi",
      toolName: null,
      parts: null,
      meta: null,
      createdAt: "2026-09-18T00:00:00Z",
    },
    {
      id: "m2",
      role: "assistant",
      content,
      toolName: null,
      parts: null,
      meta: null,
      createdAt: "2026-09-18T00:00:01Z",
    },
  ],
  awaitingApproval: false,
  pendingPlan: null,
})

function fakeClient(options?: {
  readonly postError?: Error
}): OneshotClient {
  return {
    async createSession(request) {
      return session("s-new", request.title ?? "t")
    },
    async postMessage() {
      if (options?.postError) {
        throw options.postError
      }
      return turn("machine reply")
    },
  }
}

function parseLines(lines: readonly string[]): Record<string, unknown>[] {
  return lines.map((line) => JSON.parse(line) as Record<string, unknown>)
}

const dirs: string[] = []

afterEach(async () => {
  await Promise.all(
    dirs.splice(0).map((dir) => rm(dir, { recursive: true, force: true }))
  )
})

async function persistPath(): Promise<string> {
  const dir = await mkdtemp(join(tmpdir(), "comuki-oneshot-machine-"))
  dirs.push(dir)
  return join(dir, "sessions.json")
}

describe("runOneshotMachine — ndjson", () => {
  it("streams started, session.created, submitted, completed, terminal", async () => {
    const cap = capturingStreams()

    const exitCode = await runOneshotMachine({
      client: fakeClient(),
      message: "fix the login flow",
      format: "ndjson",
      persistPath: await persistPath(),
      clock: fakeClock(),
      streams: cap.streams,
    })

    expect(exitCode).toBe(0)
    expect(cap.errWrites.length).toBe(0)
    const lines = parseLines(cap.outWrites)
    expect(lines.length).toBe(5)

    expect(lines[0]?.kind).toBe("command.started")
    expect(lines[0]?.command).toBe("oneshot")

    expect(lines[1]?.kind).toBe("command.event")
    const created = lines[1]?.event as Record<string, unknown>
    expect(created.event).toBe("session.created")
    expect(created.sessionId).toBe("s-new")
    expect(created.title).toBe("fix the login flow")

    expect(lines[2]?.kind).toBe("command.event")
    const submitted = lines[2]?.event as Record<string, unknown>
    expect(submitted.event).toBe("turn.submitted")
    expect(submitted.sessionId).toBe("s-new")

    expect(lines[3]?.kind).toBe("command.event")
    const completed = lines[3]?.event as Record<string, unknown>
    expect(completed.event).toBe("turn.completed")
    expect(completed.messageCount).toBe(2)

    expect(lines[4]?.kind).toBe("command.completed")
    const result = lines[4]?.result as Record<string, unknown>
    expect(result.sessionId).toBe("s-new")
    expect(result.reply).toBe("machine reply")
  })

  it("omits session.created when reusing a persisted active session", async () => {
    const path = await persistPath()
    await Bun.write(
      path,
      JSON.stringify({
        activeSessionId: "s-live",
        sessions: [
          {
            id: "s-live",
            name: "existing",
            status: "done",
            createdAt: 1,
          },
        ],
      })
    )
    const cap = capturingStreams()
    const postedTo: string[] = []
    const client: OneshotClient = {
      async createSession() {
        throw new Error("must not create")
      },
      async postMessage(sessionId) {
        postedTo.push(sessionId)
        return turn("ok")
      },
    }

    const exitCode = await runOneshotMachine({
      client,
      message: "follow up",
      format: "ndjson",
      persistPath: path,
      clock: fakeClock(),
      streams: cap.streams,
    })

    expect(exitCode).toBe(0)
    expect(postedTo).toEqual(["s-live"])
    const lines = parseLines(cap.outWrites)
    expect(lines.length).toBe(4)
    for (const line of lines) {
      const event = line.event as Record<string, unknown> | undefined
      if (event) {
        expect(event.event).not.toBe("session.created")
      }
    }
    const terminal = lines[3] as Record<string, unknown>
    expect(terminal.kind).toBe("command.completed")
    const result = terminal.result as Record<string, unknown>
    expect(result.sessionId).toBe("s-live")
    expect(result.reply).toBe("ok")
  })

  it("emits turn.failed + terminal command.failed (exit 1) on post failure", async () => {
    const cap = capturingStreams()

    const exitCode = await runOneshotMachine({
      client: fakeClient({ postError: new Error("unable to connect") }),
      message: "hi",
      format: "ndjson",
      persistPath: await persistPath(),
      clock: fakeClock(),
      streams: cap.streams,
    })

    expect(exitCode).toBe(1)
    const lines = parseLines(cap.outWrites)
    expect(lines.length).toBe(5)

    expect(lines[3]?.kind).toBe("command.event")
    const failed = lines[3]?.event as Record<string, unknown>
    expect(failed.event).toBe("turn.failed")

    const terminal = lines[4] as Record<string, unknown>
    expect(terminal.kind).toBe("command.failed")
    expect(terminal.exitCode).toBe(1)
    const error = terminal.error as Record<string, unknown>
    expect(error.code).toBe("runtime.network")
  })
})

describe("runOneshotMachine — json", () => {
  it("writes exactly one terminal envelope with the reply", async () => {
    const cap = capturingStreams()

    const exitCode = await runOneshotMachine({
      client: fakeClient(),
      message: "fix the login flow",
      format: "json",
      persistPath: await persistPath(),
      clock: fakeClock(),
      streams: cap.streams,
    })

    expect(exitCode).toBe(0)
    expect(cap.outWrites.length).toBe(1)
    expect(cap.errWrites.length).toBe(0)
    const envelope = JSON.parse(
      cap.outWrites[0] ?? "{}"
    ) as Record<string, unknown>
    expect(envelope.kind).toBe("command.completed")
    expect(envelope.schemaVersion).toBe(1)
    const result = envelope.result as Record<string, unknown>
    expect(result.sessionId).toBe("s-new")
    expect(result.reply).toBe("machine reply")
  })

  it("returns 2 with a usage.empty-message denial envelope on empty input", async () => {
    for (const format of ["json", "ndjson"] as const) {
      const cap = capturingStreams()

      const exitCode = await runOneshotMachine({
        client: fakeClient(),
        message: "   ",
        format,
        persistPath: await persistPath(),
        clock: fakeClock(),
        streams: cap.streams,
      })

      expect(exitCode).toBe(2)
      const lines = parseLines(cap.outWrites)
      const terminal = lines[lines.length - 1] as Record<string, unknown>
      expect(terminal.kind).toBe("command.failed")
      expect(terminal.exitCode).toBe(2)
      const error = terminal.error as Record<string, unknown>
      expect(error.code).toBe("usage.empty-message")
    }
  })
})

import { afterEach, describe, expect, it } from "bun:test"
import { mkdtemp, rm } from "node:fs/promises"
import { tmpdir } from "node:os"
import { join } from "node:path"
import {
  assistantReplyText,
  ONESHOT_TIMEOUT_MS,
  resolveOneshotMode,
  runOneshot,
  type OneshotClient,
} from "./oneshot"
import type { ChatSessionView, ChatTurnResultView } from "../lib/client"
import { fromPersisted, readSessionsFile } from "../lib/sessions"

const session = (id: string, title = "t"): ChatSessionView => ({
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

describe("resolveOneshotMode", () => {
  it("treats -m as flag mode even on a TTY", () => {
    expect(resolveOneshotMode("hello", true)).toBe("flag")
    expect(resolveOneshotMode("hello", false)).toBe("flag")
    expect(resolveOneshotMode("", true)).toBe("flag")
  })

  it("pipes a non-TTY stdin when no -m is set", () => {
    expect(resolveOneshotMode(undefined, false)).toBe("stdin")
  })

  it("stays on the REPL for a TTY or an unknown stdin", () => {
    expect(resolveOneshotMode(undefined, true)).toBe("repl")
    expect(resolveOneshotMode(undefined, undefined)).toBe("repl")
  })
})

describe("assistantReplyText", () => {
  it("returns the last assistant content", () => {
    expect(assistantReplyText(turn("**done**"))).toBe("**done**")
  })

  it("prefers text parts over the content field", () => {
    const result: ChatTurnResultView = {
      messages: [
        {
          id: "m",
          role: "assistant",
          content: "fallback",
          toolName: null,
          parts: [
            { kind: "thinking", text: "hmm" },
            { kind: "text", markdown: "hello **world**" },
            { kind: "code", language: "ts", source: "const x = 1" },
          ],
          meta: null,
          createdAt: "2026-09-18T00:00:00Z",
        },
      ],
      awaitingApproval: false,
      pendingPlan: null,
    }
    expect(assistantReplyText(result)).toBe("hello **world**\n\nconst x = 1")
  })

  it("returns an empty string when no assistant message arrived", () => {
    expect(
      assistantReplyText({
        messages: [],
        awaitingApproval: false,
        pendingPlan: null,
      })
    ).toBe("")
  })
})

describe("ONESHOT_TIMEOUT_MS", () => {
  it("is a 120s abort window", () => {
    expect(ONESHOT_TIMEOUT_MS).toBe(120_000)
  })
})

describe("runOneshot", () => {
  const dirs: string[] = []

  afterEach(async () => {
    await Promise.all(dirs.splice(0).map((dir) => rm(dir, { recursive: true, force: true })))
  })

  async function persistPath(): Promise<string> {
    const dir = await mkdtemp(join(tmpdir(), "comuki-oneshot-"))
    dirs.push(dir)
    return join(dir, "sessions.json")
  }

  it("creates a session, posts the message, persists the tab", async () => {
    const created: string[] = []
    const posted: { id: string; message: string }[] = []
    const client: OneshotClient = {
      async createSession(request) {
        created.push(request.title ?? "")
        return session("s-new", request.title)
      },
      async postMessage(id, message) {
        posted.push({ id, message })
        return turn("plain reply")
      },
    }
    const path = await persistPath()

    const result = await runOneshot({
      client,
      message: "fix the login flow",
      persistPath: path,
    })

    expect(result).toEqual({ sessionId: "s-new", reply: "plain reply" })
    expect(created).toEqual(["fix the login flow"])
    expect(posted).toEqual([{ id: "s-new", message: "fix the login flow" }])
    const persisted = fromPersisted(await readSessionsFile(path))
    expect(persisted.sessions.map((item) => item.id)).toEqual(["s-new"])
    expect(persisted.sessions[0]?.name).toBe("fix the login flow")
  })

  it("reuses the persisted active session instead of creating another", async () => {
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
    const client: OneshotClient = {
      async createSession() {
        throw new Error("must not create")
      },
      async postMessage(id, message) {
        expect(id).toBe("s-live")
        expect(message).toBe("follow up")
        return turn("ok")
      },
    }

    const result = await runOneshot({
      client,
      message: "follow up",
      persistPath: path,
    })
    expect(result.sessionId).toBe("s-live")
    expect(result.reply).toBe("ok")
  })

  it("passes projectId into createSession", async () => {
    const client: OneshotClient = {
      async createSession(request) {
        expect(request.projectId).toBe("proj-1")
        return session("s1")
      },
      async postMessage() {
        return turn("x")
      },
    }
    await runOneshot({
      client,
      message: "hi",
      projectId: "proj-1",
      persistPath: await persistPath(),
    })
  })
})

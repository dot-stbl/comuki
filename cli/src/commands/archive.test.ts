import { afterEach, describe, expect, it } from "bun:test"
import { mkdtemp, rm } from "node:fs/promises"
import { tmpdir } from "node:os"
import { join } from "node:path"
import { resolveArchiveTarget } from "./archive"
import { writeSessionsFile } from "../lib/sessions"
import { newPendingSession } from "../lib/session-state"

const dirs: string[] = []

afterEach(async () => {
  await Promise.all(
    dirs.splice(0).map((dir) => rm(dir, { recursive: true, force: true }))
  )
})

async function persistPath(): Promise<string> {
  const dir = await mkdtemp(join(tmpdir(), "comuki-archive-resolve-"))
  dirs.push(dir)
  return join(dir, "sessions.json")
}

async function writeActive(
  path: string,
  sessions: ReturnType<typeof newPendingSession>[],
  activeIndex: number
): Promise<void> {
  await writeSessionsFile({ sessions, activeIndex }, path)
}

describe("resolveArchiveTarget", () => {
  it("resolves a live active session via --current", async () => {
    const path = await persistPath()
    const live = {
      ...newPendingSession(),
      id: "s-live",
      name: "live tab",
      status: "done" as const,
      hydrated: false,
    }
    await writeActive(path, [live], 0)

    const result = await resolveArchiveTarget({ current: true }, path)
    expect(result).toEqual({ sessionId: "s-live", sessionName: "live tab" })
  })

  it("returns an error when --current has only a pending tab", async () => {
    const path = await persistPath()
    const pendingId = `local-${Date.now()}-0`
    // Pending ids never reach the persisted file via `writeSessionsFile`
    // (it filters them out), so write the JSON directly to simulate a
    // tab that has never sent a message.
    await Bun.write(
      path,
      JSON.stringify({
        activeSessionId: pendingId,
        sessions: [{ id: pendingId, name: "new", status: "idle", createdAt: 1 }],
      })
    )

    const result = await resolveArchiveTarget({ current: true }, path)
    expect("error" in result).toBe(true)
    if ("error" in result) {
      expect(result.error).toMatch(/no server id yet/)
    }
  })

  it("returns an error when --current has no active session", async () => {
    const path = await persistPath()
    await writeActive(path, [], -1)

    const result = await resolveArchiveTarget({ current: true }, path)
    expect("error" in result).toBe(true)
    if ("error" in result) {
      expect(result.error).toMatch(/no active session/)
    }
  })

  it("resolves a session id that matches a local entry to its persisted name", async () => {
    const path = await persistPath()
    const session = {
      ...newPendingSession(),
      id: "s-named",
      name: "named tab",
      status: "done" as const,
      hydrated: false,
    }
    await writeActive(path, [session], 0)

    const result = await resolveArchiveTarget(
      { current: false, sessionId: "s-named" },
      path
    )
    expect(result).toEqual({ sessionId: "s-named", sessionName: "named tab" })
  })

  it("resolves an unknown session id with an empty name (host still archives it)", async () => {
    const path = await persistPath()
    await writeActive(path, [], -1)

    const result = await resolveArchiveTarget(
      { current: false, sessionId: "s-far-away" },
      path
    )
    expect(result).toEqual({ sessionId: "s-far-away", sessionName: "" })
  })

  it("returns an error when neither sessionId nor --current is given", async () => {
    const path = await persistPath()
    const live = {
      ...newPendingSession(),
      id: "s-live",
      name: "live",
      status: "done" as const,
      hydrated: false,
    }
    await writeActive(path, [live], 0)

    const result = await resolveArchiveTarget(
      { current: false, sessionId: undefined },
      path
    )
    expect("error" in result).toBe(true)
    if ("error" in result) {
      expect(result.error).toMatch(/usage/i)
    }
  })

  it("lets --current win when both are given", async () => {
    const path = await persistPath()
    const named = {
      ...newPendingSession(),
      id: "s-named",
      name: "named",
      status: "done" as const,
      hydrated: false,
    }
    const active = {
      ...newPendingSession(2027, 1),
      id: "s-active",
      name: "active",
      status: "done" as const,
      hydrated: false,
    }
    await writeActive(path, [named, active], 1)

    const result = await resolveArchiveTarget(
      { current: true, sessionId: "s-named" },
      path
    )
    expect(result).toEqual({ sessionId: "s-active", sessionName: "active" })
  })
})

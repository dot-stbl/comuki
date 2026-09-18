import { afterEach, describe, expect, it } from "bun:test"
import { mkdtemp, rm } from "node:fs/promises"
import { tmpdir } from "node:os"
import { join } from "node:path"
import { JsonSessionStore } from "./session-store"

const temporaryDirectories: string[] = []

afterEach(async () => {
  await Promise.all(
    temporaryDirectories.splice(0).map((path) =>
      rm(path, { force: true, recursive: true })
    )
  )
})

async function temporarySessionsPath(): Promise<string> {
  const directory = await mkdtemp(join(tmpdir(), "comuki-session-store-"))
  temporaryDirectories.push(directory)
  return join(directory, "nested", "sessions.json")
}

describe("JsonSessionStore", () => {
  it("returns an empty document for a missing file", async () => {
    expect(await new JsonSessionStore(await temporarySessionsPath()).read()).toEqual({
      sessions: [],
    })
  })

  it("returns an empty document for malformed JSON", async () => {
    const path = await temporarySessionsPath()
    await Bun.write(path, "not json", { createPath: true })

    expect(await new JsonSessionStore(path).read()).toEqual({ sessions: [] })
  })

  it("round-trips the persisted document", async () => {
    const store = new JsonSessionStore(await temporarySessionsPath())
    const persisted = {
      activeSessionId: "s2",
      sessions: [
        { id: "s1", name: "one", status: "done" as const, createdAt: 1 },
        { id: "s2", name: "two", status: "running" as const, createdAt: 2 },
      ],
    }

    await store.write(persisted)

    expect(await store.read()).toEqual(persisted)
  })
})

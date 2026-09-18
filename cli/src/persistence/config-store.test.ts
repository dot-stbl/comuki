import { afterEach, describe, expect, it } from "bun:test"
import { mkdtemp, readFile, rm, stat } from "node:fs/promises"
import { tmpdir } from "node:os"
import { join } from "node:path"
import { JsonConfigStore } from "./config-store"

const temporaryDirectories: string[] = []

afterEach(async () => {
  await Promise.all(
    temporaryDirectories.splice(0).map((path) =>
      rm(path, { force: true, recursive: true })
    )
  )
})

async function temporaryConfigPath(): Promise<string> {
  const directory = await mkdtemp(join(tmpdir(), "comuki-config-store-"))
  temporaryDirectories.push(directory)
  return join(directory, "nested", "config.json")
}

describe("JsonConfigStore", () => {
  it("returns empty config when the file is missing or malformed", async () => {
    const path = await temporaryConfigPath()
    const store = new JsonConfigStore(path)
    expect(await store.read()).toEqual({})
    await Bun.write(path, "not json", { createPath: true })

    expect(await store.read()).toEqual({})
  })

  it("preserves unknown fields during an update", async () => {
    const path = await temporaryConfigPath()
    const store = new JsonConfigStore(path)
    await Bun.write(
      path,
      JSON.stringify({ cookie: "session=secret", future: { enabled: true } }),
      { createPath: true }
    )

    const updated = await store.update((current) => ({
      ...current,
      theme: "dockside-dark",
    }))

    expect(updated).toEqual({
      cookie: "session=secret",
      future: { enabled: true },
      theme: "dockside-dark",
    })
    const persisted: unknown = JSON.parse(await readFile(path, "utf8"))
    expect(persisted).toEqual(updated)
  })

  it("serializes concurrent read-modify-write operations", async () => {
    const store = new JsonConfigStore(await temporaryConfigPath())

    await Promise.all([
      store.update(async (current) => {
        await Bun.sleep(10)
        return { ...current, bell: false }
      }),
      store.update((current) => ({ ...current, preferredProfile: "implement" })),
    ])

    expect(await store.read()).toEqual({
      bell: false,
      preferredProfile: "implement",
    })
  })

  it("writes owner-only files on platforms with unix modes", async () => {
    const path = await temporaryConfigPath()
    const store = new JsonConfigStore(path)

    await store.update(() => ({ bell: true }))

    if (process.platform !== "win32") {
      expect((await stat(path)).mode & 0o777).toBe(0o600)
    }
  })
})

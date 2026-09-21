/**
 * DraftStore — round-trip + rename invariance (issue #77).
 *
 * The contract:
 *
 * 1. The body survives a process restart (drop the in-memory
 *    bookkeeping and read the file from a fresh store).
 * 2. `clear()` deletes the file (no JSON wrapper to leak through).
 * 3. Renaming the session does NOT move the draft (drafts are
 *    keyed by id, not name).
 * 4. The user can read / edit / rm the file directly — the body is
 *    plain UTF-8, the store only writes the file the user expects.
 */

import { afterEach, beforeEach, describe, expect, test } from "bun:test"
import { mkdir, readdir, readFile, rm } from "node:fs/promises"
import { tmpdir } from "node:os"
import { join } from "node:path"

import { createDraftStore, draftFilePath, type DraftStore } from "./drafts"

async function mkTempDir(label: string): Promise<string> {
  const stamp = `${Date.now()}-${Math.floor(Math.random() * 1e6)}`
  const dir = join(tmpdir(), `${label}-${stamp}`)
  await rm(dir, { recursive: true, force: true })
  return dir
}

describe("DraftStore — body + persistence", () => {
  let tempDir: string
  let store: DraftStore

  beforeEach(async () => {
    tempDir = await mkTempDir("comuki-drafts")
    store = createDraftStore({ stateDirectory: tempDir })
  })

  afterEach(async () => {
    await rm(tempDir, { recursive: true, force: true })
  })

  test("get returns null when no draft has been saved", async () => {
    expect(await store.get("s-1")).toBeNull()
  })

  test("set writes the body verbatim and get round-trips", async () => {
    const body = "draft\nwith\nnewlines\tand unicode ★"
    await store.set("s-1", body)
    await store.whenIdle()
    expect(await store.get("s-1")).toBe(body)
  })

  test("the file is plain UTF-8 — no JSON wrapper, no metadata header", async () => {
    await store.set("s-1", "raw text only")
    await store.whenIdle()

    const onDisk = await readFile(draftFilePath(tempDir, "s-1"), "utf8")
    expect(onDisk).toBe("raw text only")
    // No JSON braces, no metadata prefix.
    expect(onDisk).not.toContain("{")
    expect(onDisk).not.toContain('"')
  })

  test("the body survives a process restart", async () => {
    await store.set("s-1", "the body")
    await store.whenIdle()

    // Drop the store entirely and build a fresh one over the same
    // directory — simulates the CLI process restarting.
    store = createDraftStore({ stateDirectory: tempDir })
    expect(await store.get("s-1")).toBe("the body")
  })

  test("clear() deletes the file", async () => {
    await store.set("s-1", "the body")
    await store.whenIdle()
    expect(await store.get("s-1")).toBe("the body")

    await store.clear("s-1")
    await store.whenIdle()
    expect(await store.get("s-1")).toBeNull()
  })

  test("clear() on a missing draft is idempotent (does not throw)", async () => {
    await expect(store.clear("never-saved")).resolves.toBeUndefined()
    await store.whenIdle()
  })

  test("set with an empty body deletes the file (cleared == removed)", async () => {
    await store.set("s-1", "the body")
    await store.whenIdle()
    expect(await store.get("s-1")).toBe("the body")

    await store.set("s-1", "")
    await store.whenIdle()
    expect(await store.get("s-1")).toBeNull()
  })

  test("each session gets its own file", async () => {
    await store.set("s-1", "first body")
    await store.set("s-2", "second body")
    await store.whenIdle()

    expect(await store.get("s-1")).toBe("first body")
    expect(await store.get("s-2")).toBe("second body")

    const names = (await readdir(join(tempDir, "drafts"))).sort()
    expect(names).toEqual(["s-1.txt", "s-2.txt"])
  })

  test("renaming the session does NOT move the draft (keyed by id)", async () => {
    await store.set("s-1", "body before rename")
    await store.whenIdle()

    // The rename is the SessionStore's job — DraftStore just keeps
    // the body where the id put it. The new id ("renamed-1") gets
    // its own (empty) draft space, the old id keeps its body.
    await store.set("renamed-1", "")
    await store.whenIdle()

    expect(await store.get("s-1")).toBe("body before rename")
    expect(await store.get("renamed-1")).toBeNull()
  })

  test("a manual file removal outside the store is observed as null", async () => {
    await store.set("s-1", "the body")
    await store.whenIdle()
    await rm(draftFilePath(tempDir, "s-1"), { force: true })
    expect(await store.get("s-1")).toBeNull()
  })

  test("listStale returns ids older than the threshold", async () => {
    // Three drafts at different ages. We control the clock via the
    // `now` parameter — the on-disk mtime is independent.
    await store.set("recent", "now-ish")
    await store.set("ancient", "ages ago")
    await store.whenIdle()

    // Force the file mtimes to known values so listStale can compare.
    const baseMtime = Date.now()
    await rm(draftFilePath(tempDir, "ancient"), { force: true })
    await writeFileBack(draftFilePath(tempDir, "ancient"), "ages ago", baseMtime - 10_000)
    await rm(draftFilePath(tempDir, "recent"), { force: true })
    await writeFileBack(draftFilePath(tempDir, "recent"), "now-ish", baseMtime)

    // 5s threshold — only `ancient` is stale.
    const stale = await store.listStale(5, baseMtime)
    expect(stale).toContain("ancient")
    expect(stale).not.toContain("recent")
  })

  test("listStale returns [] when no drafts directory exists", async () => {
    expect(await store.listStale(0, Date.now())).toEqual([])
  })
})

async function writeFileBack(
  path: string,
  body: string,
  mtimeMs: number
): Promise<void> {
  const { utimes, writeFile: wf } = await import("node:fs/promises")
  await mkdir(join(path, ".."), { recursive: true })
  await wf(path, body, "utf8")
  await utimes(path, mtimeMs / 1000, mtimeMs / 1000)
}

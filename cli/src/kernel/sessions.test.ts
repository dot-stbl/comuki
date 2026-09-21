/**
 * SessionStore — NDJSON round-trip and idempotent fork (issue #77).
 *
 * The store survives a process restart (drop the in-memory map, re-
 * read the file) without losing any session reference. Concurrent
 * forks of the same source session with the same newId collapse to
 * one entry — the second fork returns the first fork's meta, no
 * duplicate line on disk.
 *
 * Tests use a fresh temp directory per case; the production code
 * points at `defaultStateDirectory() + SESSIONS_FILE` (the same
 * XDG/LOCALAPPDATA root receipts already uses).
 */

import { afterEach, beforeEach, describe, expect, test } from "bun:test"
import { readFile, rm } from "node:fs/promises"
import { tmpdir } from "node:os"
import { join } from "node:path"

import {
  createSessionStore,
  decodeMeta,
  encodeMeta,
  sessionsFilePath,
  type SessionMeta,
  type SessionStore,
} from "./sessions"

function mkTempDir(label: string): Promise<string> {
  const stamp = `${Date.now()}-${Math.floor(Math.random() * 1e6)}`
  const dir = join(tmpdir(), `${label}-${stamp}`)
  return rm(dir, { recursive: true, force: true }).then(() => dir)
}

describe("SessionStore — NDJSON round-trip", () => {
  let tempDir: string
  let store: SessionStore
  let clock: () => number

  beforeEach(async () => {
    tempDir = await mkTempDir("comuki-sessions")
    let ticks = 0
    clock = () => {
      ticks += 1
      return 1_700_000_000_000 + ticks * 1_000
    }
    store = createSessionStore({ stateDirectory: tempDir, now: clock })
  })

  afterEach(async () => {
    await rm(tempDir, { recursive: true, force: true })
  })

  test("upsert writes one well-formed NDJSON line per call", async () => {
    const written = await store.upsert("s-1", { name: "First" })
    expect(written.id).toBe("s-1")
    expect(written.name).toBe("First")
    expect(written.createdAt).toBe(1_700_000_001_000)
    expect(written.lastKnownCursor).toBe(0)
    expect(written.renamed).toBe(false)
    expect(written.archived).toBe(false)

    await store.whenIdle()
    const text = await readFile(sessionsFilePath(tempDir), "utf8")
    const lines = text.split("\n").filter((line) => line.length > 0)
    expect(lines).toHaveLength(1)

    const decoded = decodeMeta(lines[0]!)
    expect(decoded).not.toBeNull()
    expect(decoded?.id).toBe("s-1")
    expect(decoded?.name).toBe("First")
    expect(decoded?.createdAt).toBe(1_700_000_001_000)
  })

  test("upsert merges on existing id without clobbering siblings", async () => {
    await store.upsert("s-1", { name: "First" })
    await store.upsert("s-1", { lastKnownCursor: 42 })
    await store.whenIdle()

    const entry = await store.get("s-1")
    expect(entry).not.toBeNull()
    expect(entry?.name).toBe("First") // not clobbered by cursor write
    expect(entry?.lastKnownCursor).toBe(42)
    expect(entry?.renamed).toBe(false)
  })

  test("round-trip: open 3 sessions, list them, rename one, archive another, restart, list again", async () => {
    await store.upsert("s-1", { name: "First" })
    await store.upsert("s-2", { name: "Second" })
    await store.upsert("s-3", { name: "Third" })
    await store.whenIdle()

    const initialList = await store.list()
    expect(initialList.map((entry) => entry.id)).toEqual(["s-1", "s-2", "s-3"])

    await store.rename("s-2", "Second renamed")
    await store.archive("s-1")
    await store.whenIdle()

    // Drop in-memory cache and rebuild from disk — the contract.
    store = createSessionStore({ stateDirectory: tempDir, now: clock })
    // With last-write-wins, the in-memory map reflects the latest
    // write per id: s-2's rename (archived: false) and s-1's
    // archive (archived: true). s-3 was never touched.
    const live = await store.list({ archived: false })
    expect(live.map((entry) => entry.id).sort()).toEqual(["s-2", "s-3"])
    expect(live.find((entry) => entry.id === "s-2")?.name).toBe("Second renamed")
    expect(live.find((entry) => entry.id === "s-2")?.renamed).toBe(true)

    const archived = await store.list({ archived: true })
    expect(archived.map((entry) => entry.id)).toEqual(["s-1"])
    expect((await store.get("s-1"))?.archived).toBe(true)
  })

  test("rename collapses whitespace and rejects empty input", async () => {
    await store.upsert("s-1", { name: "Original" })
    await store.rename("s-1", "  double   spaced  ")
    await store.whenIdle()

    const entry = await store.get("s-1")
    expect(entry?.name).toBe("double spaced")
    expect(entry?.renamed).toBe(true)

    // Empty / whitespace-only rename is a no-op (returns null),
    // preserves the existing name.
    const before = (await store.get("s-1"))?.name
    const result = await store.rename("s-1", "   ")
    expect(result).toBeNull()
    expect((await store.get("s-1"))?.name).toBe(before)
  })

  test("archive is idempotent — second call returns the current meta without rewriting", async () => {
    await store.upsert("s-1", { name: "First" })
    const first = await store.archive("s-1")
    await store.whenIdle()
    expect(first?.archived).toBe(true)

    const before = (await readFile(sessionsFilePath(tempDir), "utf8")).length
    const second = await store.archive("s-1")
    await store.whenIdle()
    const after = (await readFile(sessionsFilePath(tempDir), "utf8")).length
    expect(second?.archived).toBe(true)
    expect(after).toBe(before) // no extra line
  })

  test("search matches case-insensitively against id and name", async () => {
    await store.upsert("alpha-1", { name: "First" })
    await store.upsert("beta-2", { name: "Second" })
    await store.upsert("gamma-3", { name: "Third" })
    await store.whenIdle()

    const byId = await store.search("BETA")
    expect(byId.map((entry) => entry.id)).toEqual(["beta-2"])

    const byName = await store.search("third")
    expect(byName.map((entry) => entry.id)).toEqual(["gamma-3"])

    expect(await store.search("nothing-here")).toEqual([])
    expect(await store.search("")).toEqual([])
  })

  test("filter combines archived/renamed flags with AND", async () => {
    await store.upsert("s-1", { name: "Live" })
    await store.upsert("s-2", { name: "Renamed" })
    await store.upsert("s-3", { name: "Dead" })
    await store.rename("s-2", "Renamed")
    await store.archive("s-3")
    await store.whenIdle()

    const live = await store.list({ archived: false })
    expect(live.map((entry) => entry.id).sort()).toEqual(["s-1", "s-2"])

    const renamed = await store.list({ renamed: true })
    expect(renamed.map((entry) => entry.id)).toEqual(["s-2"])

    const intersection = await store.list({ archived: false, renamed: true })
    expect(intersection.map((entry) => entry.id)).toEqual(["s-2"])
  })

  test("compact rewrites the file from the in-memory map, dropping stale lines", async () => {
    await store.upsert("s-1", { name: "First" })
    await store.upsert("s-1", { name: "Second" })
    await store.upsert("s-1", { name: "Third" })
    await store.whenIdle()

    const beforeCompact = (await readFile(sessionsFilePath(tempDir), "utf8"))
      .split("\n")
      .filter((line) => line.length > 0)
    expect(beforeCompact).toHaveLength(3)

    await store.compact()

    const afterCompact = (await readFile(sessionsFilePath(tempDir), "utf8"))
      .split("\n")
      .filter((line) => line.length > 0)
    expect(afterCompact).toHaveLength(1)
    const decoded = decodeMeta(afterCompact[0]!)
    expect(decoded?.name).toBe("Third")
  })
})

describe("SessionStore — concurrent fork idempotency", () => {
  let tempDir: string
  let store: SessionStore
  let clock: () => number

  beforeEach(async () => {
    tempDir = await mkTempDir("comuki-sessions-fork")
    let ticks = 0
    clock = () => {
      ticks += 1
      return 1_700_000_000_000 + ticks * 1_000
    }
    store = createSessionStore({ stateDirectory: tempDir, now: clock })
  })

  afterEach(async () => {
    await rm(tempDir, { recursive: true, force: true })
  })

  test("two concurrent forks of the same session return the same newId", async () => {
    await store.upsert("source", { name: "Source" })
    await store.whenIdle()

    const [first, second] = await Promise.all([
      store.fork("source", "forked"),
      store.fork("source", "forked"),
    ])

    expect(first?.id).toBe("forked")
    expect(second?.id).toBe("forked")
    expect(first?.name).toBe("Source")
    expect(second?.name).toBe("Source")
    expect(first?.lastKnownCursor).toBe(0)
    expect(first?.createdAt).toBe(second?.createdAt) // same clock tick
    await store.whenIdle()

    // The on-disk file has TWO lines for "source" (the original +
    // its upsert) plus ONE line for "forked" — the second fork was
    // a no-op write.
    const lines = (await readFile(sessionsFilePath(tempDir), "utf8"))
      .split("\n")
      .filter((line) => line.length > 0)
    const forkLines = lines
      .map((line) => decodeMeta(line))
      .filter((entry): entry is SessionMeta => entry !== null && entry.id === "forked")
    expect(forkLines).toHaveLength(1)
  })

  test("fork returns null when the source id is unknown", async () => {
    const fork = await store.fork("does-not-exist", "forked")
    expect(fork).toBeNull()
  })

  test("the fork inherits the source's name but resets cursor + renamed flags", async () => {
    await store.upsert("source", { name: "Source" })
    await store.rename("source", "Renamed source")
    await store.upsert("source", { lastKnownCursor: 99 })
    await store.whenIdle()

    const fork = await store.fork("source", "forked")
    expect(fork?.name).toBe("Renamed source")
    expect(fork?.renamed).toBe(false)
    expect(fork?.lastKnownCursor).toBe(0)
  })
})

describe("encodeMeta / decodeMeta — line format", () => {
  test("encodeMeta produces exactly one line per call (no embedded newlines)", () => {
    const meta: SessionMeta = {
      id: "s-1",
      name: "multi\nline\nname",
      createdAt: 1_700_000_000_000,
      lastKnownCursor: 0,
      renamed: false,
      archived: false,
    }
    const line = encodeMeta(meta)
    expect(line.endsWith("\n")).toBe(true)
    // The newline-bearing name is encoded as \n inside the JSON
    // string — the row itself stays a single line.
    expect(line.split("\n")).toHaveLength(2)
  })

  test("decodeMeta returns null on empty / malformed input", () => {
    expect(decodeMeta("")).toBeNull()
    expect(decodeMeta("not-json")).toBeNull()
    expect(decodeMeta(JSON.stringify({ id: "x" }))).toBeNull()
    expect(decodeMeta(JSON.stringify({}))).toBeNull()
    expect(decodeMeta(JSON.stringify(null))).toBeNull()
    expect(decodeMeta(JSON.stringify({ id: "x", name: "n", createdAt: "not-a-number" }))).toBeNull()
  })

  test("decodeMeta accepts well-formed meta and round-trips", () => {
    const meta: SessionMeta = {
      id: "s-1",
      name: "First",
      createdAt: 1_700_000_000_000,
      lastKnownCursor: 42,
      renamed: true,
      archived: false,
    }
    const decoded = decodeMeta(encodeMeta(meta))
    expect(decoded).toEqual(meta)
  })
})

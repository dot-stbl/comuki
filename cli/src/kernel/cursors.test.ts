/**
 * CursorStore — reconnect contract (issue #77).
 *
 * Three behaviours, all idempotent on retry:
 *
 * 1. `set` rejects backwards writes (the realtime hub never
 *    rewinds, so a non-monotonic write signals a clock skew or a
 *    server-side bug — the caller surfaces it instead of silently
 *    overwriting the cursor).
 * 2. `advance` is the safe monotonic bump — it never goes
 *    backwards, and it is a no-op when the local cursor is already
 *    at-or-past the target.
 * 3. `catchUp` is the reconnect primitive. It returns the gap the
 *    caller must backfill when the server is ahead, the gap the
 *    caller must push upstream when the local is ahead, and `null`
 *    when both sides agree. The dedupe set is consumed on every
 *    call so a retry does not re-apply events the client has
 *    already seen.
 */

import { afterEach, beforeEach, describe, expect, test } from "bun:test"
import { readFile, rm } from "node:fs/promises"
import { tmpdir } from "node:os"
import { join } from "node:path"

import {
  createCursorStore,
  cursorsFilePath,
  decodeCursor,
  encodeCursor,
  type CursorStore,
} from "./cursors"

async function mkTempDir(label: string): Promise<string> {
  const stamp = `${Date.now()}-${Math.floor(Math.random() * 1e6)}`
  const dir = join(tmpdir(), `${label}-${stamp}`)
  await rm(dir, { recursive: true, force: true })
  return dir
}

describe("CursorStore — set / advance / get", () => {
  let tempDir: string
  let store: CursorStore
  let clock: () => number

  beforeEach(async () => {
    tempDir = await mkTempDir("comuki-cursors")
    let ticks = 0
    clock = () => {
      ticks += 1
      return 1_700_000_000_000 + ticks * 1_000
    }
    store = createCursorStore({ stateDirectory: tempDir, now: clock })
  })

  afterEach(async () => {
    await rm(tempDir, { recursive: true, force: true })
  })

  test("get returns null for an unknown session", async () => {
    expect(await store.get("s-1")).toBeNull()
  })

  test("set writes one well-formed NDJSON line per call", async () => {
    expect(await store.set("s-1", 1_000, 1_700_000_001_000)).toBe(true)
    await store.whenIdle()
    expect(await store.get("s-1")).toBe(1_000)

    const text = await readFile(cursorsFilePath(tempDir), "utf8")
    const lines = text.split("\n").filter((line) => line.length > 0)
    expect(lines).toHaveLength(1)

    const decoded = decodeCursor(lines[0]!)
    expect(decoded?.sessionId).toBe("s-1")
    expect(decoded?.cursor).toBe(1_000)
    expect(decoded?.updatedAt).toBe(1_700_000_001_000)
  })

  test("set rejects a backwards write (clock skew / server bug signal)", async () => {
    expect(await store.set("s-1", 1_000, 1_700_000_001_000)).toBe(true)
    await store.whenIdle()
    const second = await store.set("s-1", 500, 1_700_000_002_000)
    await store.whenIdle()
    expect(second).toBe(false)
    // The stored cursor is unchanged.
    expect(await store.get("s-1")).toBe(1_000)
  })

  test("advance never goes backwards — equal cursor is a no-op", async () => {
    expect(await store.set("s-1", 1_000)).toBe(true)
    await store.whenIdle()
    const advanced = await store.advance("s-1", 1_000)
    expect(advanced).toBe(1_000)

    // No additional NDJSON line was written for the no-op call.
    const text = await readFile(cursorsFilePath(tempDir), "utf8")
    const lines = text.split("\n").filter((line) => line.length > 0)
    expect(lines).toHaveLength(1)
  })

  test("advance past the current cursor writes the new value", async () => {
    expect(await store.set("s-1", 1_000)).toBe(true)
    await store.whenIdle()
    expect(await store.advance("s-1", 5_000)).toBe(5_000)
    await store.whenIdle()
    expect(await store.get("s-1")).toBe(5_000)
  })

  test("snapshot returns every stored cursor as a frozen record", async () => {
    expect(await store.set("s-1", 100)).toBe(true)
    expect(await store.set("s-2", 200)).toBe(true)
    expect(await store.set("s-3", 300)).toBe(true)
    await store.whenIdle()

    const snap = await store.snapshot()
    expect(snap).toEqual({ "s-1": 100, "s-2": 200, "s-3": 300 })
    expect(Object.isFrozen(snap)).toBe(true)
  })

  test("cursors survive a process restart (in-memory reload from disk)", async () => {
    expect(await store.set("s-1", 100)).toBe(true)
    expect(await store.set("s-2", 200)).toBe(true)
    await store.whenIdle()

    // Drop in-memory map and re-read from disk — simulates restart.
    store = createCursorStore({ stateDirectory: tempDir, now: clock })
    expect(await store.get("s-1")).toBe(100)
    expect(await store.get("s-2")).toBe(200)
  })
})

describe("CursorStore — catchUp reconnect primitive", () => {
  let tempDir: string
  let store: CursorStore
  let clock: () => number

  beforeEach(async () => {
    tempDir = await mkTempDir("comuki-cursors-catchup")
    let ticks = 0
    clock = () => {
      ticks += 1
      return 1_700_000_000_000 + ticks * 1_000
    }
    store = createCursorStore({ stateDirectory: tempDir, now: clock })
  })

  afterEach(async () => {
    await rm(tempDir, { recursive: true, force: true })
  })

  test("server ahead: catchUp returns the gap and marks backfillNeeded", async () => {
    await store.set("s-1", 100)
    await store.whenIdle()

    const dedupe = new Set<number>()
    const result = await store.catchUp({
      sessionId: "s-1",
      authoritativeCursor: 250,
      dedupeSet: dedupe,
    })

    expect(result.gap).toEqual({ start: 101, end: 250 })
    expect(result.backfillNeeded).toBe(true)
    expect(result.dedupeApplied).toBe(0)
    expect(dedupe.size).toBe(0) // consumed regardless of size
    await store.whenIdle()
    expect(await store.get("s-1")).toBe(250) // cursor advanced
  })

  test("catchUp is idempotent — a second call returns gap: null", async () => {
    await store.set("s-1", 100)
    await store.whenIdle()

    const first = await store.catchUp({
      sessionId: "s-1",
      authoritativeCursor: 250,
      dedupeSet: new Set(),
    })
    expect(first.gap).toEqual({ start: 101, end: 250 })

    const second = await store.catchUp({
      sessionId: "s-1",
      authoritativeCursor: 250,
      dedupeSet: new Set(),
    })
    expect(second.gap).toBeNull()
    expect(second.backfillNeeded).toBe(false)
  })

  test("catchUp dedupes — already-seen event ids are reported and consumed", async () => {
    await store.set("s-1", 100)
    await store.whenIdle()

    const dedupe = new Set<number>([101, 102, 103, 150, 200])
    const result = await store.catchUp({
      sessionId: "s-1",
      authoritativeCursor: 250,
      dedupeSet: dedupe,
    })

    // The store does NOT shrink the gap for dedupe — that is the
    // caller's job (the hub-side query layer filters). The store's
    // job is bookkeeping: the dedupe set is consumed so the next
    // reconnect doesn't re-apply those events.
    expect(result.gap).toEqual({ start: 101, end: 250 })
    expect(result.dedupeApplied).toBe(5)
    expect(dedupe.size).toBe(0) // consumed

    await store.whenIdle()
    expect(await store.get("s-1")).toBe(250)
  })

  test("catchUp reduces the gap on each call as the server streams more events", async () => {
    await store.set("s-1", 100)
    await store.whenIdle()

    // First catchUp: server is at 200, we were at 100.
    const first = await store.catchUp({
      sessionId: "s-1",
      authoritativeCursor: 200,
      dedupeSet: new Set([101, 102]),
    })
    expect(first.gap).toEqual({ start: 101, end: 200 })
    await store.whenIdle()
    expect(await store.get("s-1")).toBe(200)

    // Second catchUp: server has streamed another 100 events.
    const second = await store.catchUp({
      sessionId: "s-1",
      authoritativeCursor: 300,
      dedupeSet: new Set([201, 202, 203]),
    })
    expect(second.gap).toEqual({ start: 201, end: 300 })
    await store.whenIdle()
    expect(await store.get("s-1")).toBe(300)
  })

  test("an old authoritative cursor still triggers a backfill (server rewinds are surfaced)", async () => {
    // The client has caught up to 500; the server briefly reports
    // an older cursor (e.g. a stale load balancer node). The store
    // rejects the backwards set but catchUp with that old cursor
    // returns no gap — the client's view is already fresher.
    await store.set("s-1", 500)
    await store.whenIdle()

    const result = await store.catchUp({
      sessionId: "s-1",
      authoritativeCursor: 300,
      dedupeSet: new Set(),
    })

    // Server is BEHIND the client — the local view is fresher.
    // The store returns the [server+1, existing] range so the
    // caller knows to push local writes upstream.
    expect(result.gap).toEqual({ start: 301, end: 500 })
    expect(result.backfillNeeded).toBe(false)
    // The stored cursor is unchanged (server is the only authoritative
    // source of truth — we don't roll back).
    expect(await store.get("s-1")).toBe(500)
  })

  test("catchUp with the same authoritative cursor on a fresh store returns the full backfill", async () => {
    // The client crashed and rehydrates with no in-memory state.
    // The file says cursor = 100 (from before the crash); the server
    // is now at 250. catchUp returns the entire [101..250] range.
    await store.set("s-1", 100)
    await store.whenIdle()

    store = createCursorStore({ stateDirectory: tempDir, now: clock })
    const result = await store.catchUp({
      sessionId: "s-1",
      authoritativeCursor: 250,
      dedupeSet: new Set(),
    })

    expect(result.gap).toEqual({ start: 101, end: 250 })
    expect(result.backfillNeeded).toBe(true)
    await store.whenIdle()
    expect(await store.get("s-1")).toBe(250)
  })
})

describe("encodeCursor / decodeCursor — line format", () => {
  test("encodeCursor produces exactly one line per call", () => {
    const entry = {
      sessionId: "s-1",
      cursor: 1_700_000_000_000,
      updatedAt: 1_700_000_000_001,
    }
    const line = encodeCursor(entry)
    expect(line.endsWith("\n")).toBe(true)
    expect(line.split("\n")).toHaveLength(2) // payload + trailing newline
  })

  test("decodeCursor returns null on empty / malformed input", () => {
    expect(decodeCursor("")).toBeNull()
    expect(decodeCursor("not-json")).toBeNull()
    expect(decodeCursor(JSON.stringify({ sessionId: "x" }))).toBeNull()
    expect(decodeCursor(JSON.stringify({ sessionId: "x", cursor: 1 }))).toBeNull()
    expect(decodeCursor(JSON.stringify(null))).toBeNull()
  })

  test("decodeCursor round-trips a well-formed entry", () => {
    const entry = {
      sessionId: "s-1",
      cursor: 42,
      updatedAt: 1_700_000_000_000,
    }
    expect(decodeCursor(encodeCursor(entry))).toEqual(entry)
  })
})

/**
 * Telemetry round-trip (issue #81) — write 100 events, drop the
 * in-memory cache, read the file back, assert every event decodes.
 * Concurrent writers don't interleave (chained lane).
 *
 * Three layers of coverage:
 *
 *   1. schema — every encoded line parses with the same shape.
 *   2. concurrency — N concurrent log() calls land as N consecutive
 *      lines in the file (no torn writes).
 *   3. failure — a write to a read-only directory surfaces on stderr
 *      but does not throw; the dispatch path keeps going.
 */

import { afterEach, beforeEach, describe, expect, test } from "bun:test"
import { rm } from "node:fs/promises"
import { tmpdir } from "node:os"
import { join } from "node:path"

import {
  createStructuredLog,
  decodeEvent,
  diagnosticsFilePath,
  encodeEvent,
  readEvents,
  type StructuredLog,
  type StructuredLogEvent,
} from "./telemetry"

async function mkTempDir(label: string): Promise<string> {
  const stamp = `${Date.now()}-${Math.floor(Math.random() * 1e6)}`
  const dir = join(tmpdir(), `${label}-${stamp}`)
  await rm(dir, { recursive: true, force: true })
  return dir
}

describe("StructuredLog — encoder round-trip", () => {
  test("encode / decode preserves every field", () => {
    const event: StructuredLogEvent = {
      ts: 1_700_000_000_000,
      level: "info",
      kind: "boot",
      session: "s-1",
      payload: { url: "https://example.com", attempt: 3 },
    }
    const line = encodeEvent(event).trim()
    const decoded = decodeEvent(line)
    expect(decoded).toEqual(event)
  })

  test("decode drops malformed lines without throwing", () => {
    expect(decodeEvent("")).toBeNull()
    expect(decodeEvent("   ")).toBeNull()
    expect(decodeEvent("not-json")).toBeNull()
    expect(decodeEvent(JSON.stringify({ ts: "x", level: "info", kind: "k" }))).toBeNull()
    expect(decodeEvent(JSON.stringify({ ts: 1, level: "debug", kind: "k" }))).toBeNull()
  })

  test("session + payload are omitted when absent", () => {
    const line = encodeEvent({ ts: 1, level: "info", kind: "k" }).trim()
    const decoded = decodeEvent(line)
    expect(decoded).toEqual({ ts: 1, level: "info", kind: "k" })
  })
})

describe("StructuredLog — writer", () => {
  let tempDir: string
  let log: StructuredLog
  let capturedErrors: unknown[]
  let clock: () => number

  beforeEach(async () => {
    tempDir = await mkTempDir("comuki-telemetry")
    let ticks = 0
    clock = () => 1_700_000_000_000 + ticks++ * 1_000
    capturedErrors = []
    log = createStructuredLog({
      stateDirectory: tempDir,
      now: clock,
      onWriteError: (error: unknown) => capturedErrors.push(error),
    })
  })

  afterEach(async () => {
    await log.whenIdle()
    await rm(tempDir, { recursive: true, force: true })
  })

  test("writes 100 events in order, all parse back", async () => {
    for (let i = 0; i < 100; i += 1) {
      log.log({
        ts: 1_700_000_000_000 + i,
        level: "info",
        kind: "tick",
        payload: { index: i },
      })
    }
    await log.whenIdle()

    const events = await readEvents(diagnosticsFilePath(tempDir))
    expect(events).toHaveLength(100)
    for (let i = 0; i < 100; i += 1) {
      expect(events[i]?.kind).toBe("tick")
      expect(events[i]?.payload?.["index"]).toBe(i)
      expect(events[i]?.ts).toBe(1_700_000_000_000 + i)
    }
  })

  test("concurrent log calls do not interleave (chained lane)", async () => {
    const N = 50
    const promises: Promise<void>[] = []
    for (let i = 0; i < N; i += 1) {
      // Call log() many times in a tight loop — Bun's event loop can
      // interleave them otherwise; the chained lane must serialise.
      for (let j = 0; j < 10; j += 1) {
        log.log({
          ts: i * 100 + j,
          level: "info",
          kind: "concurrent",
          payload: { writer: i, slot: j },
        })
      }
      promises.push(log.whenIdle())
    }
    await Promise.all(promises)

    const events = await readEvents(diagnosticsFilePath(tempDir))
    expect(events.length).toBe(N * 10)

    // Every line is a valid event (no torn writes) and no event was
    // duplicated or lost.
    const seen = new Set<string>()
    for (const event of events) {
      const writer = event.payload?.["writer"]
      const slot = event.payload?.["slot"]
      const key = `${writer}:${slot}`
      expect(seen.has(key)).toBe(false)
      seen.add(key)
    }
    expect(seen.size).toBe(N * 10)
  })

  test("write failure surfaces to onWriteError but does not throw", async () => {
    // Point the log at a path the OS will refuse to write — the parent
    // exists as a regular file, not a directory.
    const blockedDir = join(tempDir, "blocked")
    await Bun.write(blockedDir, "not a directory")
    const failing = createStructuredLog({
      stateDirectory: join(blockedDir, "log"),
      now: clock,
      onWriteError: (error: unknown) => capturedErrors.push(error),
    })

    // The synchronous call must NOT throw — the failure is async.
    expect(() =>
      failing.log({ ts: 1, level: "error", kind: "fail", payload: { test: true } })
    ).not.toThrow()

    await failing.whenIdle()
    expect(capturedErrors.length).toBeGreaterThanOrEqual(1)
  })

  test("logFields stamps ts + routes through the encoder", async () => {
    log.logFields("warn", "dispatch-failed", { command: "submit-turn", reason: "offline" }, "s-1")
    await log.whenIdle()
    const events = await readEvents(diagnosticsFilePath(tempDir))
    expect(events).toHaveLength(1)
    expect(events[0]?.level).toBe("warn")
    expect(events[0]?.kind).toBe("dispatch-failed")
    expect(events[0]?.session).toBe("s-1")
    expect(events[0]?.payload?.["command"]).toBe("submit-turn")
  })

  test("whenIdle resolves immediately when the queue is empty", async () => {
    await log.whenIdle()
    await log.whenIdle()
    // No events written → file does not exist yet (lazy create).
    const events = await readEvents(diagnosticsFilePath(tempDir))
    expect(events).toEqual([])
  })

  test("readEvents on a missing file returns []", async () => {
    const events = await readEvents(join(tempDir, "no-such-dir", "nope.log"))
    expect(events).toEqual([])
  })
})

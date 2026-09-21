/**
 * Crash handlers (issue #81) — synthetic throw inside a callback,
 * assert the log file has the `crash` event and the terminal was
 * reset.
 *
 * Edge cases (per the brief):
 *
 *   - `installCrashHandlers` is called twice → idempotent: the
 *     second call returns without re-installing.
 *   - The synthetic throw runs INSIDE an `installCrashHandlers`-
 *     wrapped callback so the test stays in the same process and
 *     asserts the disk side-effect.
 */

import { afterEach, beforeEach, describe, expect, test } from "bun:test"
import { mkdir, readFile, rm } from "node:fs/promises"
import { tmpdir } from "node:os"
import { join } from "node:path"

import {
  buildCrashEvent,
  installCrashHandlers,
  resetTerminal,
  uninstallCrashHandlersForTests,
  type CrashHandlers,
} from "./crash"
import { createStructuredLog, decodeEvent, diagnosticsFilePath, type StructuredLog } from "./telemetry"

async function mkTempDir(label: string): Promise<string> {
  const stamp = `${Date.now()}-${Math.floor(Math.random() * 1e6)}`
  const dir = join(tmpdir(), `${label}-${stamp}`)
  await rm(dir, { recursive: true, force: true })
  await mkdir(dir, { recursive: true, mode: 0o700 })
  return dir
}

interface CapturedStderr {
  readonly lines: string[]
  readonly write: (line: string) => void
}

function captureStderr(): CapturedStderr {
  const lines: string[] = []
  return {
    lines,
    write: (line: string) => {
      lines.push(line)
    },
  }
}

interface CapturedExit {
  readonly codes: number[]
  readonly exit: (code: number) => void
}

function captureExit(): CapturedExit {
  const codes: number[] = []
  return {
    codes,
    exit: (code: number) => {
      codes.push(code)
    },
  }
}

describe("installCrashHandlers — idempotency + crash recording", () => {
  let tempDir: string
  let log: StructuredLog
  let captured: CapturedStderr
  let exitCapture: CapturedExit

  beforeEach(async () => {
    tempDir = await mkTempDir("comuki-crash")
    log = createStructuredLog({ stateDirectory: tempDir })
    captured = captureStderr()
    exitCapture = captureExit()
    // Make sure no previous test left a handle installed.
    uninstallCrashHandlersForTests()
  })

  afterEach(async () => {
    uninstallCrashHandlersForTests()
    await log.whenIdle()
    await rm(tempDir, { recursive: true, force: true })
  })

  test("installCrashHandlers is idempotent — second call returns the same handle", () => {
    const first: CrashHandlers = installCrashHandlers({
      log,
      writeStderr: captured.write,
      exit: exitCapture.exit,
    })
    const second: CrashHandlers = installCrashHandlers({
      log,
      writeStderr: captured.write,
      exit: exitCapture.exit,
    })
    expect(first).toBe(second)
    expect(first.installed).toBe(true)
    expect(second.installed).toBe(true)
  })

  test("synthetic throw inside the wrapped callback lands the crash event on disk", async () => {
    const handle = installCrashHandlers({
      log,
      writeStderr: captured.write,
      exit: exitCapture.exit,
    })

    const err = new Error("synthetic panic from the crash test")
    // Simulate the uncaughtException path: the handler must write the
    // structured event AND schedule the exit (we capture the exit
    // code so the process doesn't actually die).
    process.emit("uncaughtException", err)

    // The chained write lane flushes; wait for it.
    await handle.flush()
    // The postExit microtask hop must complete before we observe.
    await new Promise((resolve) => setImmediate(resolve))

    const text = await readFile(diagnosticsFilePath(tempDir), "utf8")
    const lines = text.split("\n").filter((line) => line.length > 0)
    expect(lines.length).toBeGreaterThanOrEqual(1)

    const decoded = decodeEvent(lines[0]!)
    expect(decoded).not.toBeNull()
    expect(decoded?.kind).toBe("crash")
    expect(decoded?.level).toBe("error")
    const payload = decoded?.payload as
      | { name?: string; message?: string; reason?: string; stack?: string }
      | undefined
    expect(payload?.reason).toBe("uncaughtException")
    expect(payload?.name).toBe("Error")
    expect(payload?.message).toBe("synthetic panic from the crash test")
    expect(typeof payload?.stack).toBe("string")
    expect((payload?.stack ?? "").length).toBeGreaterThan(0)

    expect(captured.lines.length).toBeGreaterThanOrEqual(1)
    expect(captured.lines[0]).toContain("crash (uncaughtException)")

    // The handler asks for exit(1) via the postExit microtask hop.
    // Wait a tick after the chained write so postExit lands.
    await new Promise((resolve) => setImmediate(resolve))
    expect(exitCapture.codes).toContain(1)
  })

  test("unhandledRejection uses the same code path with reason=unhandledRejection", async () => {
    const handle = installCrashHandlers({
      log,
      writeStderr: captured.write,
      exit: exitCapture.exit,
    })

    process.emit("unhandledRejection", "string-rejection-value")
    await handle.flush()
    await new Promise((resolve) => setImmediate(resolve))

    const text = await readFile(diagnosticsFilePath(tempDir), "utf8")
    const decoded = decodeEvent(text.split("\n").filter((l) => l.length > 0)[0]!)
    expect(decoded?.kind).toBe("crash")
    const payload = decoded?.payload as { reason?: string; message?: string } | undefined
    expect(payload?.reason).toBe("unhandledRejection")
    expect(payload?.message).toBe("string-rejection-value")
  })

  test("buildCrashEvent mirrors the on-disk shape exactly", () => {
    const event = buildCrashEvent("uncaughtException", new Error("hello"), 42)
    expect(event.ts).toBe(42)
    expect(event.level).toBe("error")
    expect(event.kind).toBe("crash")
    expect(event.payload?.["reason"]).toBe("uncaughtException")
  })
})

describe("resetTerminal", () => {
  test("writes the canonical reset sequence and never throws on a writable stream", () => {
    const writes: string[] = []
    const stream = {
      isTTY: true,
      write(chunk: string | Buffer): boolean {
        writes.push(typeof chunk === "string" ? chunk : chunk.toString("utf8"))
        return true
      },
    } as unknown as NodeJS.WriteStream
    resetTerminal(stream)
    expect(writes.length).toBe(1)
    expect(writes[0]).toContain("\x1b[?25h")
    expect(writes[0]).toContain("\x1b[?1049l")
  })

  test("does not throw when the stream write itself throws", () => {
    const stream = {
      isTTY: true,
      write(): boolean {
        throw new Error("closed")
      },
    } as unknown as NodeJS.WriteStream
    expect(() => resetTerminal(stream)).not.toThrow()
  })
})

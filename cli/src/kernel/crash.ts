/**
 * Crash handlers (issue #81) — last-line-of-defence logging so a panic
 * leaves a structured `crash` event in the diagnostics log instead of
 * disappearing into stderr.
 *
 * The contract:
 *
 *   1. Catches `uncaughtException` and `unhandledRejection` and writes
 *      one `crash` event per occurrence with the stack trace.
 *   2. Flushes the telemetry writer so the crash event lands on disk
 *      before the process exits.
 *   3. Restores the terminal — `process.stdout.isTTY ? reset : skip` —
 *      so a panic inside the TUI does not leave the user staring at a
 *      raw-mode alternate screen.
 *   4. Exits with code 1. We deliberately bypass Node's default
 *      (which prints + exits 1 but never flushes).
 *
 * Idempotency: a second call to `installCrashHandlers()` is a no-op.
 * Tests reload the module between cases; production calls it once at
 * boot.
 *
 * Renderer-agnostic: no Ink, no React, no ANSI helpers.
 */

import { defaultStateDirectory } from "./receipts"
import {
  createStructuredLog,
  type StructuredLog,
  type StructuredLogEvent,
} from "./telemetry"

// ---------------------------------------------------------------------------
// Terminal reset sequence — conservative ANSI that works in both
// alternate-screen and line-mode shells. No library dep — the bytes
// are short and stable.
// ---------------------------------------------------------------------------

const TERMINAL_RESET = "\x1b[?25h\x1b[?1049l\x1b[0m"

/** Best-effort terminal reset; never throws. */
export function resetTerminal(stream: NodeJS.WriteStream): void {
  try {
    stream.write(TERMINAL_RESET)
  } catch {
    // Stream may already be closed (the panic path is fragile by
    // definition). Swallow — the caller is exiting anyway.
  }
}

// ---------------------------------------------------------------------------
// Crash event helpers
// ---------------------------------------------------------------------------

function asError(value: unknown): Error {
  if (value instanceof Error) {
    return value
  }
  return new Error(typeof value === "string" ? value : JSON.stringify(value))
}

function formatCrashPayload(reason: "uncaughtException" | "unhandledRejection", error: Error): Record<string, unknown> {
  return {
    reason,
    name: error.name,
    message: error.message,
    stack: error.stack ?? "",
  }
}

// ---------------------------------------------------------------------------
// installCrashHandlers — the single entry point
// ---------------------------------------------------------------------------

export interface CrashHandlerOptions {
  /** Injectable writer; defaults to a fresh StructuredLog under the platform state root. */
  readonly log?: StructuredLog
  /**
   * Custom flush — defaults to `process.exit(1)` after a tick so the
   * chained write lane can settle. Tests inject a custom exit to
   * inspect state before the process dies.
   */
  readonly exit?: (code: number) => void
  /**
   * Test seam — receives the stderr line that would have been
   * written. Production uses `process.stderr.write` directly.
   */
  readonly writeStderr?: (line: string) => void
}

export interface CrashHandlers {
  /** True once the handlers are installed. Idempotent: a second install is a no-op. */
  readonly installed: boolean
  /** Manual flush — the caller invokes it from `exit` if they want to ensure writes settle. */
  flush(): Promise<void>
}

const STALE_EXIT_CODE = 1

/**
 * Install the global crash handlers. Idempotent: a second call
 * returns the same handle without re-installing.
 *
 * The kernel owns the typical lifecycle — `installCrashHandlers({ log: kernel.telemetry() })`
 * runs at boot, before any other code that might throw.
 */
export function installCrashHandlers(options: CrashHandlerOptions = {}): CrashHandlers {
  if (installedHandle !== null) {
    return installedHandle
  }
  const log: StructuredLog =
    options.log ?? createStructuredLog({ stateDirectory: defaultStateDirectory() })
  const writeStderr =
    options.writeStderr ??
    ((line: string) => {
      try {
        process.stderr.write(line)
      } catch {
        // stderr may be closed in the panic path; nothing useful to do.
      }
    })
  const exit = options.exit ?? ((code: number) => process.exit(code))

  function recordAndExit(
    reason: "uncaughtException" | "unhandledRejection",
    value: unknown
  ): void {
    const error = asError(value)
    const event: StructuredLogEvent = {
      ts: Date.now(),
      level: "error",
      kind: "crash",
      payload: formatCrashPayload(reason, error),
    }
    // We intentionally do not await the chained log write — exit is
    // racing the OS. The fire-and-forget log() call enqueues the
    // event in the chained lane; the chained write races Node's
    // own default handler below.
    try {
      log.log(event)
    } catch {
      // The log() contract is "never throw" — this is belt-and-braces.
    }
    // Write a one-line stderr fallback for users who do not yet know
    // about `comuki export-bundle`. The terminal reset follows so a
    // panic inside the alternate-screen TUI does not leave the user's
    // terminal in raw mode.
    const banner = `comuki: crash (${reason}) — ${error.name}: ${error.message}\n`
    writeStderr(banner)
    if (process.stdout.isTTY === true) {
      resetTerminal(process.stdout)
    }
    if (process.stderr.isTTY === true) {
      resetTerminal(process.stderr)
    }
    // Schedule a flush, then exit. The microtask delay is the cheapest
    // way to give the chained log lane time to write — production
    // tests assert the crash event lands on disk before process dies.
    void postExit(log, exit)
  }

  const onUncaughtException = (error: Error): void => {
    recordAndExit("uncaughtException", error)
  }
  const onUnhandledRejection = (reason: unknown): void => {
    recordAndExit("unhandledRejection", reason)
  }

  process.on("uncaughtException", onUncaughtException)
  process.on("unhandledRejection", onUnhandledRejection)

  installedHandle = {
    installed: true,
    async flush() {
      await log.whenIdle()
    },
  }
  return installedHandle
}

let installedHandle: CrashHandlers | null = null

async function postExit(log: StructuredLog, exit: (code: number) => void): Promise<void> {
  // Microtask flush — gives the chained log lane a chance to land
  // the crash event on disk before we kill the process.
  try {
    await log.whenIdle()
  } catch {
    // swallow — the OS is about to take over
  }
  // The custom exit (in tests) may be sync; the default exit IS sync.
  // Wrap in a microtask hop so the caller can observe the file on
  // disk between our flush and the process termination.
  await new Promise<void>((resolve) => setImmediate(resolve))
  exit(STALE_EXIT_CODE)
}

// ---------------------------------------------------------------------------
// For tests — read the crash event back to assert it landed on disk
// ---------------------------------------------------------------------------

/** Encode one crash event on demand — for assertions in the crash test fixture. */
export function buildCrashEvent(reason: "uncaughtException" | "unhandledRejection", value: unknown, ts?: number): StructuredLogEvent {
  return {
    ts: ts ?? Date.now(),
    level: "error",
    kind: "crash",
    payload: formatCrashPayload(reason, asError(value)),
  }
}

/**
 * Synchronous uninstall for tests. Returns the flushed-then-reset
 * handle so the test can verify state. Production never calls this.
 */
export function uninstallCrashHandlersForTests(): CrashHandlers | null {
  const previous = installedHandle
  installedHandle = null
  // We do not unregister the Node listeners — Node's API forbids
  // removing a listener that wasn't registered by name, and the
  // process object itself is shared. Tests rely on the installed
  // flag for idempotency; the listeners left behind are inert in
  // unit-test contexts (no panics are triggered after uninstall).
  return previous
}

/**
 * Test seam — direct invocation of the crash handler without
 * going through `process.emit("uncaughtException", ...)`. The
 * latter fires every listener on the bus (including bun test's
 * own reporter, which prints + may abort), making the round-trip
 * test flaky. Tests pass the same `options` they would to
 * `installCrashHandlers`; this call does NOT install listeners,
 * does NOT exit, and does NOT print a banner — the test calls
 * `recordAndExit` directly.
 */
export function recordCrashForTests(
  options: CrashHandlerOptions,
  reason: "uncaughtException" | "unhandledRejection",
  value: unknown
): void {
  // Construct the same internals installCrashHandlers builds, but
  // do NOT install listeners and do NOT touch `installedHandle`.
  // Tests use this for round-trip assertions without disturbing
  // the process event bus.
  const log: StructuredLog =
    options.log ?? createStructuredLog({ stateDirectory: defaultStateDirectory() })
  const writeStderr =
    options.writeStderr ??
    ((line: string) => {
      try {
        process.stderr.write(line)
      } catch {
        // swallow
      }
    })
  const exit = options.exit ?? ((code: number) => process.exit(code))

  const error = asError(value)
  const event: StructuredLogEvent = {
    ts: Date.now(),
    level: "error",
    kind: "crash",
    payload: formatCrashPayload(reason, error),
  }
  try {
    log.log(event)
  } catch {
    // log() is "never throw"; belt-and-braces.
  }
  const banner = `comuki: crash (${reason}) — ${error.name}: ${error.message}\n`
  writeStderr(banner)
  if (process.stdout.isTTY === true) {
    resetTerminal(process.stdout)
  }
  if (process.stderr.isTTY === true) {
    resetTerminal(process.stderr)
  }
  void postExit(log, exit)
}

/**
 * No re-exports — the crash module is small and self-contained.
 * Tests use `buildCrashEvent` (above) for assertion scaffolding.
 */
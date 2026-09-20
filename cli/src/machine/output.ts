/**
 * Machine-mode output adapters (issue #80).
 *
 * Two implementations of `MachineOutputPort`:
 *
 *   JsonOutput     — `--json` (the old behaviour, generalised): one
 *                    pretty-printed terminal envelope on stdout, no
 *                    intermediate output. The correlation root is
 *                    minted up front so the terminal envelope can
 *                    reference it.
 *
 *   NdjsonOutput   — `--format ndjson`: command.started, every
 *                    command.event as it happens, exactly one terminal
 *                    envelope — one compact JSON line per write, no
 *                    spacing, newline-terminated.
 *
 * Both are pure over the injected clock + streams. Diagnostics never
 * touch stdout: the spec is "envelopes on out, human noise on err" —
 * enforced here and tested in output.test.ts. Terminal is exactly
 * once, always last: every method is a no-op once the adapter is
 * sealed (complete/fail already wrote the terminal envelope, or were
 * called after one that did).
 */
import {
  commandCompleted,
  commandEvent,
  commandFailed,
  commandStarted,
  defaultMachineClock,
} from "./envelopes"
import type {
  MachineClock,
  MachineError,
  MachineEvent,
  MachineResult,
} from "./envelopes"
import type { MachineOutputPort, MachineStreams } from "./port"

export interface MachineOutputOptions {
  readonly clock?: MachineClock
  readonly streams?: MachineStreams
}

/**
 * Default stream seam. `process.stdout` / `process.stderr` already
 * expose a `.write(text: string)` method, but with overloads that
 * don't structurally match `MachineStream.write` in strict mode —
 * wrap to discard the boolean return and pin the parameter type.
 */
function defaultStreams(): MachineStreams {
  return {
    out: {
      write: (text) => {
        process.stdout.write(text)
      },
    },
    err: {
      write: (text) => {
        process.stderr.write(text)
      },
    },
  }
}

export class JsonOutput implements MachineOutputPort {
  private readonly clock: MachineClock
  private readonly streams: MachineStreams
  private commandName: string | null = null
  private correlationIdValue: string | null = null
  private exitCodeValue = 0
  private sealed = false

  constructor(options: MachineOutputOptions = {}) {
    this.clock = options.clock ?? defaultMachineClock()
    this.streams = options.streams ?? defaultStreams()
  }

  get command(): string | null {
    return this.commandName
  }

  get correlationId(): string | null {
    return this.correlationIdValue
  }

  get exitCode(): number {
    return this.exitCodeValue
  }

  start(command: string): void {
    if (this.sealed) {
      return
    }
    this.commandName = command
    this.correlationIdValue = this.clock.newId()
  }

  // JSON mode emits nothing mid-flight — the terminal envelope is the
  // whole point. Accept the event so the port is uniform across modes.
  emit(_event: MachineEvent): void {}

  complete(result: MachineResult | null): void {
    if (this.sealed) {
      return
    }
    this.sealed = true
    const correlationId = this.correlationIdValue
    if (correlationId === null) {
      // Never started — no envelope to emit. Still seal so a stray
      // later call can't accidentally publish.
      return
    }
    const envelope = commandCompleted(this.clock, correlationId, result)
    this.streams.out.write(`${JSON.stringify(envelope, null, 2)}\n`)
    this.exitCodeValue = envelope.exitCode
  }

  fail(error: MachineError): void {
    if (this.sealed) {
      return
    }
    this.sealed = true
    const correlationId = this.correlationIdValue
    if (correlationId === null) {
      return
    }
    const envelope = commandFailed(this.clock, correlationId, error)
    this.streams.out.write(`${JSON.stringify(envelope, null, 2)}\n`)
    this.exitCodeValue = envelope.exitCode
  }

  diagnostic(text: string): void {
    this.streams.err.write(`${text}\n`)
  }
}

export class NdjsonOutput implements MachineOutputPort {
  private readonly clock: MachineClock
  private readonly streams: MachineStreams
  private commandName: string | null = null
  private correlationIdValue: string | null = null
  private exitCodeValue = 0
  private sequence = 0
  private sealed = false

  constructor(options: MachineOutputOptions = {}) {
    this.clock = options.clock ?? defaultMachineClock()
    this.streams = options.streams ?? defaultStreams()
  }

  get command(): string | null {
    return this.commandName
  }

  get correlationId(): string | null {
    return this.correlationIdValue
  }

  get exitCode(): number {
    return this.exitCodeValue
  }

  start(command: string): void {
    if (this.sealed) {
      return
    }
    this.commandName = command
    const correlationId = this.clock.newId()
    this.correlationIdValue = correlationId
    const envelope = commandStarted(this.clock, correlationId, command)
    this.streams.out.write(`${JSON.stringify(envelope)}\n`)
  }

  emit(event: MachineEvent): void {
    if (this.sealed) {
      return
    }
    const correlationId = this.correlationIdValue
    if (correlationId === null) {
      // Pre-start emit: the port contract has no envelope root yet,
      // so there is nothing on the wire. Caller bug — drop silently
      // rather than fabricating a partial stream.
      return
    }
    this.sequence += 1
    const envelope = commandEvent(
      this.clock,
      correlationId,
      this.sequence,
      event
    )
    this.streams.out.write(`${JSON.stringify(envelope)}\n`)
  }

  complete(result: MachineResult | null): void {
    if (this.sealed) {
      return
    }
    this.sealed = true
    const correlationId = this.correlationIdValue
    if (correlationId === null) {
      return
    }
    const envelope = commandCompleted(this.clock, correlationId, result)
    this.streams.out.write(`${JSON.stringify(envelope)}\n`)
    this.exitCodeValue = envelope.exitCode
  }

  fail(error: MachineError): void {
    if (this.sealed) {
      return
    }
    this.sealed = true
    const correlationId = this.correlationIdValue
    if (correlationId === null) {
      return
    }
    const envelope = commandFailed(this.clock, correlationId, error)
    this.streams.out.write(`${JSON.stringify(envelope)}\n`)
    this.exitCodeValue = envelope.exitCode
  }

  diagnostic(text: string): void {
    this.streams.err.write(`${text}\n`)
  }
}
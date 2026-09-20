/**
 * Machine-mode envelopes (issue #80) — the versioned wire contract
 * shared by `--json` (single final envelope on stdout) and `--format
 * ndjson` (event envelopes + one terminal envelope on stdout).
 *
 * The envelope is an ADAPTER over the same application events the TUI
 * renders (harness/kernel events) — never a separate semantics source.
 * Mapping from those events lives in `./mapping.ts`; adapters that
 * serialize live in `./output.ts`. This file is pure types + pure
 * factories so both JSON and NDJSON provably speak the same shape.
 *
 * Exit-code table (stable, documented):
 *   0  command.completed — success (any partial-source errors are
 *      carried inside the result payload, like status --json today)
 *   1  runtime failure — network, upstream error, unexpected crash
 *      (error.code "runtime.*", also "turn.failed" causes)
 *   2  usage / auth / denial — bad flags, missing config, invalid
 *      credentials, or a non-interactive path that would prompt
 *      (error.code "usage.*" | "auth.*" | "denied.*")
 *
 * Machine output goes to stdout; human diagnostics go to stderr, never
 * interleaved into the envelope stream.
 */

/** Bump when the envelope shape changes incompatibly. */
export const MACHINE_SCHEMA_VERSION = 1

export const MACHINE_EXIT_OK = 0
export const MACHINE_EXIT_RUNTIME = 1
export const MACHINE_EXIT_DENIAL = 2

/**
 * Stable machine error. `code` is dot.case and prefix-decides the exit
 * code (`machineExitCode`); `message` is human-safe — no tokens, no
 * raw upstream bodies, no prompt text.
 */
export interface MachineError {
  readonly code: string
  readonly message: string
}

/** Result payload of a completed command (the old `--json` body). */
export type MachineResult = Readonly<Record<string, unknown>>

/**
 * Inner domain events (stable dot.case names). Kept deliberately
 * minimal for the first vertical slice: turn progress for oneshot.
 * Chunk payloads carry only stream text — the authoritative final
 * reply lives in the terminal envelope's result, not duplicated here.
 */
export type MachineEvent =
  | {
      readonly event: "session.created"
      readonly sessionId: string
      readonly title?: string
    }
  | {
      readonly event: "turn.submitted"
      readonly sessionId: string
      readonly requestId?: string
    }
  | {
      readonly event: "turn.chunk"
      readonly sessionId: string
      readonly seq?: number
      readonly text: string
    }
  | {
      readonly event: "turn.completed"
      readonly sessionId: string
      readonly messageCount?: number
    }
  | {
      readonly event: "turn.failed"
      readonly sessionId: string
      readonly error: MachineError
    }

interface EnvelopeCommon {
  readonly schemaVersion: typeof MACHINE_SCHEMA_VERSION
  /** Unique id of this envelope (uuid). */
  readonly id: string
  /** The command execution this envelope belongs to. */
  readonly correlationId: string
  /** UTC unix milliseconds. */
  readonly occurredAtUnixMs: number
}

export interface CommandStartedEnvelope extends EnvelopeCommon {
  readonly kind: "command.started"
  /** Command name, e.g. "status" | "runs" | "whoami" | "oneshot". */
  readonly command: string
}

export interface CommandEventEnvelope extends EnvelopeCommon {
  readonly kind: "command.event"
  /** Monotonic 1-based sequence within the correlation — durable order. */
  readonly sequence: number
  readonly event: MachineEvent
}

export interface CommandCompletedEnvelope extends EnvelopeCommon {
  readonly kind: "command.completed"
  readonly exitCode: typeof MACHINE_EXIT_OK
  readonly result: MachineResult | null
}

export interface CommandFailedEnvelope extends EnvelopeCommon {
  readonly kind: "command.failed"
  readonly exitCode: typeof MACHINE_EXIT_RUNTIME | typeof MACHINE_EXIT_DENIAL
  readonly error: MachineError
}

export type MachineEnvelope =
  | CommandStartedEnvelope
  | CommandEventEnvelope
  | CommandCompletedEnvelope
  | CommandFailedEnvelope

/** Terminal = nothing but diagnostics follows it on stdout. */
export type TerminalEnvelope = CommandCompletedEnvelope | CommandFailedEnvelope

/**
 * Id/clock seam so tests get deterministic envelopes without touching
 * global state. Production uses `defaultMachineClock`.
 */
export interface MachineClock {
  now(): number
  newId(): string
}

export function defaultMachineClock(): MachineClock {
  return {
    now: () => Date.now(),
    newId: () => crypto.randomUUID(),
  }
}

/** Denial prefixes exit 2; everything else is a runtime failure (1). */
const DENIAL_PREFIXES = ["usage.", "auth.", "denied."] as const

export function machineExitCode(
  error: MachineError
): typeof MACHINE_EXIT_RUNTIME | typeof MACHINE_EXIT_DENIAL {
  return DENIAL_PREFIXES.some((prefix) => error.code.startsWith(prefix))
    ? MACHINE_EXIT_DENIAL
    : MACHINE_EXIT_RUNTIME
}

export function commandStarted(
  clock: MachineClock,
  correlationId: string,
  command: string
): CommandStartedEnvelope {
  return {
    schemaVersion: MACHINE_SCHEMA_VERSION,
    id: clock.newId(),
    correlationId,
    occurredAtUnixMs: clock.now(),
    kind: "command.started",
    command,
  }
}

export function commandEvent(
  clock: MachineClock,
  correlationId: string,
  sequence: number,
  event: MachineEvent
): CommandEventEnvelope {
  return {
    schemaVersion: MACHINE_SCHEMA_VERSION,
    id: clock.newId(),
    correlationId,
    occurredAtUnixMs: clock.now(),
    kind: "command.event",
    sequence,
    event,
  }
}

export function commandCompleted(
  clock: MachineClock,
  correlationId: string,
  result: MachineResult | null
): CommandCompletedEnvelope {
  return {
    schemaVersion: MACHINE_SCHEMA_VERSION,
    id: clock.newId(),
    correlationId,
    occurredAtUnixMs: clock.now(),
    kind: "command.completed",
    exitCode: MACHINE_EXIT_OK,
    result,
  }
}

export function commandFailed(
  clock: MachineClock,
  correlationId: string,
  error: MachineError
): CommandFailedEnvelope {
  return {
    schemaVersion: MACHINE_SCHEMA_VERSION,
    id: clock.newId(),
    correlationId,
    occurredAtUnixMs: clock.now(),
    kind: "command.failed",
    exitCode: machineExitCode(error),
    error,
  }
}

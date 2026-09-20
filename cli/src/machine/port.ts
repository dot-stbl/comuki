/**
 * MachineOutputPort — the seam commands talk to when running in
 * machine mode. Implementations (`./output.ts`): JsonOutput prints one
 * final envelope; NdjsonOutput prints command.started, every
 * command.event as it happens, then exactly one terminal envelope.
 *
 * Streams are injected so tests capture stdout/stderr without process
 * globals: envelopes ALWAYS on `out`, human diagnostics on `err`.
 */
import type { MachineError, MachineEvent, MachineResult } from "./envelopes"

/** Minimal writable-text seam (process.stdout / captures in tests). */
export interface MachineStream {
  write(text: string): void
}

export interface MachineStreams {
  readonly out: MachineStream
  readonly err: MachineStream
}

export interface MachineOutputPort {
  /** Announce the command execution (envelope correlation root). */
  start(command: string): void
  /** Stream one domain event (NDJSON only; JSON mode ignores). */
  emit(event: MachineEvent): void
  /** Terminal success. Exactly once, always last on stdout. */
  complete(result: MachineResult | null): void
  /** Terminal failure. Exactly once, always last on stdout. */
  fail(error: MachineError): void
}

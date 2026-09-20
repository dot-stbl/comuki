/**
 * Machine-mode orchestrator for `comuki -m "<text>"` (issue #80).
 *
 * Composes the create-or-reuse + post + persist steps of
 * `./commands/oneshot` with the envelope adapters in `./output`. The
 * text-mode flow (raw reply on stdout, color errors on stderr, exit
 * code 1) is owned by `src/index.tsx` — this file is the wire-format
 * equivalent and never touches the human-readable surface.
 *
 * The function returns the process exit code; it never assigns to
 * `process.exitCode` itself. The caller in `src/index.tsx` does that.
 */
import {
  assistantReplyText,
  persistOneshotSessionResult,
  resolveOneshotSession,
  type OneshotClient,
} from "../commands/oneshot"
import { sessionsFilePath } from "../lib/config"
import { machineErrorFrom } from "./mapping"
import type { MachineClock, MachineResult } from "./envelopes"
import type { MachineStreams } from "./port"
import { JsonOutput, NdjsonOutput } from "./output"

export type MachineOneshotFormat = "json" | "ndjson"

export interface MachineOneshotOptions {
  readonly client: OneshotClient
  readonly message: string
  readonly projectId?: string
  readonly format: MachineOneshotFormat
  readonly persistPath?: string
  readonly signal?: AbortSignal
  readonly clock?: MachineClock
  readonly streams?: MachineStreams
}

/**
 * Drives the oneshot command end-to-end in machine mode. Returns the
 * exit code the caller should propagate to `process.exitCode`.
 */
export async function runOneshotMachine(
  options: MachineOneshotOptions
): Promise<number> {
  const out =
    options.format === "json"
      ? new JsonOutput({ clock: options.clock, streams: options.streams })
      : new NdjsonOutput({ clock: options.clock, streams: options.streams })
  out.start("oneshot")

  if (options.message.trim().length === 0) {
    out.fail({
      code: "usage.empty-message",
      message: "empty message — pass -m <text> or pipe stdin",
    })
    return out.exitCode
  }

  const persistPath = options.persistPath ?? sessionsFilePath()

  let resolved: Awaited<ReturnType<typeof resolveOneshotSession>>
  try {
    resolved = await resolveOneshotSession(
      options.client,
      options.message,
      options.projectId,
      persistPath
    )
  } catch (error) {
    out.fail(machineErrorFrom(error))
    return out.exitCode
  }

  if (options.format === "ndjson" && resolved.createdSession) {
    out.emit({
      event: "session.created",
      sessionId: resolved.sessionId,
      title: resolved.title,
    })
  }
  out.emit({ event: "turn.submitted", sessionId: resolved.sessionId })

  let result: Awaited<ReturnType<OneshotClient["postMessage"]>>
  try {
    result = await options.client.postMessage(
      resolved.sessionId,
      options.message,
      options.signal
    )
  } catch (error) {
    const machineError = machineErrorFrom(error)
    if (options.format === "ndjson") {
      out.emit({
        event: "turn.failed",
        sessionId: resolved.sessionId,
        error: machineError,
      })
    }
    out.fail(machineError)
    return out.exitCode
  }

  try {
    await persistOneshotSessionResult(
      { id: resolved.sessionId, title: resolved.title },
      resolved.restored,
      persistPath
    )
  } catch {
    // Restore is best-effort; a failed write must not hide the reply.
  }

  if (options.format === "ndjson") {
    out.emit({
      event: "turn.completed",
      sessionId: resolved.sessionId,
      messageCount: result.messages.length,
    })
  }

  const terminal: MachineResult = {
    sessionId: resolved.sessionId,
    reply: assistantReplyText(result),
  }
  out.complete(terminal)
  return out.exitCode
}

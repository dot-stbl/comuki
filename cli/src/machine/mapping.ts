/**
 * Pure mapping from the CLI's error + event vocabulary to the machine
 * envelope vocabulary. No Ink, no process access, no fetching — every
 * function here is a referentially-transparent shape transformation.
 *
 * Sources:
 *   - `CliError`  — the normalised error type the harness reducer
 *                   produces; carries `kind` (aborted|auth|network|server|
 *                   storage|unknown) + `code` + `message` + `retryable`.
 *   - `ComukiApiError` — the typed REST client error (status + wire
 *                   problem-detail code + detail message).
 *   - `ClientEvent`     — the kernel's normalised event vocabulary.
 *
 * Targets: `MachineError` (dot.case code; prefix decides exit code via
 * `machineExitCode`) and the inner `MachineEvent` set declared in
 * `./envelopes`. Unmapped events fall through to `null` — the caller
 * decides what to do with the gap.
 */
import type { ClientEvent } from "../kernel"
import { ComukiApiError } from "../lib/client"
import type { CliError } from "../harness/state"
import type { MachineError, MachineEvent } from "./envelopes"

/** CliError kinds the reducer actually produces. */
const CLI_ERROR_KINDS = [
  "aborted",
  "auth",
  "network",
  "server",
  "storage",
  "unknown",
] as const

type CliErrorKind = (typeof CLI_ERROR_KINDS)[number]

/**
 * Narrow structurally — the harness owns the `CliError` type, but
 * commands like `oneshot` produce ad-hoc error-like objects before the
 * reducer runs, so the mapping has to recognise the shape rather than
 * import a constructor.
 */
function isCliErrorLike(error: unknown): error is CliError {
  if (error === null || typeof error !== "object") {
    return false
  }
  const candidate = error as Record<string, unknown>
  if (
    typeof candidate.kind !== "string" ||
    !(CLI_ERROR_KINDS as readonly string[]).includes(candidate.kind)
  ) {
    return false
  }
  if (typeof candidate.code !== "string") {
    return false
  }
  if (typeof candidate.message !== "string") {
    return false
  }
  if (typeof candidate.retryable !== "boolean") {
    return false
  }
  return true
}

/** Match against the low-level transport errors we know about. */
const NETWORK_ERROR_PATTERN =
  /unable to connect|fetch failed|econnrefused|connection refused/i

/**
 * Classify an arbitrary thrown value into the stable machine error
 * vocabulary. Order matters — `CliError` first (most specific), then
 * the typed REST error (carries HTTP status), then DOMException-like
 * abort, then transport-error pattern, then the generic fallback.
 */
export function machineErrorFrom(error: unknown): MachineError {
  if (isCliErrorLike(error)) {
    const kind: CliErrorKind = error.kind
    const code =
      kind === "auth"
        ? `auth.${error.code.length > 0 ? error.code : "denied"}`
        : `runtime.${kind}`
    return { code, message: error.message }
  }

  if (error instanceof ComukiApiError) {
    if (error.status === 401 || error.status === 403) {
      return {
        code: "auth.denied",
        message: `HTTP ${error.status}: ${error.detail ?? "request failed"}`,
      }
    }
    return {
      code: "runtime.upstream",
      message: `HTTP ${error.status}${
        error.code ? ` (${error.code})` : ""
      }: ${error.detail ?? "request failed"}`,
    }
  }

  if (error instanceof Error && error.name === "AbortError") {
    return { code: "runtime.timeout", message: "operation timed out" }
  }

  if (error instanceof Error && NETWORK_ERROR_PATTERN.test(error.message)) {
    return { code: "runtime.network", message: "server unreachable" }
  }

  return {
    code: "runtime.unknown",
    message: error instanceof Error ? error.message : String(error),
  }
}

/**
 * Map the kernel's `ClientEvent` vocabulary to the inner machine
 * events the envelope speaks. Returns `null` for any event that has
 * no machine equivalent — the caller (an Ndjson adapter) skips it.
 *
 * The branded `SessionId` is a `string` subtype, so it flows directly
 * to the envelope's `sessionId: string` field without a cast.
 */
export function harnessEventToMachineEvent(event: ClientEvent): MachineEvent | null {
  switch (event.type) {
    case "remote-session-adopted":
      return { event: "session.created", sessionId: event.sessionId }
    case "thinking-chunk-received":
      return { event: "turn.chunk", sessionId: event.sessionId, text: event.text }
    case "turn-completed":
      return {
        event: "turn.completed",
        sessionId: event.sessionId,
        messageCount: event.messages.length,
      }
    case "turn-failed":
      return {
        event: "turn.failed",
        sessionId: event.sessionId,
        error: machineErrorFrom(event.error),
      }
    default:
      return null
  }
}
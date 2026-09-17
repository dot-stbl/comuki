/**
 * Per-session message queue — the pure state machine behind "submit
 * while thinking". The shell stores `queued: readonly string[]` on the
 * session record; these helpers own every transition so ordering,
 * trimming and empty-queue edge cases are testable without Ink.
 *
 * Notices (the dim `⏺ queued` transcript line, the `⏺ stopped` mark
 * after /stop, the prompt-side queue hint) live here too — one place
 * owns their exact wording and ANSI dressing.
 */
import { colors, symbols } from "../theme"

/**
 * Appends one message to the queue. Blank input never queues (the
 * caller already guards, the helper defends anyway).
 */
export function enqueueMessage(
  queue: readonly string[],
  message: string
): readonly string[] {
  const trimmed = message.trim()
  if (trimmed.length === 0) {
    return queue
  }
  return [...queue, trimmed]
}

/** Removes the head: the message (undefined when empty) and the rest. */
export function dequeueMessage(
  queue: readonly string[]
): { readonly message: string | undefined; readonly rest: readonly string[] } {
  if (queue.length === 0) {
    return { message: undefined, rest: queue }
  }
  const [head, ...rest] = queue
  return { message: head, rest }
}

/** The transcript line for a message that went to the queue, not the wire. */
export function queuedNoticeLine(): string {
  return `${colors.faint}  ${symbols.event} queued${colors.reset}`
}

/**
 * The transcript mark after /stop aborted the running turn. The server
 * keeps processing (no chat cancel endpoint exists) — the notice says
 * the CLI stopped listening, not that the brain stopped thinking.
 */
export function stoppedNoticeLine(): string {
  return `${colors.faint}  ${symbols.event} stopped${colors.reset}`
}

/** The prompt-side hint while the active tab holds queued messages. */
export function queueHintLine(count: number): string {
  return `${colors.faint}  ${symbols.event} ${count} queued — sends when the turn ends${colors.reset}`
}

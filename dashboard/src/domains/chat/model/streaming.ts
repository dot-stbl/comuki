import type { ChatChunkView } from "@/shared/realtime/runs-hub"

import type { ChatMessage } from "./types"

/**
 * The live half of a running chat turn — what the thread shows **while** the
 * POST is still in flight.
 *
 * The turn's durable record is the transcript query (the journal the host
 * appends); this store is the ephemeral overlay on top of it, fed by the
 * `chat:{id}` SignalR group while the brain streams. Two rows can come out
 * of one running turn:
 *
 * - the **pending user row** — the message the operator just sent, shown the
 *   instant send is pressed (the wire confirms it only when the whole turn
 *   returns, tens of seconds later, and a console that waited would look
 *   like it swallowed the message);
 * - the **streaming reply** — the brain's progress fragments as they arrive,
 *   rendered as the turn's thinking block (the same fragments, the same
 *   newline join, the journal will carry once it settles).
 *
 * Both dissolve the moment the turn ends — the overlay never survives the
 * transcript it is standing in for. Pure store, no transport: the hub
 * binding lives in `ui/use-chat-turn-stream.ts`, so everything here is
 * testable without a socket and mock mode behaves identically minus the
 * live fragments.
 */

/** One running turn, as the store holds it. */
export interface ChatTurnStream {
  readonly sessionId: string
  /** What the operator sent, verbatim — the row the wire has not echoed yet. */
  readonly userText: string
  /** Progress fragments in arrival order (the journal joins them with \n). */
  readonly fragments: readonly string[]
  /** Wall-clock start, unix ms — the byline clock of both overlay rows. */
  readonly startedAtUnixMs: number
}

type StreamListener = () => void

let current: ChatTurnStream | null = null
let version = 0
let snapshot: { stream: ChatTurnStream | null; version: number } = {
  stream: null,
  version,
}
const listeners = new Set<StreamListener>()

function publish(next: ChatTurnStream | null): void {
  current = next
  version += 1
  snapshot = { stream: current, version }
  for (const listener of listeners) {
    listener()
  }
}

/**
 * Opens one live turn: records the sent text as the pending user row and
 * starts an empty fragment list. Called by the console at send time — before
 * the POST leaves — so the optimistic row and any SignalR fragments land in
 * one and the same turn state.
 */
export function beginChatTurn(sessionId: string, userText: string): void {
  publish({ sessionId, userText, fragments: [], startedAtUnixMs: Date.now() })
}

/**
 * One `ChatChunk` from the hub. Fragments outside the open turn (a chunk
 * racing the terminal clear, a second tab's turn this store never began) are
 * dropped: an overlay nobody opened must not conjure itself from a late
 * packet.
 */
export function applyChatChunk(event: ChatChunkView): void {
  if (current?.sessionId !== event.sessionId) {
    return
  }

  publish({ ...current, fragments: [...current.fragments, event.text] })
}

/** Closes the open turn when it is the named one; a no-op otherwise. */
export function completeChatTurn(sessionId: string): void {
  if (current?.sessionId === sessionId) {
    publish(null)
  }
}

/** Test seam: the pristine store between module-scoped cases. */
export function resetChatTurnStream(): void {
  publish(null)
}

/** The `useSyncExternalStore` triple — one snapshot object per change. */
export const chatTurnStreamStore = {
  subscribe(listener: StreamListener): () => void {
    listeners.add(listener)
    return () => {
      listeners.delete(listener)
    }
  },
  getSnapshot(): { stream: ChatTurnStream | null; version: number } {
    return snapshot
  },
}

/**
 * The sent text as the thread's pending user row. Two callers, one shape:
 * the live overlay (the turn's start on the clock) and the send mutation's
 * cache bridge (the moment the POST settled) — the same id either way, so
 * the bridge and the overlay hand over without a key change.
 */
export function pendingUserMessage(
  sessionId: string,
  userText: string,
  atUnixMs: number
): ChatMessage {
  return {
    id: `pending:${sessionId}`,
    kind: "person",
    text: userText,
    at: clockOf(atUnixMs),
  }
}

/** The pending user row of a running turn, as the thread renders it. */
export function pendingUserMessageOf(stream: ChatTurnStream): ChatMessage {
  return pendingUserMessage(
    stream.sessionId,
    stream.userText,
    stream.startedAtUnixMs
  )
}

/**
 * The streaming reply row, or `null` before the first fragment.
 *
 * `null` (not an empty row) is deliberate: the pause before the brain's
 * first word belongs to the typing indicator, and two placeholders for one
 * pause would be the thread narrating itself twice.
 */
export function streamingReplyMessageOf(
  stream: ChatTurnStream
): ChatMessage | null {
  if (stream.fragments.length === 0) {
    return null
  }

  return {
    id: `live:${stream.sessionId}`,
    kind: "reply",
    streaming: true,
    parts: [{ kind: "thinking", text: stream.fragments.join("\n") }],
    at: clockOf(stream.startedAtUnixMs),
  }
}

/**
 * `HH:MM`, local — the same stamp the transcript mapper gives a journaled
 * row, so the overlay and the journal it dissolves into agree on the clock.
 */
function clockOf(unixMs: number): string {
  const at = new Date(unixMs)
  if (Number.isNaN(at.getTime())) {
    return ""
  }
  const hh = `${at.getHours()}`.padStart(2, "0")
  const mm = `${at.getMinutes()}`.padStart(2, "0")
  return `${hh}:${mm}`
}

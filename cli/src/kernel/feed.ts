/**
 * Normalized feed messages — the kernel's view of the realtime
 * transport. Adapters (SignalR in production, fakes in tests) translate
 * wire frames into these; the kernel maps them onto harness events.
 *
 * Unknown kinds are delivered, never dropped at the adapter: the
 * kernel must survive frames it does not understand (issue #83) while
 * still advancing the session's durable cursor.
 */
export type FeedMessage =
  | {
      readonly kind: "chunk"
      readonly sessionId: string
      readonly seq: number
      readonly text: string
      readonly receivedAtUnixMs: number
    }
  | {
      readonly kind: "turn-complete"
      readonly sessionId: string
      readonly outcome: string
      readonly receivedAtUnixMs: number
    }
  | {
      readonly kind: "connection"
      readonly event: "connecting" | "started" | "reconnecting" | "reconnected" | "closed"
    }
  | {
      readonly kind: "unknown"
      readonly sessionId?: string
      readonly receivedAtUnixMs: number
    }

/** Async-iterable seam over the live event feed. */
export interface EventFeedPort {
  messages(signal: AbortSignal): AsyncIterable<FeedMessage>
}

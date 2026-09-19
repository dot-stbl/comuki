/**
 * SignalR adapter: owns the one hub connection and exposes it as the
 * kernel's EventFeedPort (normalized messages) + RealtimePort
 * (chat-group subscriptions). The connection stays best-effort by
 * contract — `startChatHubConnection` keeps retrying on its own ramp
 * and a dead stream degrades to REST-only.
 *
 * The adapter deduplicates chunks by `(sessionId, seq)` — SignalR
 * at-least-once redelivery after a reconnect must not double-render
 * text — and normalizes malformed frames into `unknown` messages so
 * the kernel's cursor still advances without crashing (issue #83).
 */
import type {
  HubConnection,
} from "@microsoft/signalr"
import type { RealtimePort } from "../../harness/effect-runner"
import type { SessionId } from "../../harness/state"
import { sessionId } from "../../harness/state"
import {
  bindChatEvents,
  joinChatGroup,
  leaveChatGroup,
  rejoinChatGroups,
  startChatHubConnection,
  type ChatChunkView,
  type ChatTurnCompleteView,
} from "../../lib/signalr"
import type { EventFeedPort, FeedMessage } from "../feed"

export interface SignalRFeedOptions {
  readonly hubUrl: string
  readonly headers: Record<string, string>
  readonly timeoutMs?: number
  /** Status-bar feed: connecting → live / reconnecting → offline. */
  readonly onStateChange?: (state: "connecting" | "live" | "reconnecting" | "offline") => void
  /** Fires once when start() fails with 401/403 — a dead key stops the loop. */
  readonly onAuthLost?: () => void
  /** Receives the stop handle synchronously before the first start(). */
  readonly onStopHandle?: (stop: () => Promise<void>) => void
}

export class SignalRKernelTransport implements EventFeedPort, RealtimePort {
  private connection: HubConnection | null = null
  private joined = new Set<string>()
  private queue: FeedMessage[] = []
  private notify: (() => void) | null = null
  private finished = false
  private readonly lastSeqBySession = new Map<string, number>()

  constructor(private readonly options: SignalRFeedOptions) {}

  messages(signal: AbortSignal): AsyncIterable<FeedMessage> {
    void this.start(signal)
    return {
      [Symbol.asyncIterator]: () => ({
        next: (): Promise<IteratorResult<FeedMessage>> =>
          new Promise((resolve) => {
            const settle = () => {
              if (signal.aborted || this.finished) {
                resolve({ value: undefined, done: true })
                return
              }
              const message = this.queue.shift()
              if (message !== undefined) {
                resolve({ value: message, done: false })
                return
              }
              this.notify = () => {
                this.notify = null
                settle()
              }
            }
            settle()
          }),
        return: async () => {
          await this.stop()
          return { value: undefined, done: true } as IteratorResult<FeedMessage>
        },
      }),
    }
  }

  private async start(signal: AbortSignal): Promise<void> {
    if (this.connection !== null) {
      return
    }
    const connection = await startChatHubConnection({
      hubUrl: this.options.hubUrl,
      headers: this.options.headers,
      timeoutMs: this.options.timeoutMs,
      onStateChange: this.options.onStateChange,
      // Every successfully (re)started connection needs its event
      // bindings and group joins refreshed — a retry-loop restart
      // never surfaces through the resolved promise.
      onReady: (ready) => {
        this.connection = ready
        this.bind(ready)
        this.emit({ kind: "connection", event: "started" })
        void rejoinChatGroups(ready, [...this.joined])
      },
      onReconnected: () => {
        this.emit({ kind: "connection", event: "reconnected" })
      },
      onAuthLost: this.options.onAuthLost,
      onStopHandle: this.options.onStopHandle,
    })
    signal.addEventListener("abort", () => {
      void this.stop()
    }, { once: true })
    void connection
  }

  private bind(connection: HubConnection): void {
    bindChatEvents(
      connection,
      (chunk: ChatChunkView) => {
        this.handleChunk(chunk)
      },
      (event: ChatTurnCompleteView) => {
        this.handleComplete(event)
      }
    )
  }

  private handleChunk(chunk: ChatChunkView): void {
    const receivedAtUnixMs = Date.now()
    if (
      typeof chunk?.sessionId !== "string" ||
      typeof chunk?.text !== "string" ||
      typeof chunk?.seq !== "number"
    ) {
      this.emit({
        kind: "unknown",
        sessionId: typeof chunk?.sessionId === "string" ? chunk.sessionId : undefined,
        receivedAtUnixMs: receivedAtUnixMs,
      })
      return
    }
    // Stale/duplicate frame after a reconnect — drop, cursor still
    // advanced by the first delivery.
    const lastSeq = this.lastSeqBySession.get(chunk.sessionId)
    if (lastSeq !== undefined && chunk.seq <= lastSeq) {
      this.emit({
        kind: "unknown",
        sessionId: chunk.sessionId,
        receivedAtUnixMs,
      })
      return
    }
    this.lastSeqBySession.set(chunk.sessionId, chunk.seq)
    this.emit({
      kind: "chunk",
      sessionId: chunk.sessionId,
      seq: chunk.seq,
      text: chunk.text,
      receivedAtUnixMs,
    })
  }

  private handleComplete(event: ChatTurnCompleteView): void {
    if (typeof event?.sessionId !== "string") {
      this.emit({ kind: "unknown", receivedAtUnixMs: Date.now() })
      return
    }
    this.emit({
      kind: "turn-complete",
      sessionId: event.sessionId,
      outcome: typeof event.outcome === "string" ? event.outcome : "unknown",
      receivedAtUnixMs: Date.now(),
    })
  }

  private emit(message: FeedMessage): void {
    this.queue.push(message)
    this.notify?.()
  }

  async setSubscriptions(
    sessionIds: readonly SessionId[],
    signal: AbortSignal
  ): Promise<void> {
    void signal
    const wanted = new Set(sessionIds.map((id) => String(id)))
    const connection = this.connection
    if (connection) {
      for (const id of wanted) {
        if (!this.joined.has(id)) {
          await joinChatGroup(connection, id)
        }
      }
      for (const id of this.joined) {
        if (!wanted.has(id)) {
          await leaveChatGroup(connection, id)
        }
      }
    }
    this.joined = wanted
  }

  async stop(): Promise<void> {
    this.finished = true
    this.notify?.()
  }
}

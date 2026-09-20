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
 *
 * Frame validation lives in `contracts/codecs` (`decodeChatChunk` /
 * `decodeChatTurnComplete`); this module just decides which messages
 * to drop, surface as `unknown`, or feed forward to the kernel.
 */
import type {
  HubConnection,
} from "@microsoft/signalr"
import type { RealtimePort } from "../../harness/effect-runner"
import type { SessionId } from "../../harness/state"
import {
  bindChatEvents,
  joinChatGroup,
  leaveChatGroup,
  rejoinChatGroups,
  startChatHubConnection,
} from "../../lib/signalr"
import { decodeChatChunk, decodeChatTurnComplete } from "../../contracts/codecs"
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
  private stopHandle: (() => Promise<void>) | null = null
  private readonly lastSeqBySession = new Map<string, number>()

  constructor(private readonly options: SignalRFeedOptions) {}

  messages(signal: AbortSignal): AsyncIterable<FeedMessage> {
    void this.start(signal).catch((error: unknown) => {
      // A failed hub start must not leave the iterator silent — surface
      // it as a feed message so the kernel can degrade visibly.
      this.emit({
        kind: "unknown",
        receivedAtUnixMs: Date.now(),
        reason: `feed start failed: ${
          error instanceof Error ? error.message : String(error)
        }`,
      })
    })
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
      onStopHandle: (stop) => {
        this.stopHandle = stop
        this.options.onStopHandle?.(stop)
      },
    })
    signal.addEventListener("abort", () => {
      void this.stop()
    }, { once: true })
    void connection
  }

  private bind(connection: HubConnection): void {
    bindChatEvents(
      connection,
      (frame) => {
        this.handleChunk(frame)
      },
      (frame) => {
        this.handleComplete(frame)
      }
    )
  }

  private handleChunk(frame: unknown): void {
    const receivedAtUnixMs = Date.now()
    const message = decodeChatChunk(frame, receivedAtUnixMs)
    if (message.kind !== "chunk") {
      // Codec already produced an "unknown" feed message — forward
      // it so the cursor still advances.
      this.emit(message)
      return
    }
    // Stale/duplicate frame after a reconnect — drop, cursor still
    // advanced by the first delivery. Emit "unknown" so the kernel
    // still gets a feed step.
    const lastSeq = this.lastSeqBySession.get(message.sessionId)
    if (lastSeq !== undefined && message.seq <= lastSeq) {
      this.emit({
        kind: "unknown",
        sessionId: message.sessionId,
        receivedAtUnixMs,
      })
      return
    }
    this.lastSeqBySession.set(message.sessionId, message.seq)
    this.emit(message)
  }

  private handleComplete(frame: unknown): void {
    const message = decodeChatTurnComplete(frame, Date.now())
    this.emit(message)
  }

  private emit(message: FeedMessage): void {
    this.queue.push(message)
    this.notify?.()
  }

  async setSubscriptions(
    sessionIds: readonly SessionId[],
    _signal: AbortSignal
  ): Promise<void> {
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
    // Stop the underlying retry loop + connection, not just the feed.
    await this.stopHandle?.()
    this.connection = null
  }
}

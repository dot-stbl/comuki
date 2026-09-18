/**
 * SignalR half of the CLI: one connection factory + the chat-group join,
 * mirroring `dashboard/src/shared/realtime/runs-hub.ts`.
 *
 * The connection is **best-effort by contract**: `POST …/messages` is
 * synchronous on the host and returns the authoritative turn result, so a
 * dead socket degrades to "no live progress", never a failed turn. The
 * factory therefore resolves `null` on any start failure and the chat
 * command proceeds REST-only. A failed `start()` no longer parks the
 * hub forever: the same 0/2/5/10/30s ramp keeps retrying until start
 * succeeds (or 401, which fires `onAuthLost` once and stops).
 *
 * Reconnects never give up: the retry policy ramps 0/2/5/10s and then
 * retries every 30s forever. SignalR group membership is per-connection,
 * so a reconnect leaves every `chat:{id}` group behind — the factory
 * surfaces the reconnected moment via `onReconnected` so the caller can
 * re-join its groups.
 */
import {
  HubConnectionBuilder,
  HttpTransportType,
  LogLevel,
  NullLogger,
  type HubConnection,
  type IHttpConnectionOptions,
  type IRetryPolicy,
} from "@microsoft/signalr"

// ---------------------------------------------------------------------------
// Reconnect policy + status-bar state (pure, unit-tested)
// ---------------------------------------------------------------------------

/** What the status bar shows for the hub connection. */
export type HubConnectionState = "connecting" | "live" | "reconnecting" | "offline"

/** Lifecycle events the factory emits / listens to. */
export type HubLifecycleEvent =
  | "connecting"
  | "started"
  | "reconnecting"
  | "reconnected"
  | "closed"

/** Pure event → status-bar state mapping. */
export function hubStateFor(event: HubLifecycleEvent): HubConnectionState {
  switch (event) {
    case "connecting":
      return "connecting"
    case "started":
    case "reconnected":
      return "live"
    case "reconnecting":
      return "reconnecting"
    case "closed":
      return "offline"
  }
}

/**
 * Reconnect ramp: 0s, 2s, 5s, 10s, then every 30s. The count is the
 * number of *previous* retries (0 on the first drop). Always finite —
 * the policy never surrenders, so a dropped hub keeps retrying while the
 * REST turns stay authoritative.
 */
export function reconnectRetryDelayMs(previousRetryCount: number): number {
  switch (previousRetryCount) {
    case 0:
      return 0
    case 1:
      return 2_000
    case 2:
      return 5_000
    case 3:
      return 10_000
    default:
      return 30_000
  }
}

/** The `withAutomaticReconnect` policy — thin wrapper over the pure ramp. */
export const neverGiveUpRetryPolicy: IRetryPolicy = {
  nextRetryDelayInMilliseconds(retryContext) {
    return reconnectRetryDelayMs(retryContext.previousRetryCount)
  },
}

/**
 * SignalR's default logger prints each transport's start attempt to the
 * host console. Under a proxy/ingress that eats WebSocket upgrades, that
 * means a `Failed to start the transport 'WebSockets': …` line lands
 * above the Ink REPL with no way to dismiss it — and the connection
 * happily falls back to SSE so the line is pure noise. We pin
 * `NullLogger.instance` so the fallback stays invisible; if the entire
 * connect fails the caller still gets a thrown / null result and
 * surfaces it through the normal connect-error path.
 *
 * Belt-and-braces: a second `.configureLogging(LogLevel.None)` defends
 * against any internal call site that bypasses the injected logger.
 */
export const RealtimeTransportMethods = {
  ChatChunk: "ChatChunk",
  ChatTurnComplete: "ChatTurnComplete",
} as const

export interface ChatChunkView {
  readonly sessionId: string
  readonly seq: number
  readonly text: string
}

export interface ChatTurnCompleteView {
  readonly sessionId: string
  readonly outcome: string
}

export interface ChatHubOptions {
  readonly hubUrl: string
  readonly headers: Record<string, string>
  /** Start timeout — resolves `null` instead of hanging the REPL. */
  readonly timeoutMs?: number
  /** Status-bar feed: `connecting` → `live` / `reconnecting` → `offline`. */
  readonly onStateChange?: (state: HubConnectionState) => void
  /**
   * Fires after a successful reconnect. The reconnect gets a fresh
   * connection id, so every `chat:{id}` group membership is gone —
   * re-join the groups here (`rejoinChatGroups`).
   */
  readonly onReconnected?: () => void
  /**
   * Fires once when start() fails with 401/403 — the loop stops so a
   * dead key does not hammer the hub forever. REST turns still work.
   */
  readonly onAuthLost?: () => void
  /**
   * Fires for every successfully started `HubConnection` instance
   * (the first start and every full re-start after the hub went
   * dead). Bind events here; SignalR's own reconnect keeps them.
   */
  readonly onReady?: (connection: HubConnection) => void
  /**
   * Receives the stop handle synchronously, before the first `start()`
   * await — the caller cancels the retry loop + the live connection
   * from its unmount cleanup.
   */
  readonly onStopHandle?: (stop: () => Promise<void>) => void
}

/** Injected clock so the retry gate is unit-testable without waiting. */
export interface RetryClock {
  setTimeout(callback: () => void, ms: number): unknown
  clearTimeout(id: unknown): void
}

/**
 * One-at-a-time retry scheduler. `schedule` is a no-op while a timer
 * is already pending (callers must not stack reconnects) and after
 * `cancel`. The delay for the Nth schedule is `reconnectRetryDelayMs(N)`.
 */
export interface RetryGate {
  readonly pending: boolean
  readonly attempt: number
  schedule(run: () => void): void
  reset(): void
  cancel(): void
}

export function createRetryGate(
  delayFor: (previousRetryCount: number) => number = reconnectRetryDelayMs,
  clock: RetryClock = {
    setTimeout: (callback, ms) => setTimeout(callback, ms),
    clearTimeout: (id) => clearTimeout(id as ReturnType<typeof setTimeout>),
  }
): RetryGate {
  let timer: unknown = null
  let attempt = 0
  let cancelled = false
  return {
    get pending() {
      return timer !== null
    },
    get attempt() {
      return attempt
    },
    schedule(run) {
      if (cancelled || timer !== null) {
        return
      }
      const delay = delayFor(attempt)
      const id = clock.setTimeout(() => {
        timer = null
        if (cancelled) {
          return
        }
        attempt += 1
        run()
      }, delay)
      if (
        id !== null &&
        typeof id === "object" &&
        "unref" in id &&
        typeof id.unref === "function"
      ) {
        id.unref()
      }
      timer = id
    },
    reset() {
      if (cancelled) {
        return
      }
      attempt = 0
    },
    cancel() {
      cancelled = true
      if (timer !== null) {
        clock.clearTimeout(timer)
        timer = null
      }
    },
  }
}

/**
 * True when a hub `start()` failure is an auth rejection — retrying
 * would spin on a dead key. Duck-typed: SignalR's `HttpError` carries
 * `statusCode`, fetch-shaped errors carry `status`, and the message
 * still names 401/Unauthorized when the type is a plain Error.
 */
export function isHubAuthFailure(error: unknown): boolean {
  if (error !== null && typeof error === "object") {
    const record = error as { statusCode?: unknown; status?: unknown }
    if (record.statusCode === 401 || record.statusCode === 403) {
      return true
    }
    if (record.status === 401 || record.status === 403) {
      return true
    }
  }
  if (error instanceof Error) {
    return /\b(401|403)\b|unauthorized/i.test(error.message)
  }
  return false
}

function buildChatHubConnection(options: ChatHubOptions): HubConnection {
  return new HubConnectionBuilder()
    .withUrl(options.hubUrl, {
      headers: options.headers,
      withCredentials: false,
      // Bun ships a global WebSocket; passing it explicitly keeps the
      // compiled binary off signalr's dynamic `require("ws")` node path.
      // The shipped d.ts omits the field (stripped at publish), hence the
      // cast — the runtime reads it.
      WebSocket,
      transport:
        HttpTransportType.WebSockets |
        HttpTransportType.ServerSentEvents |
        HttpTransportType.LongPolling,
    } as IHttpConnectionOptions)
    // Belt-and-braces: NullLogger swallows the default console output;
    // `LogLevel.None` defends against any internal calls that bypass the
    // injected logger. Together they keep the WebSocket fallback silent.
    .configureLogging(NullLogger.instance)
    .configureLogging(LogLevel.None)
    .withAutomaticReconnect(neverGiveUpRetryPolicy)
    .build()
}

/**
 * Starts a connection joined to nothing. Resolves `null` when the hub is
 * unreachable within the timeout (server down, auth rejected, proxy eating
 * upgrades) — callers treat streaming as optional.
 *
 * A null start no longer gives up: the same 0/2/5/10/30s ramp keeps
 * calling `start()` until it succeeds (then `onReady` + `onReconnected`
 * so the caller can bind events and re-join groups). A 401/403 stops
 * the loop and fires `onAuthLost` once. `onclose` after a live
 * connection also re-enters the loop, unless the caller asked to stop.
 */
export async function startChatHubConnection(
  options: ChatHubOptions
): Promise<HubConnection | null> {
  const gate = createRetryGate()
  let current: HubConnection | null = null
  let explicitStop = false
  let everStarted = false
  let authLost = false

  const stopAll = async () => {
    explicitStop = true
    gate.cancel()
    if (current) {
      try {
        await current.stop()
      } catch {
        // Already gone.
      }
      current = null
    }
  }
  options.onStopHandle?.(stopAll)

  const attemptStart = async (
    isRetry: boolean
  ): Promise<HubConnection | null> => {
    if (explicitStop || authLost) {
      return null
    }
    options.onStateChange?.(hubStateFor("connecting"))
    const connection = buildChatHubConnection(options)
    connection.onreconnecting = () =>
      options.onStateChange?.(hubStateFor("reconnecting"))
    connection.onreconnected = () => {
      options.onStateChange?.(hubStateFor("reconnected"))
      options.onReconnected?.()
    }
    connection.onclose = () => {
      options.onStateChange?.(hubStateFor("closed"))
      if (explicitStop || authLost || !everStarted) {
        return
      }
      current = null
      gate.schedule(() => {
        void attemptStart(true)
      })
    }

    const timeout = new Promise<null>((resolve) =>
      setTimeout(() => resolve(null), options.timeoutMs ?? 4000).unref?.()
    )
    try {
      const started = await Promise.race([
        connection.start().then((): HubConnection | null => connection),
        timeout,
      ])
      if (explicitStop) {
        try {
          await connection.stop()
        } catch {
          // Already gone.
        }
        return null
      }
      if (started) {
        current = started
        everStarted = true
        gate.reset()
        options.onStateChange?.(hubStateFor("started"))
        options.onReady?.(started)
        if (isRetry) {
          options.onReconnected?.()
        }
        return started
      }
      // Start lost the race — tear the half-open connection down.
      try {
        await connection.stop()
      } catch {
        // Already gone.
      }
      options.onStateChange?.(hubStateFor("closed"))
      if (!explicitStop && !authLost) {
        gate.schedule(() => {
          void attemptStart(true)
        })
      }
      return null
    } catch (error) {
      try {
        await connection.stop()
      } catch {
        // Already gone.
      }
      options.onStateChange?.(hubStateFor("closed"))
      if (isHubAuthFailure(error)) {
        authLost = true
        gate.cancel()
        options.onAuthLost?.()
        return null
      }
      if (!explicitStop) {
        gate.schedule(() => {
          void attemptStart(true)
        })
      }
      return null
    }
  }

  return attemptStart(false)
}

/**
 * Minimal hub seam for the group calls — a real `HubConnection` satisfies
 * it structurally, tests pass a stub (the generic `invoke<T>` is widened
 * to `unknown` so concrete stubs stay assignable).
 */
export interface GroupInvokingConnection {
  invoke(methodName: string, ...args: unknown[]): Promise<unknown>
}

/** Joins the `chat:{id}` group; ownership-gated server-side, rejections swallowed. */
export async function joinChatGroup(
  connection: GroupInvokingConnection,
  sessionId: string
): Promise<void> {
  try {
    await connection.invoke("JoinChatAsync", sessionId)
  } catch {
    // Foreign or unknown session — no live chunks for this connection.
  }
}

/**
 * Re-joins every group after a reconnect — sequential, each rejection
 * swallowed per-group (a deleted session must not block the rest).
 */
export async function rejoinChatGroups(
  connection: GroupInvokingConnection,
  sessionIds: readonly string[]
): Promise<void> {
  for (const sessionId of sessionIds) {
    await joinChatGroup(connection, sessionId)
  }
}

export async function leaveChatGroup(
  connection: GroupInvokingConnection,
  sessionId: string
): Promise<void> {
  try {
    await connection.invoke("LeaveChatAsync", sessionId)
  } catch {
    // Connection already gone — that is what we wanted.
  }
}

/** Registers the two chat callbacks. Pure wiring, testable with a stub. */
export function bindChatEvents(
  connection: Pick<HubConnection, "on">,
  onChunk: (chunk: ChatChunkView) => void,
  onComplete: (event: ChatTurnCompleteView) => void
): void {
  connection.on(RealtimeTransportMethods.ChatChunk, (chunk: ChatChunkView) =>
    onChunk(chunk)
  )
  connection.on(
    RealtimeTransportMethods.ChatTurnComplete,
    (event: ChatTurnCompleteView) => onComplete(event)
  )
}

/**
 * SignalR half of the CLI: one connection factory + the chat-group join,
 * mirroring `dashboard/src/shared/realtime/runs-hub.ts`.
 *
 * The connection is **best-effort by contract**: `POST …/messages` is
 * synchronous on the host and returns the authoritative turn result, so a
 * dead socket degrades to "no live progress", never a failed turn. The
 * factory therefore resolves `null` on any start failure and the chat
 * command proceeds REST-only.
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
}

/**
 * Starts a connection joined to nothing. Resolves `null` when the hub is
 * unreachable within the timeout (server down, auth rejected, proxy eating
 * upgrades) — callers treat streaming as optional.
 */
export async function startChatHubConnection(
  options: ChatHubOptions
): Promise<HubConnection | null> {
  options.onStateChange?.(hubStateFor("connecting"))
  const connection = new HubConnectionBuilder()
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

  // The never-give-up policy means `onclose` only fires on an explicit
  // stop() (or a start that never completed) — the status bar drops to
  // offline. Handler slots are plain assignments, so the factory owns
  // them; callers hook in through the options callbacks.
  connection.onreconnecting = () =>
    options.onStateChange?.(hubStateFor("reconnecting"))
  connection.onreconnected = () => {
    options.onStateChange?.(hubStateFor("reconnected"))
    options.onReconnected?.()
  }
  connection.onclose = () => options.onStateChange?.(hubStateFor("closed"))

  const timeout = new Promise<null>((resolve) =>
    setTimeout(() => resolve(null), options.timeoutMs ?? 4000).unref?.()
  )
  try {
    const started = await Promise.race([
      connection.start().then((): HubConnection | null => connection),
      timeout,
    ])
    if (started) {
      options.onStateChange?.(hubStateFor("started"))
      return started
    }
    // Start lost the race — tear the half-open connection down.
    await connection.stop()
    options.onStateChange?.(hubStateFor("closed"))
    return null
  } catch {
    try {
      await connection.stop()
    } catch {
      // Already gone.
    }
    options.onStateChange?.(hubStateFor("closed"))
    return null
  }
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

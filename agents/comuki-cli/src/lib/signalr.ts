/**
 * SignalR half of the CLI: one connection factory + the chat-group join,
 * mirroring `dashboard/src/shared/realtime/runs-hub.ts`.
 *
 * The connection is **best-effort by contract**: `POST …/messages` is
 * synchronous on the host and returns the authoritative turn result, so a
 * dead socket degrades to "no live progress", never a failed turn. The
 * factory therefore resolves `null` on any start failure and the chat
 * command proceeds REST-only.
 */
import {
  HubConnectionBuilder,
  HttpTransportType,
  type HubConnection,
  type IHttpConnectionOptions,
} from "@microsoft/signalr"

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
}

/**
 * Starts a connection joined to nothing. Resolves `null` when the hub is
 * unreachable within the timeout (server down, auth rejected, proxy eating
 * upgrades) — callers treat streaming as optional.
 */
export async function startChatHubConnection(
  options: ChatHubOptions
): Promise<HubConnection | null> {
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
    .withAutomaticReconnect()
    .build()

  const timeout = new Promise<null>((resolve) =>
    setTimeout(() => resolve(null), options.timeoutMs ?? 4000).unref?.()
  )
  try {
    const started = await Promise.race([
      connection.start().then((): HubConnection | null => connection),
      timeout,
    ])
    if (started) {
      return started
    }
    // Start lost the race — tear the half-open connection down.
    await connection.stop()
    return null
  } catch {
    try {
      await connection.stop()
    } catch {
      // Already gone.
    }
    return null
  }
}

/** Joins the `chat:{id}` group; ownership-gated server-side, rejections swallowed. */
export async function joinChatGroup(
  connection: HubConnection,
  sessionId: string
): Promise<void> {
  try {
    await connection.invoke("JoinChatAsync", sessionId)
  } catch {
    // Foreign or unknown session — no live chunks for this connection.
  }
}

export async function leaveChatGroup(
  connection: HubConnection,
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

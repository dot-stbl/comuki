/**
 * One-shot / stdin mode: `comuki -m "text"` or `echo text | comuki`
 * sends a single turn and prints the assistant reply, then exits.
 * Never starts the Ink REPL. The client is injected so tests never
 * hit the network.
 */
import type {
  ChatSessionView,
  ChatTurnResultView,
  MessagePart,
} from "../lib/client"
import {
  addSession,
  fromPersisted,
  newPendingSession,
  PENDING_PREFIX,
  patchSession,
  readSessionsFile,
  sessionNameFromMessage,
  writeSessionsFile,
} from "../lib/sessions"
import { sessionsFilePath } from "../lib/config"

export const ONESHOT_TIMEOUT_MS = 120_000

export type OneshotMode = "flag" | "stdin" | "repl"

/**
 * `-m` always wins; a non-TTY stdin is pipe mode; otherwise the REPL.
 * `stdinIsTty === false` (piped) is the only non-flag oneshot trigger —
 * `undefined` (some Windows hosts) stays on the REPL.
 */
export function resolveOneshotMode(
  messageFlag: string | undefined,
  stdinIsTty: boolean | undefined
): OneshotMode {
  if (messageFlag !== undefined) {
    return "flag"
  }
  if (stdinIsTty === false) {
    return "stdin"
  }
  return "repl"
}

export interface OneshotClient {
  createSession(request: {
    projectId?: string
    title?: string
  }): Promise<ChatSessionView>
  postMessage(
    sessionId: string,
    message: string,
    signal?: AbortSignal
  ): Promise<ChatTurnResultView>
}

export interface OneshotOptions {
  readonly client: OneshotClient
  readonly message: string
  readonly projectId?: string
  readonly persistPath?: string
  readonly signal?: AbortSignal
}

export interface OneshotResult {
  readonly sessionId: string
  readonly reply: string
}

/** Plain text of the last assistant message (parts preferred over content). */
export function assistantReplyText(result: ChatTurnResultView): string {
  for (let index = result.messages.length - 1; index >= 0; index--) {
    const message = result.messages[index]
    if (message?.role !== "assistant") {
      continue
    }
    if (message.parts !== null && message.parts.length > 0) {
      const body = message.parts
        .map(partToText)
        .filter((part) => part.length > 0)
        .join("\n\n")
      if (body.length > 0) {
        return body
      }
    }
    return message.content
  }
  return ""
}

function partToText(part: MessagePart): string {
  switch (part.kind) {
    case "text":
      return part.markdown.trim()
    case "code":
      return part.source
    case "diagram":
      return part.source
    default:
      return ""
  }
}

export async function readStdinText(
  stream: { text(): Promise<string> } = Bun.stdin
): Promise<string> {
  return (await stream.text()).replace(/\r\n?/g, "\n").trimEnd()
}

/**
 * Create a session when none is persisted, POST the message, persist
 * the tab so the next REPL restore picks it up.
 */
export async function runOneshot(
  options: OneshotOptions
): Promise<OneshotResult> {
  const title = sessionNameFromMessage(options.message)
  const persistPath = options.persistPath ?? sessionsFilePath()
  const restored = fromPersisted(await readSessionsFile(persistPath))
  const active = restored.sessions[restored.activeIndex]
  let sessionId =
    active && !active.id.startsWith(PENDING_PREFIX) ? active.id : undefined
  if (sessionId === undefined) {
    const created = await options.client.createSession({
      projectId: options.projectId,
      title,
    })
    sessionId = created.id
  }
  const result = await options.client.postMessage(
    sessionId,
    options.message,
    options.signal
  )
  try {
    await persistOneshotSession({ id: sessionId, title }, restored, persistPath)
  } catch {
    // Restore is best-effort; a failed write must not hide the reply.
  }
  return { sessionId, reply: assistantReplyText(result) }
}

async function persistOneshotSession(
  session: { id: string; title: string },
  restored: ReturnType<typeof fromPersisted>,
  persistPath?: string
): Promise<void> {
  const existing = restored.sessions.find((item) => item.id === session.id)
  if (existing) {
    const sessions = patchSession(restored.sessions, session.id, {
      status: "done",
    })
    const activeIndex = sessions.findIndex((item) => item.id === session.id)
    await writeSessionsFile({ sessions, activeIndex }, persistPath)
    return
  }
  await writeSessionsFile(
    addSession(restored, {
      ...newPendingSession(),
      id: session.id,
      name: session.title,
      status: "done",
      hydrated: false,
    }),
    persistPath
  )
}

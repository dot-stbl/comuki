/**
 * HTTP adapters: the kernel's ConversationPort / ApprovalPort /
 * WorkspaceStore implemented over the existing typed REST client
 * (`lib/client.ts`) and JSON config dir. Transport only — no state,
 * no renderer concerns. The client is provided through a getter so a
 * mid-session credential swap (cookie login) is picked up on the next
 * call without rebuilding the ports.
 */
import type {
  ApprovalPort,
  ConversationPort,
  OpenedConversation,
  TurnOutcome,
  WorkspaceStore,
} from "../../harness/effect-runner"
import type { HarnessMessage, ProjectId, SessionId } from "../../harness/state"
import { projectId, sessionId } from "../../harness/state"
import type { WorkspaceDocument } from "../../harness/workspace"
import {
  ComukiClient,
  type ChatMessageView,
} from "../../lib/client"
import { readJsonFile, writeJsonFile } from "../../lib/json"

const TRANSCRIPT_PAGE_SIZE = 50

function messageRole(role: string): HarnessMessage["role"] {
  switch (role) {
    case "user":
    case "assistant":
    case "system":
    case "tool":
      return role
    default:
      return "system"
  }
}

export function harnessMessageFromView(
  view: ChatMessageView
): HarnessMessage {
  return {
    id: view.id,
    role: messageRole(view.role),
    content: view.content,
    createdAtUnixMs: Date.parse(view.createdAt),
    view,
  }
}

export class HttpConversationPort implements ConversationPort {
  /** In-flight submits by command id — the cancel seam (/stop). */
  private readonly controllers = new Map<string, AbortController>()

  constructor(private readonly client: () => ComukiClient) {}

  async openConversation(
    request: { projectId: ProjectId | null; title: string },
    signal: AbortSignal
  ): Promise<OpenedConversation> {
    void signal
    const created = await this.client().createSession({
      projectId: request.projectId ?? undefined,
      ...(request.title.length > 0 ? { title: request.title } : {}),
    })
    return {
      sessionId: sessionId(created.id),
      projectId: created.projectId ? projectId(created.projectId) : null,
      title: created.title,
    }
  }

  async submitTurn(
    sessionId: SessionId,
    message: string,
    commandId: string | undefined,
    signal: AbortSignal
  ): Promise<TurnOutcome> {
    const controller = new AbortController()
    const forward = () => controller.abort()
    signal.addEventListener("abort", forward, { once: true })
    const key = commandId ?? sessionId
    try {
      this.controllers.set(key, controller)
      const result = await this.client().postMessage(
        sessionId,
        message,
        controller.signal
      )
      return {
        messages: result.messages.map(harnessMessageFromView),
        awaitingApproval: result.awaitingApproval,
        pendingPlan: result.pendingPlan,
      }
    } finally {
      this.controllers.delete(key)
      signal.removeEventListener("abort", forward)
    }
  }

  async cancelTurn(
    sessionId: SessionId,
    commandId: string | undefined
  ): Promise<void> {
    // The fallback key mirrors submitTurn's registration so /stop
    // works even for commands dispatched without an id.
    this.controllers.get(commandId ?? sessionId)?.abort()
  }

  async loadConversation(
    sessionId: SessionId,
    signal: AbortSignal
  ): Promise<readonly HarnessMessage[]> {
    void signal
    const client = this.client()
    const first = await client.listMessages(sessionId, 1, TRANSCRIPT_PAGE_SIZE)
    const lastPage = Math.max(1, Math.ceil(first.total / TRANSCRIPT_PAGE_SIZE))
    const page =
      lastPage === 1
        ? first
        : await client.listMessages(sessionId, lastPage, TRANSCRIPT_PAGE_SIZE)
    return page.items.map(harnessMessageFromView)
  }
}

export class HttpApprovalPort implements ApprovalPort {
  constructor(private readonly client: () => ComukiClient) {}

  async decide(
    sessionId: SessionId,
    approved: boolean,
    reason: string | undefined,
    commandId: string | undefined,
    signal: AbortSignal
  ): Promise<TurnOutcome> {
    void commandId
    const result = await this.client().approve(
      sessionId,
      approved,
      reason,
      signal
    )
    return {
      messages: result.messages.map(harnessMessageFromView),
      awaitingApproval: result.awaitingApproval,
      pendingPlan: result.pendingPlan,
    }
  }
}

/** JSON-file workspace store — same file the legacy sessions doc used. */
export class JsonWorkspaceStore implements WorkspaceStore {
  constructor(private readonly path: string) {}

  async read(): Promise<unknown> {
    return readJsonFile(this.path)
  }

  async write(document: WorkspaceDocument, signal: AbortSignal): Promise<void> {
    void signal
    await writeJsonFile(this.path, document)
  }
}

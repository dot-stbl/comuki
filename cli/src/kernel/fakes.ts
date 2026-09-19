/**
 * Fake ports for kernel tests — no network, no timers, fully
 * scriptable. Conversation/approval calls stay pending until the test
 * resolves or aborts them, so in-flight turns, cancellation and
 * duplicate-suppression are all observable.
 */
import type {
  ApprovalPort,
  ConversationPort,
  HarnessEffectPorts,
  OpenedConversation,
  RealtimePort,
  TurnOutcome,
  WorkspaceStore,
} from "../harness/effect-runner"
import type { HarnessMessage, ProjectId, SessionId } from "../harness/state"
import { sessionId } from "../harness/state"
import type { EventFeedPort, FeedMessage } from "./feed"

export interface PendingSubmit {
  readonly sessionId: SessionId
  readonly message: string
  readonly commandId: string | undefined
  readonly signal: AbortSignal
  readonly resolve: (outcome: TurnOutcome) => void
  readonly reject: (error: unknown) => void
}

export interface PendingDecision {
  readonly sessionId: SessionId
  readonly approved: boolean
  readonly reason: string | undefined
  readonly commandId: string | undefined
  readonly signal: AbortSignal
  readonly resolve: (outcome: TurnOutcome) => void
  readonly reject: (error: unknown) => void
}

export class FakeConversationPort implements ConversationPort {
  private readonly openLog: { projectId: ProjectId | null; title: string }[] = []
  readonly submits: PendingSubmit[] = []
  private readonly cancelLog: { sessionId: SessionId; commandId: string | undefined }[] = []
  /** What the next openConversation resolves with (or throws, when it has an `error`). */
  nextOpened: OpenedConversation | { error: unknown } = {
    sessionId: sessionId("server-1"),
    projectId: null,
    title: "Task",
  }

  async openConversation(
    request: { projectId: ProjectId | null; title: string },
    signal: AbortSignal
  ): Promise<OpenedConversation> {
    void signal
    this.openLog.push({ projectId: request.projectId, title: request.title })
    if (this.nextOpened && "error" in this.nextOpened) {
      throw this.nextOpened.error
    }
    const opened = this.nextOpened as OpenedConversation
    return {
      sessionId: opened.sessionId,
      projectId: request.projectId ?? opened.projectId,
      // The real host echoes the requested title back on the view.
      title: request.title.length > 0 ? request.title : opened.title,
    }
  }

  async submitTurn(
    sessionId: SessionId,
    message: string,
    commandId: string | undefined,
    signal: AbortSignal
  ): Promise<TurnOutcome> {
    return new Promise<TurnOutcome>((resolve, reject) => {
      this.submits.push({ sessionId, message, commandId, signal, resolve, reject })
      if (commandId !== undefined) {
        signal.addEventListener(
          "abort",
          () => {
            reject(Object.assign(new Error("aborted"), { name: "AbortError" }))
          },
          { once: true }
        )
      }
    })
  }

  async cancelTurn(
    sessionId: SessionId,
    commandId: string | undefined
  ): Promise<void> {
    this.cancelLog.push({ sessionId, commandId })
    // Mirrors the real adapter: cancelling aborts the matching
    // in-flight submit, which rejects with an AbortError.
    const target = this.submits.find(
      (submit) =>
        (commandId !== undefined && submit.commandId === commandId) ||
        (commandId === undefined && submit.sessionId === sessionId)
    )
    target?.reject(Object.assign(new Error("aborted"), { name: "AbortError" }))
  }

  get cancelCalls(): readonly { sessionId: SessionId; commandId: string | undefined }[] {
    return this.cancelLog
  }

  async loadConversation(): Promise<readonly HarnessMessage[]> {
    return []
  }
}

export class FakeApprovalPort implements ApprovalPort {
  readonly decisions: PendingDecision[] = []

  async decide(
    sessionId: SessionId,
    approved: boolean,
    reason: string | undefined,
    commandId: string | undefined,
    signal: AbortSignal
  ): Promise<TurnOutcome> {
    return new Promise<TurnOutcome>((resolve, reject) => {
      this.decisions.push({
        sessionId,
        approved,
        reason,
        commandId,
        signal,
        resolve,
        reject,
      })
    })
  }
}

export class FakeRealtimePort implements RealtimePort {
  private readonly calls: SessionId[][] = []

  async setSubscriptions(sessionIds: readonly SessionId[]): Promise<void> {
    this.calls.push([...sessionIds])
  }

  get callsList(): readonly SessionId[][] {
    return this.calls
  }
}

export class FakeWorkspaceStore implements WorkspaceStore {
  readonly written: unknown[] = []
  private readonly initial: unknown

  constructor(initial: unknown = null) {
    this.initial = initial
  }

  async read(): Promise<unknown> {
    return this.initial
  }

  async write(document: unknown): Promise<void> {
    this.written.push(document)
  }
}

/** A push-based feed: the test pumps messages, the kernel pulls them. */
export function fakeFeed(): {
  readonly port: EventFeedPort
  push(message: FeedMessage): void
  end(): void
} {
  const queue: FeedMessage[] = []
  let notify: (() => void) | null = null
  let finished = false
  const port: EventFeedPort = {
    messages(signal: AbortSignal): AsyncIterable<FeedMessage> {
      return {
        async *[Symbol.asyncIterator]() {
          while (!signal.aborted) {
            if (queue.length > 0) {
              yield queue.shift() as FeedMessage
              continue
            }
            if (finished) {
              return
            }
            await new Promise<void>((resolve) => {
              notify = resolve
            })
            notify = null
          }
        },
      }
    },
  }
  return {
    port,
    push(message) {
      queue.push(message)
      notify?.()
    },
    end() {
      finished = true
      notify?.()
    },
  }
}

export function fakePorts(initialWorkspace: unknown = null): {
  ports: HarnessEffectPorts
  conversation: FakeConversationPort
  approval: FakeApprovalPort
  realtime: FakeRealtimePort
  workspace: FakeWorkspaceStore
} {
  const conversation = new FakeConversationPort()
  const approval = new FakeApprovalPort()
  const realtime = new FakeRealtimePort()
  const workspace = new FakeWorkspaceStore(initialWorkspace)
  return {
    ports: { conversation, approval, realtime, workspace },
    conversation,
    approval,
    realtime,
    workspace,
  }
}

/**
 * `HarnessEngineAdapter` — thin implementation of `HarnessEngineInterface`.
 *
 * Owns its own `HarnessState`, threads every event through
 * `reduceHarness`, runs the resulting effects through `runEffect`, and
 * feeds confirmation events (`sessions-persisted`,
 * `subscriptions-set`, …) back through the same reducer until the
 * chain settles. Subscribers fire after each pass.
 *
 * Step 1 surface: `focus-session` and `close-session`. Both are
 * already handled by `reduceHarness` (with their existing pure tests);
 * the adapter just plumbs the existing kernel's transport (`realtime`)
 * and workspace store (`workspace`) over as ports so the reducer's
 * `persist-sessions` and `set-subscriptions` effects actually run
 * through the same machinery the rest of the kernel uses.
 *
 * What the adapter does NOT do in step 1:
 *   - `open-session` / `pending-session-opened` — still on `ClientKernel`.
 *   - turn lifecycle (`turn-queued`, `thinking-*`, `turn-completed`)
 *     — still on `ClientKernel`.
 *   - approval pipeline — still on `ClientKernel`.
 *   - `ConversationPort` / `ApprovalPort` — stubbed as throw-on-use;
 *     no second implementer yet, so the port shape itself is delayed.
 *
 * Migration seam: when (much later) every event flows through this
 * adapter and `ClientKernel` is retired, this file becomes the
 * single entry point. Until then the two coexist side-by-side and
 * chat.tsx dispatches focus/close here while everything else keeps
 * going to `ClientKernel`.
 */
import type { HarnessEvent } from "./events"
import type { HarnessState } from "./state"
import { initialHarnessState } from "./state"
import { reduceHarness } from "./reducer"
import type {
  ConversationPort,
  ApprovalPort,
  RealtimePort,
  WorkspaceStore,
} from "./effect-runner"
import { runEffect } from "./effect-runner"
import type { HarnessListener, HarnessEngineInterface } from "./engine-types"

export interface HarnessEnginePorts {
  readonly conversation: ConversationPort
  readonly approval: ApprovalPort
  readonly realtime: RealtimePort
  readonly workspace: WorkspaceStore
}

export interface HarnessEngineOptions {
  /** One-shot seed — the harness snapshot captured at engine creation. */
  readonly initial: HarnessState
  /** Ports the engine runs reducer effects against. */
  readonly ports: HarnessEnginePorts
  /** Test hook: replaces `runEffect`. Defaults to the real runner. */
  readonly runEffect?: typeof runEffect
  /** Test hook: an AbortSignal that aborts in-flight effect chains. */
  readonly signal?: AbortSignal
}

/**
 * A no-op conversation port — throws on use. Step 1 never triggers
 * `create-remote-session`; the kernel still owns that flow. We define
 * the adapter surface for `ConversationPort` so swapping in a real
 * implementation later is a one-line change.
 */
export const throwawayConversationPort: ConversationPort = {
  async openConversation() {
    throw new Error("harness engine: conversation port is unused in step 1")
  },
  async submitTurn() {
    throw new Error("harness engine: conversation port is unused in step 1")
  },
  async cancelTurn() {
    throw new Error("harness engine: conversation port is unused in step 1")
  },
  async loadConversation() {
    throw new Error("harness engine: conversation port is unused in step 1")
  },
}

/** Same idea for approvals. */
export const throwawayApprovalPort: ApprovalPort = {
  async decide() {
    throw new Error("harness engine: approval port is unused in step 1")
  },
}

export class HarnessEngineAdapter implements HarnessEngineInterface {
  private state: HarnessState
  private readonly ports: HarnessEnginePorts
  private readonly runEffectFn: typeof runEffect
  private readonly signal: AbortSignal
  private readonly listeners = new Set<HarnessListener>()
  /** Serialises effect runs so confirmation events stay ordered. */
  private tail: Promise<void> = Promise.resolve()

  constructor(options: HarnessEngineOptions) {
    this.state = options.initial
    this.ports = options.ports
    this.runEffectFn = options.runEffect ?? runEffect
    // An always-aborted signal is a safe default — every effect-runner
    // path respects AbortSignal, so future steps that wire real
    // ports inherit the abort without extra plumbing.
    this.signal =
      options.signal ?? new AbortController().signal
  }

  get snapshot(): HarnessState {
    return this.state
  }

  dispatch(event: HarnessEvent): Promise<void> {
    const dispatch = (next: HarnessEvent) => this.applyOne(next)
    const transition = reduceHarness(this.state, event)
    this.state = transition.state
    this.notify()
    if (transition.effects.length === 0) {
      return Promise.resolve()
    }
    const chain = transition.effects.reduce<Promise<void>>(
      (acc, effect) =>
        acc.then(() =>
          this.runEffectFn(effect, this.ports, dispatch, this.signal),
        ),
      Promise.resolve(),
    )
    this.tail = this.tail.then(() => chain)
    return this.tail
  }

  subscribe(listener: HarnessListener): () => void {
    this.listeners.add(listener)
    return () => {
      this.listeners.delete(listener)
    }
  }

  private applyOne(event: HarnessEvent): void {
    const transition = reduceHarness(this.state, event)
    this.state = transition.state
    this.notify()
  }

  private notify(): void {
    const snapshot = this.state
    for (const listener of this.listeners) {
      listener(snapshot)
    }
  }
}

/** Convenience: an empty engine with the seed `initialHarnessState()`. */
export function emptyHarnessEngine(
  ports: HarnessEnginePorts,
  options?: Omit<HarnessEngineOptions, "initial" | "ports">,
): HarnessEngineAdapter {
  return new HarnessEngineAdapter({
    initial: initialHarnessState(),
    ports,
    ...(options ?? {}),
  })
}

// Re-export for the chat wiring (no new port types in step 1).
export type { ConversationPort, ApprovalPort, RealtimePort, WorkspaceStore }

/**
 * ClientKernel — the renderer-independent client state machine
 * (issue #83). Deliberately small interface:
 *
 * - `dispatch(intent)` — the renderer's entire vocabulary;
 * - `accept(event)` — normalized server/runtime events in;
 * - `snapshot()` — immutable state out;
 * - `subscribe(listener)` — change notification.
 *
 * Internally the kernel owns the ClientWorkspace aggregate (open
 * session refs, active session, drafts, cursors, outbound idempotency
 * log), conversation/approval projections, effect scheduling, request
 * correlation, stale/duplicate event rejection, reconnect
 * reconciliation, and pending→remote session adoption. Renderers
 * (Ink today, OpenTUI per ADR-0002) read snapshots and dispatch
 * intents — nothing else.
 *
 * The kernel contains no Ink/React/ANSI code; it talks to the world
 * exclusively through ports (`harness/effect-runner.ts`, `./feed.ts`).
 */
import type { HarnessEffect } from "../harness/effects"
import { runEffect, type HarnessEffectPorts } from "../harness/effect-runner"
import type { HarnessEvent } from "../harness/events"
import { translateIntent, type UserIntent } from "../harness/intents"
import { reduceHarness } from "../harness/reducer"
import {
  initialHarnessState,
  sessionId,
  type HarnessState,
  type SessionId,
} from "../harness/state"
import {
  applyWorkspaceToState,
  migrateWorkspaceDocument,
} from "../harness/workspace"
import type { EventFeedPort, FeedMessage } from "./feed"

/** Normalized server/runtime event accepted by the kernel. */
export type ClientEvent = HarnessEvent

export interface ClientSnapshot {
  readonly revision: number
  readonly state: HarnessState
}

export type Unsubscribe = () => void

export interface ClientKernel {
  dispatch(intent: UserIntent): void
  accept(event: ClientEvent): void
  snapshot(): ClientSnapshot
  subscribe(listener: (snapshot: ClientSnapshot) => void): Unsubscribe
  /** Loads the workspace document and starts consuming the feed. */
  start(): void
  /** Stops effect scheduling and feed consumption. Idempotent. */
  stop(): void
  /** Resolves when every scheduled effect has settled (test/stop seam). */
  whenIdle(): Promise<void>
}

export interface ClientKernelOptions {
  readonly ports: HarnessEffectPorts
  readonly feed?: EventFeedPort
  readonly now?: () => number
  /** Test observability — invoked for every effect as it starts. */
  readonly onEffect?: (effect: HarnessEffect) => void
  /**
   * Operational degradation signal. The kernel never crashes on a dead
   * feed, an unreadable workspace or a defective port — it degrades and
   * reports here. Optional; production hosts can wire a status line.
   */
  readonly onDegrade?: (reason: string) => void
}

export function createClientKernel(options: ClientKernelOptions): ClientKernel {
  const ports = options.ports
  const feed = options.feed
  const now = options.now ?? (() => Date.now())
  const controller = new AbortController()

  let state: HarnessState = initialHarnessState()
  let revision = 0
  let snapshot: ClientSnapshot = { revision, state }
  let started = false
  let stopped = false
  const listeners = new Set<(snapshot: ClientSnapshot) => void>()

  /** Duplicate server events must not produce duplicate effects. */
  const appliedServerEvents = new Set<string>()
  /** Conversation ops in flight, keyed per request identity. */
  const inFlight = new Set<string>()
  let activeCount = 0
  let idleWaiters: (() => void)[] = []
  /** Ordered lane for persist/subscriptions — last writer, newest state. */
  let chain: Promise<void> = Promise.resolve()

  function safeReduce(event: ClientEvent): {
    readonly state: HarnessState
    readonly effects: readonly HarnessEffect[]
  } | null {
    // Unknown/unsupported events must not crash the kernel (issue #83);
    // the reducer's exhaustive switch decides what is supported, a
    // rejection here just drops the event.
    try {
      return reduceHarness(state, event)
    } catch {
      return null
    }
  }

  function commit(): void {
    revision += 1
    snapshot = { revision, state }
    for (const listener of [...listeners]) {
      listener(snapshot)
    }
  }

  function effectKey(effect: HarnessEffect): string | null {
    switch (effect.type) {
      case "submit-turn":
      case "cancel-turn":
      case "decide-approval":
        return `${effect.type}:${effect.sessionId}:${effect.requestId}`
      case "create-remote-session":
        return `create-remote-session:${effect.pendingSessionId}`
      case "load-transcript":
        return `load-transcript:${effect.sessionId}`
      default:
        return null
    }
  }

  function trackStart(): void {
    activeCount += 1
  }

  function trackEnd(): void {
    activeCount -= 1
    if (activeCount === 0 && idleWaiters.length > 0) {
      const waiters = idleWaiters
      idleWaiters = []
      for (const waiter of waiters) {
        waiter()
      }
    }
  }

  function schedule(effects: readonly HarnessEffect[]): void {
    for (const effect of effects) {
      options.onEffect?.(effect)
      const key = effectKey(effect)
      if (key !== null) {
        // One wire request per identity — a duplicate effect (e.g. a
        // replayed event) must never issue a second POST.
        if (inFlight.has(key)) {
          continue
        }
        inFlight.add(key)
        trackStart()
        runEffect(effect, ports, acceptEvent, controller.signal)
          .catch((error: unknown) => {
            // runEffect normalizes its own failures into events; this
            // guards only against defects in the ports themselves.
            options.onDegrade?.(
              `port defect in ${effect.type}: ${
                error instanceof Error ? error.message : String(error)
              }`
            )
          })
          .finally(() => {
            inFlight.delete(key)
            trackEnd()
          })
        continue
      }
      if (effect.type === "persist-sessions" || effect.type === "set-subscriptions") {
        // Chained lanes keep writes ordered: the newest state is
        // always the last one written / subscribed.
        trackStart()
        chain = chain
          .then(() => runEffect(effect, ports, acceptEvent, controller.signal))
          .catch((error: unknown) => {
            options.onDegrade?.(
              `${effect.type} failed: ${
                error instanceof Error ? error.message : String(error)
              }`
            )
          })
          .finally(trackEnd)
        continue
      }
      // reconnect is owned by the feed adapter's own retry ramp.
    }
  }

  function applyEvents(events: readonly ClientEvent[]): void {
    const effects: HarnessEffect[] = []
    let changed = false
    for (const event of events) {
      if (stopped) {
        return
      }
      const key = serverEventKey(event)
      if (key !== null) {
        if (appliedServerEvents.has(key)) {
          continue
        }
        appliedServerEvents.add(key)
      }
      const transition = safeReduce(event)
      if (transition === null) {
        continue
      }
      state = transition.state
      effects.push(...transition.effects)
      changed = true
    }
    if (changed) {
      commit()
      schedule(effects)
    }
  }

  /** Effect runners dispatch their outcome events back through here. */
  function acceptEvent(event: ClientEvent): void {
    if (stopped) {
      return
    }
    applyEvents([event])
  }

  function validSessionId(value: string): SessionId | null {
    return value.length > 0 ? sessionId(value) : null
  }

  function cursorEvent(
    sessionId: string | undefined,
    receivedAtUnixMs: number
  ): readonly ClientEvent[] {
    if (sessionId === undefined) {
      return []
    }
    const valid = validSessionId(sessionId)
    return valid
      ? [
          {
            type: "cursor-advanced",
            sessionId: valid,
            lastSeenAtUnixMs: receivedAtUnixMs,
          },
        ]
      : []
  }

  function connectionEvent(
    event: "connecting" | "started" | "reconnecting" | "reconnected" | "closed"
  ): ClientEvent {
    switch (event) {
      case "connecting":
        return { type: "connection-started" }
      case "started":
      case "reconnected":
        return { type: "connection-established" }
      case "reconnecting":
        return { type: "connection-lost", attempt: 0 }
      case "closed":
        return { type: "connection-stopped" }
    }
  }

  function feedMessageToEvents(message: FeedMessage): readonly ClientEvent[] {
    switch (message.kind) {
      case "connection":
        return [connectionEvent(message.event)]
      case "chunk": {
        const cursor = cursorEvent(message.sessionId, message.receivedAtUnixMs)
        if (cursor.length === 0) {
          return []
        }
        // Request correlation: the chunk rides the turn that is in
        // flight for its session — chunks for anything else are stale.
        const session = state.sessions.find(
          (candidate) => candidate.identity.id === message.sessionId
        )
        if (
          session?.identity.kind === "remote" &&
          session.turn.kind === "thinking"
        ) {
          return [
            ...cursor,
            {
              type: "thinking-chunk-received",
              sessionId: session.identity.id,
              requestId: session.turn.requestId,
              text: message.text,
            },
          ]
        }
        return cursor
      }
      case "turn-complete":
        // REST is authoritative — the POST result renders the turn; a
        // completion frame only advances the cursor.
        return cursorEvent(message.sessionId, message.receivedAtUnixMs)
      case "unknown":
        return cursorEvent(message.sessionId, message.receivedAtUnixMs)
      default:
        return []
    }
  }

  async function consumeFeed(): Promise<void> {
    if (!feed || controller.signal.aborted) {
      return
    }
    try {
      for await (const message of feed.messages(controller.signal)) {
        if (controller.signal.aborted) {
          return
        }
        applyEvents(feedMessageToEvents(message))
      }
    } catch (error: unknown) {
      // The feed is best-effort by contract; a dead stream degrades to
      // REST-only and must never take the kernel down.
      options.onDegrade?.(
        `feed died: ${
          error instanceof Error ? error.message : String(error)
        }`
      )
    }
  }

  return {
    dispatch(intent) {
      if (stopped) {
        return
      }
      applyEvents(translateIntent(state, intent, now()))
    },
    accept(event) {
      if (stopped) {
        return
      }
      applyEvents([event])
    },
    snapshot() {
      return snapshot
    },
    subscribe(listener) {
      listeners.add(listener)
      return () => {
        listeners.delete(listener)
      }
    },
    start() {
      if (started || stopped) {
        return
      }
      started = true
      trackStart()
      chain = chain
        .then(async () => {
          const document = await ports.workspace.read()
          const workspace = migrateWorkspaceDocument(document)
          state = applyWorkspaceToState(state, workspace)
          commit()
        })
        .catch((error: unknown) => {
          // An unreadable workspace degrades to an empty one.
          options.onDegrade?.(
            `workspace unreadable: ${
              error instanceof Error ? error.message : String(error)
            }`
          )
        })
        .finally(trackEnd)
      void consumeFeed()
    },
    stop() {
      if (stopped) {
        return
      }
      stopped = true
      controller.abort()
      listeners.clear()
    },
    whenIdle() {
      return new Promise<void>((resolve) => {
        if (activeCount === 0) {
          resolve()
          return
        }
        idleWaiters.push(resolve)
      }).then(() => chain)
    },
  }
}

function serverEventKey(event: ClientEvent): string | null {
  switch (event.type) {
    case "turn-completed":
    case "turn-failed":
      return `${event.type}:${event.sessionId}:${event.requestId}`
    case "remote-session-adopted":
      return `adopted:${event.pendingSessionId}`
    case "remote-session-create-failed":
      return `create-failed:${event.pendingSessionId}`
    default:
      return null
  }
}

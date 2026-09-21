/**
 * ClientKernel — the renderer-independent client state machine
 * (issue #83). Deliberately small interface:
 *
 * - `dispatch(intent)` — the renderer's entire vocabulary;
 * - `accept(event)` — normalized server/runtime events in;
 * - `snapshot()` — immutable state out;
 * - `subscribe(listener)` — change notification;
 * - `recordDecision(receipt)` — append one row to the audit ledger
 *   (issue #76). Sync from the caller's POV; the writer chains
 *   file writes in a lane so concurrent decisions never interleave.
 *
 * Internally the kernel owns the ClientWorkspace aggregate (open
 * session refs, active session, drafts, cursors, outbound idempotency
 * log), conversation/approval projections, effect scheduling, request
 * correlation, stale/duplicate event rejection, reconnect
 * reconciliation, and pending→remote session adoption. Renderers
 * (Ink today, OpenTUI per ADR-0002) read snapshots and dispatch
 * intents — nothing else.
 *
 * Issue #77 — the kernel owns four durable singletons that survive
 * the same crash but layer separately: SessionStore (durable session
 * metadata), DraftStore (plain UTF-8 composer drafts), CursorStore
 * (per-session stream cursors + the reconnect `catchUp` primitive),
 * and the ReconnectOrchestrator (the coarse offline / recovering /
 * online state that gates mutating sends).
 *
 * Issue #81 — the kernel also owns three release-blocking subsystems:
 *   - `telemetry()` — structured diagnostics log (NDJSON under
 *     `<state>/diagnostics.log`, fire-and-forget).
 *   - `clientVersion()` — the result of the `GET /api/v1/version`
 *     handshake against the server; the host reads it for the
 *     `chrome.versionMismatch` hint.
 *   - `crash()` — the global `uncaughtException` /
 *     `unhandledRejection` handler chain; the host invokes
 *     `kernel.start()` and the kernel installs its handler as part
 *     of `start()` (the host can opt out via
 *     `options.skipCrashHandlers`).
 *
 * The kernel contains no Ink/React/ANSI code; it talks to the world
 * exclusively through ports (`harness/effect-runner.ts`, `./feed.ts`).
 * The receipt writer is owned by the kernel (it's the only stateful
 * side of an otherwise pure projection); no port seam is needed.
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
import {
  createDecisionReceiptStore,
  defaultStateDirectory,
  type DecisionReceipt,
  type DecisionReceiptStore,
} from "./receipts"
import { AttentionSource, type AttentionListener, type AttentionSignal } from "./attention"
import type { EventFeedPort, FeedMessage } from "./feed"
import {
  createCursorStore,
  type CursorStore,
} from "./cursors"
import {
  createDraftStore,
  type DraftStore,
} from "./drafts"
import {
  createReconnectOrchestrator,
  type ReconnectOrchestrator,
} from "./reconnect"
import {
  createSessionStore,
  type SessionMeta,
  type SessionStore,
} from "./sessions"
import {
  installCrashHandlers,
  type CrashHandlers,
} from "./crash"
import {
  createStructuredLog,
  type StructuredLog,
} from "./telemetry"
import {
  CLIENT_VERSION_STRING,
  resolveVersionHandshake,
  VersionMismatchError,
  type VersionFetch,
  type VersionHandshake,
} from "./version"

/** Normalized server/runtime event accepted by the kernel. */
export type ClientEvent = HarnessEvent

/**
 * Issue #77 — the result of a `dispatch` call. `ok: true` means the
 * intent translated to events and the kernel applied them; `ok: false`
 * means the kernel rejected the intent (the transport is offline /
 * recovering, or the kernel is stopped) and the host should keep the
 * draft text intact and surface the reason.
 *
 * `draft` is the composer body at the moment of rejection — the host
 * echoes it back unchanged so the user sees their work preserved.
 */
export type DispatchResult =
  | { readonly ok: true }
  | { readonly ok: false; readonly reason: "offline" | "stopped"; readonly draft: string }

export interface ClientSnapshot {
  readonly revision: number
  readonly state: HarnessState
  /**
   * Issue #77 — the durable session list (home view), the per-session
   * cursor map, and the coarse reconnect state. Three layers
   * independent of the workspace's transient session refs.
   *
   * `sessions` is `null` while the SessionStore is still loading from
   * disk — the host treats it as "still resolving". `cursors` is a
   * snapshot of the durable high-water marks. `online` is the
   * orchestrator's coarse state at the moment the snapshot was built.
   */
  readonly sessions: ReadonlyArray<SessionMeta> | null
  readonly cursors: Readonly<Record<string, number>>
  readonly online: boolean
}

export type Unsubscribe = () => void

export interface ClientKernel {
  dispatch(intent: UserIntent, draft?: string): DispatchResult
  accept(event: ClientEvent): void
  snapshot(): ClientSnapshot
  subscribe(listener: (snapshot: ClientSnapshot) => void): Unsubscribe
  /** Loads the workspace document and starts consuming the feed. */
  start(): void
  /** Stops effect scheduling and feed consumption. Idempotent. */
  stop(): void
  /** Resolves when every scheduled effect has settled (test/stop seam). */
  whenIdle(): Promise<void>
  /**
   * Append one row to the decision ledger (issue #76). Sync from the
   * caller's POV; the actual file write runs in the kernel's chained
   * lane. A corrupt filesystem must not break the dispatch chain —
   * the writer absorbs errors.
   */
  recordDecision(receipt: DecisionReceipt): void
  /**
   * Issue #78 — the freshest derived `AttentionSignal`. The host
   * awaits `whenIdle()` before reading so the signal reflects every
   * queued effect. Recomputed once per kernel commit (the underlying
   * `AttentionSource` subscribes internally).
   */
  attention(): AttentionSignal
  /**
   * Issue #78 — subscribe to attention-signal updates. Matches the
   * `EventTarget.addEventListener` shape used by `subscribe(...)`.
   */
  addAttentionListener(listener: AttentionListener): Unsubscribe
  /**
   * Issue #77 — durable session metadata singleton. The host reads
   * `kernel.sessions().list()` for the home view and calls
   * `upsert / rename / archive / fork` on it.
   */
  sessions(): SessionStore
  /** Issue #77 — composer drafts singleton, keyed by session id. */
  drafts(): DraftStore
  /** Issue #77 — per-session stream cursors + reconnect `catchUp`. */
  cursors(): CursorStore
  /** Issue #77 — coarse offline / recovering / online orchestrator. */
  reconnect(): ReconnectOrchestrator
  /**
   * Issue #81 — the structured diagnostics writer. The kernel owns a
   * single instance and exposes it via `telemetry()`; the host
   * calls `log(...)` from its error paths. Fire-and-forget: every
   * write is async; the chain resolves on `whenIdle()`.
   */
  telemetry(): StructuredLog
  /**
   * Issue #81 — the result of the server `GET /api/v1/version`
   * handshake. `null` before `start()` runs the handshake; after
   * `start()` the host reads the resolved status (`ok` / `warn`
   * / `refused`) to drive the `chrome.versionMismatch` UI hint.
   */
  clientVersion(): VersionHandshake | null
  /**
   * Issue #81 — the global crash handler chain. The kernel owns a
   * single instance and exposes it via `crash()`; tests read
   * `crash().installed` to assert idempotency. `crash().flush()`
   * drains the chained log lane before the host exits.
   */
  crash(): CrashHandlers
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
  /**
   * Override the receipt writer (issue #76). Defaults to an XDG-aware
   * writer rooted at the platform state directory; tests inject a
   * stub rooted at a temp directory.
   */
  readonly receipts?: DecisionReceiptStore
  /**
   * Issue #77 — override the durable session / draft / cursor
   * stores. Each defaults to a fresh XDG-rooted instance.
   */
  readonly sessions?: SessionStore
  readonly drafts?: DraftStore
  readonly cursors?: CursorStore
  readonly reconnect?: ReconnectOrchestrator
  /**
   * Issue #81 — override the structured-log writer. Tests inject a
   * stub rooted at a temp directory; production wires nothing and
   * gets the XDG-aware default.
   */
  readonly telemetry?: StructuredLog
  /**
   * Issue #81 — the host's resolved server base URL. When set,
   * `start()` performs `GET /api/v1/version` against this URL before
   * it begins accepting events. Tests omit it to skip the handshake.
   */
  readonly serverBaseUrl?: string
  /** Issue #81 — the fetch surface for the version handshake. */
  readonly versionFetch?: VersionFetch
  /**
   * Issue #81 — set `true` to skip the crash-handler install. The
   * default installs `uncaughtException` / `unhandledRejection`
   * listeners that route into the kernel's telemetry sink.
   */
  readonly skipCrashHandlers?: boolean
}

export function createClientKernel(options: ClientKernelOptions): ClientKernel {
  const ports = options.ports
  const feed = options.feed
  const now = options.now ?? (() => Date.now())
  const controller = new AbortController()
  // Receipt writer — singleton, lazy-initialised so tests can inject
  // before the first `recordDecision` call. The default root is the
  // XDG state directory; production wires nothing and gets that.
  const receipts: DecisionReceiptStore =
    options.receipts ?? createDecisionReceiptStore({ stateDirectory: defaultStateDirectory() })
  // Issue #77 — four durable singletons. The default state root is the
  // same one receipts already uses; tests inject per-kernel stores
  // rooted at temp directories.
  const stateDirectory = defaultStateDirectory()
  const sessionsStore: SessionStore =
    options.sessions ?? createSessionStore({ stateDirectory })
  const draftsStore: DraftStore =
    options.drafts ?? createDraftStore({ stateDirectory })
  const cursorsStore: CursorStore =
    options.cursors ?? createCursorStore({ stateDirectory })
  const reconnectOrchestrator: ReconnectOrchestrator =
    options.reconnect ?? createReconnectOrchestrator()
  // Issue #81 — structured diagnostics log + crash handlers. The
  // telemetry sink is shared with the crash handler so a panic
  // produces a structured `crash` event before the process exits.
  const telemetryLog: StructuredLog =
    options.telemetry ?? createStructuredLog({ stateDirectory })
  const crashHandlers: CrashHandlers =
    options.skipCrashHandlers === true
      ? { installed: false, flush: async () => undefined }
      : installCrashHandlers({ log: telemetryLog })
  // Issue #81 — the resolved server-version handshake. The host
  // reads `kernel.clientVersion()` to surface the
  // `chrome.versionMismatch` hint. Filled by `start()` when the
  // host provides `serverBaseUrl` + `versionFetch`; otherwise
  // stays `null` and the host treats it as "no handshake ran".
  let clientVersionResult: VersionHandshake | null = null

  let state: HarnessState = initialHarnessState()
  let revision = 0
  /** Issue #77 — the durable session list. `null` while the SessionStore loads. */
  let durableSessions: ReadonlyArray<SessionMeta> | null = null
  /** Issue #77 — the cursor snapshot, refreshed after every cursor mutation. */
  let cursorSnapshot: Readonly<Record<string, number>> = {}
  let snapshot: ClientSnapshot = {
    revision,
    state,
    sessions: durableSessions,
    cursors: cursorSnapshot,
    online: reconnectOrchestrator.state() === "online",
  }
  let started = false
  let stopped = false
  const listeners = new Set<(snapshot: ClientSnapshot) => void>()
  // Issue #78 — singleton attention source per kernel. The
  // `AttentionSource` type imports `ClientKernel` (for the surface
  // shape), but never reaches into kernel internals beyond the
  // public API; the type-only import erases at runtime.
  let attentionSource: AttentionSource | null = null

  /** Duplicate server events must not produce duplicate effects. */
  const appliedServerEvents = new Set<string>()
  /** Conversation ops in flight, keyed per request identity. */
  const inFlight = new Set<string>()
  let activeCount = 0
  let idleWaiters: (() => void)[] = []
  /** Ordered lane for persist/subscriptions — last writer, newest state. */
  let chain: Promise<void> = Promise.resolve()

  /**
   * Issue #77 — refresh the durable session list. Called from the
   * start sequence (initial load) and from kernel commits (whenever
   * a session list mutation may have happened). The SessionStore
   * pre-loads on first call so subsequent reads are cheap.
   */
  async function refreshSessions(): Promise<void> {
    const list = await sessionsStore.list({ archived: false })
    durableSessions = list
  }

  /**
   * Issue #77 — refresh the cursor snapshot. Called after every
   * cursor mutation. Cheap (the store holds the in-memory map).
   */
  async function refreshCursors(): Promise<void> {
    cursorSnapshot = await cursorsStore.snapshot()
  }

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
    snapshot = {
      revision,
      state,
      sessions: durableSessions,
      cursors: cursorSnapshot,
      online: reconnectOrchestrator.state() === "online",
    }
    for (const listener of [...listeners]) {
      listener(snapshot)
    }
  }

  /**
   * Issue #77 — the orchestrator fires on every transition; the
   * kernel's commit fires once on every change, and the coarse state
   * is part of the snapshot. Subscribe once at boot so offline /
   * recovering / online flip the snapshot's `online` flag.
   */
  const unsubscribeOrchestrator = reconnectOrchestrator.addEventListener(() => {
    if (stopped) {
      return
    }
    commit()
  })

  /** Local subscription helper — referenced by both the public surface and
   * the lazily-built `AttentionSource`. The AttentionSource wires itself
   * here before any host listener fires, so its first snapshot reflects
   * the kernel's commit-on-subscribe invariant. */
  function subscribeInternal(
    listener: (snapshot: ClientSnapshot) => void
  ): Unsubscribe {
    listeners.add(listener)
    return () => {
      listeners.delete(listener)
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

  /**
   * Issue #77 — flip the orchestrator when a connection event lands
   * through any path (the feed adapter OR a direct accept call).
   * The reducer updates the harness's `connection` field; the
   * orchestrator tracks the coarse offline / recovering / online
   * state for the dispatch gate.
   */
  function applyOrchestratorTransition(event: ClientEvent): void {
    switch (event.type) {
      case "connection-started":
        reconnectOrchestrator.markRecovering()
        return
      case "connection-established":
        reconnectOrchestrator.markOnline()
        return
      case "connection-lost":
        reconnectOrchestrator.markRecovering()
        return
      case "connection-stopped":
        reconnectOrchestrator.markOffline()
        return
      default:
        return
    }
  }

  function applyEvents(events: readonly ClientEvent[]): void {
    const effects: HarnessEffect[] = []
    let changed = false
    for (const event of events) {
      if (stopped) {
        return
      }
      applyOrchestratorTransition(event)
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
    // The orchestrator transition lives in `applyOrchestratorTransition`
    // so direct `accept` calls land in the same code path as feed
    // frames — the orchestrator is the kernel-side counterpart of the
    // hub for both flows.
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
          // Issue #77 — persist the cursor high-water mark alongside
          // the harness event so a reconnect knows where to backfill
          // from. Done asynchronously so the dispatch path stays
          // sync.
          void cursorsStore.set(message.sessionId, message.receivedAtUnixMs)
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
        // Even on a stale chunk, the cursor advances — issue #77's
        // "no event id should be lost" invariant.
        void cursorsStore.set(message.sessionId, message.receivedAtUnixMs)
        return cursor
      }
      case "turn-complete":
        // REST is authoritative — the POST result renders the turn; a
        // completion frame only advances the cursor.
        if (message.sessionId !== undefined) {
          void cursorsStore.set(message.sessionId, message.receivedAtUnixMs)
        }
        return cursorEvent(message.sessionId, message.receivedAtUnixMs)
      case "unknown":
        if (message.sessionId !== undefined) {
          void cursorsStore.set(message.sessionId, message.receivedAtUnixMs)
        }
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

  /**
   * Issue #77 — only mutating sends that hit the wire need the
   * online gate. Local workspace operations (open / focus / close /
   * rename / save-draft / clear-draft / cancel-turn) work even when
   * the realtime hub is down — they only affect the local state.
   * Turns and approvals need a live connection; the host shows
   * "draft retained" until the transport comes back.
   */
  function requiresOnline(intent: UserIntent): boolean {
    return intent.kind === "submit-turn" || intent.kind === "decide-approval"
  }

  return {
    dispatch(intent, draft) {
      if (stopped) {
        return { ok: false, reason: "stopped", draft: draft ?? "" }
      }
      if (requiresOnline(intent) && !reconnectOrchestrator.canDispatch()) {
        return { ok: false, reason: "offline", draft: draft ?? "" }
      }
      applyEvents(translateIntent(state, intent, now()))
      return { ok: true }
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
      return subscribeInternal(listener)
    },
    start() {
      if (started || stopped) {
        return
      }
      started = true
      trackStart()
      chain = chain
        .then(async () => {
          // Issue #81 — run the version handshake BEFORE the
          // workspace read so a refused handshake short-circuits the
          // rest of the boot sequence. The brief's matrix:
          //   - ok / warn → proceed; the host reads the result
          //     via `kernel.clientVersion()` for the UI hint.
          //   - refused → `VersionMismatchError` propagates up to
          //     the boot path; the host's try/catch surfaces the
          //     `chrome.versionMismatch` UI hint and exits.
          // We only run the handshake when both `serverBaseUrl` and
          // `versionFetch` are configured; tests + the offline
          // path omit them and the host treats it as "no handshake".
          if (options.serverBaseUrl !== undefined && options.versionFetch !== undefined) {
            try {
              clientVersionResult = await resolveVersionHandshake(
                options.serverBaseUrl,
                options.versionFetch,
                { clientVersion: CLIENT_VERSION_STRING }
              )
              telemetryLog.logFields(
                clientVersionResult.status === "ok" ? "info" : "warn",
                "version-handshake",
                {
                  client: clientVersionResult.client,
                  server: clientVersionResult.server,
                  status: clientVersionResult.status,
                  ...(clientVersionResult.reason !== undefined
                    ? { reason: clientVersionResult.reason }
                    : {}),
                }
              )
            } catch (error: unknown) {
              if (error instanceof VersionMismatchError) {
                // Refused — the host owns the user-facing surface.
                // Log to telemetry for the export-bundle bug report,
                // then re-throw so the host's boot path can decide
                // whether to surface the UI hint and exit.
                telemetryLog.logFields(
                  "error",
                  "version-handshake-refused",
                  {
                    client: error.clientVersion,
                    server: error.serverVersion,
                    reason: error.reason,
                  }
                )
                throw error
              }
              throw error
            }
          }
          const document = await ports.workspace.read()
          const workspace = migrateWorkspaceDocument(document)
          state = applyWorkspaceToState(state, workspace)
          // Issue #77 — load the durable session list + cursor map
          // once at boot so the snapshot's `sessions` and `cursors`
          // are populated for the home view. Both stores pre-load
          // on first call; we force them in parallel with the
          // workspace read.
          await Promise.all([refreshSessions(), refreshCursors()])
          commit()
        })
        .catch((error: unknown) => {
          if (error instanceof VersionMismatchError) {
            // The host catches this — pass through unchanged so the
            // UI hint can read the typed reason.
            throw error
          }
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
      unsubscribeOrchestrator()
      listeners.clear()
      attentionSource?.destroy()
      attentionSource = null
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
    recordDecision(receipt) {
      if (stopped) {
        return
      }
      receipts.append(receipt)
    },
    attention() {
      if (attentionSource === null) {
        attentionSource = createAttentionSource()
      }
      return attentionSource.snapshot()
    },
    addAttentionListener(listener) {
      if (attentionSource === null) {
        attentionSource = createAttentionSource()
      }
      return attentionSource.addAttentionListener(listener)
    },
    sessions() {
      return sessionsStore
    },
    drafts() {
      return draftsStore
    },
    cursors() {
      return cursorsStore
    },
    reconnect() {
      return reconnectOrchestrator
    },
    telemetry() {
      return telemetryLog
    },
    clientVersion() {
      return clientVersionResult
    },
    crash() {
      return crashHandlers
    },
  }

  /**
   * Wrap the kernel's own surface — `snapshot` + `subscribe` — into
   * the `ClientKernel` shape the `AttentionSource` expects. The
   * source never reads internals beyond these two methods, so the
   * cast is honest. (We do NOT forward `attention()` here — that
   * would recurse, since the source's own snapshot is the result.)
   */
  function createAttentionSource(): AttentionSource {
    const surface = {
      snapshot: () => snapshot,
      subscribe: (listener: (snapshot: ClientSnapshot) => void) =>
        subscribeInternal(listener),
    } as unknown as ClientKernel
    return new AttentionSource(surface, now)
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

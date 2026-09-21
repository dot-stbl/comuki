/**
 * Attention-first swarm signal (issue #78).
 *
 * A pure view-model that derives the user-facing attention list from
 * the latest `ClientSnapshot`. Workers = sessions (each remote session
 * is one worker the user owns). Priorities:
 *
 *   P0 — `awaiting-approval` (decision blocked), or `thinking` past
 *        the stall threshold with no heartbeat.
 *   P1 — `failed` turn, or evidence unread while not in awaiting state.
 *   P2 — running, no anomaly.
 *
 * P2 rows surface as a row in the canvas only when there is no P0/P1
 * to focus on (the focus-mode default: a quiet swarm renders nothing).
 *
 * The source is a thin singleton on the kernel. The kernel exposes
 * `attention()` and `addAttentionListener()` — same shape as the
 * platform's `EventTarget.addEventListener`, matching the existing
 * `kernel.subscribe(...)` contract.
 */

import type { ClientKernel, ClientSnapshot } from "./kernel"
import type {
  HarnessSession,
  HarnessState,
  SessionId,
  SessionKey,
} from "../harness/state"
import { sessionId as brandSessionId } from "../harness/state"

// ---------------------------------------------------------------------------
// Priority + reason — the typed view-model
// ---------------------------------------------------------------------------

export type AttentionPriority = "p0" | "p1" | "p2"

/**
 * Why a worker surfaced in this priority bucket. Stays stable across
 * the snapshot's lifecycle so a deep-link `inspect <id>` keeps the
 * same rationale the user saw at first paint.
 */
export type AttentionReason =
  | "awaiting-approval"
  | "stalled"
  | "evidence"
  | "failed"
  | "active"

/** The per-worker state the row's badge reflects. */
export type AttentionStateKind = "thinking" | "awaiting-approval" | "failed"

/**
 * One row in the swarm attention list. Lives in priority order
 * (P0 first); inside one bucket, oldest reason-time first.
 */
export interface AttentionItem {
  readonly sessionId: SessionId
  readonly priority: AttentionPriority
  readonly reason: AttentionReason
  readonly state: AttentionStateKind
  /**
   * Worker profile — a short string the row can prefix. For remote
   * sessions with a `pendingPlan`, the first plan-node profile (or
   * the session title when the plan is empty); otherwise the session
   * title truncated to 24 chars.
   */
  readonly profile: string
  /** The session title, kept verbatim for the canvas to render. */
  readonly title: string
  /** How long the worker has been in `state`. */
  readonly durationMs: number
  /**
   * Correlation id — the in-flight turn request id for `thinking` and
   * `failed`, the session id for `awaiting-approval`. The canvas
   * echoes this on the row and the deep-link command consumes it.
   */
  readonly correlationId: string
  /** One-line rationale the row carries next to the badge. */
  readonly rationale: string
  /**
   * The reason-time anchor — `now - reasonAtUnixMs` is the row's
   * `durationMs`. Kept as a unix-ms so the canvas can recompute
   * durations on re-render without recomputing the priority itself.
   */
  readonly reasonAtUnixMs: number
}

/** The full attention snapshot derived from the kernel snapshot. */
export interface AttentionSignal {
  readonly revision: number
  readonly generatedAtUnixMs: number
  /** Wall-clock at derivation — the canvas uses this for `now - durationMs`. */
  readonly nowUnixMs: number
  /**
   * Items in priority order. P0 first; within a bucket, oldest reason
   * first so the user sees the most stale anomaly at the top.
   */
  readonly items: readonly AttentionItem[]
}

// ---------------------------------------------------------------------------
// Tunables — issue #78 calls these out explicitly
// ---------------------------------------------------------------------------

/**
 * The stall threshold. A `thinking` turn without a heartbeat (chunk
 * arrival — the cursor) for longer than this is reported as P0. The
 * default matches the issue's "no heartbeat for 92s" example.
 */
export const DEFAULT_STALL_THRESHOLD_MS = 90_000

/**
 * The `failed` window. A turn that completed in `failed` within this
 * window is reported as P1 — after the window elapses the failure
 * falls off the list.
 */
export const DEFAULT_FAILED_WINDOW_MS = 60_000

/**
 * The `evidence unread` window. A session that produced output
 * (`session.unread === true`) and is NOT in `awaiting-approval` stays
 * on P1 for at least this long; after that, the marker decays and the
 * row demotes to P2 (if still running) or vanishes.
 */
export const DEFAULT_EVIDENCE_WINDOW_MS = 30_000

// ---------------------------------------------------------------------------
// Derivation — pure function over a snapshot
// ---------------------------------------------------------------------------

/**
 * The latest heartbeat for `session` — the cursor's lastSeenAt, or
 * the last transcript message's timestamp, or the session creation
 * time. The cursor is the most precise (chunk-arrival signal); the
 * fallback chain handles fresh sessions that have not produced any
 * chunks yet.
 */
function heartbeatAt(
  session: HarnessSession,
  cursors: Readonly<Record<string, number>>
): number {
  const cursor = cursors[session.identity.id] ?? 0
  if (cursor > 0) {
    return cursor
  }
  for (let index = session.transcript.length - 1; index >= 0; index -= 1) {
    const created = session.transcript[index]?.createdAtUnixMs ?? 0
    if (created > 0) {
      return created
    }
  }
  return session.createdAtUnixMs
}

/**
 * The reason-time anchor for `state`. For `thinking` it is the
 * heartbeat; for `awaiting-approval` it is the approval's
 * `createdAtUnixMs`; for `failed` it is the failure time (we use
 * `now` as a fallback — the harness does not stamp failed turns).
 */
function reasonAt(
  session: HarnessSession,
  cursors: Readonly<Record<string, number>>,
  nowUnixMs: number
): number {
  switch (session.turn.kind) {
    case "awaiting-approval": {
      for (let index = session.transcript.length - 1; index >= 0; index -= 1) {
        const created = session.transcript[index]?.createdAtUnixMs ?? 0
        if (created > 0) {
          return created
        }
      }
      return session.createdAtUnixMs
    }
    case "thinking":
      return heartbeatAt(session, cursors)
    case "failed":
      return nowUnixMs
    case "idle":
      return nowUnixMs
  }
}

/** The duration the canvas renders — `now - reasonAt`. */
function durationOf(
  session: HarnessSession,
  cursors: Readonly<Record<string, number>>,
  nowUnixMs: number
): number {
  const start = reasonAt(session, cursors, nowUnixMs)
  return Math.max(0, nowUnixMs - start)
}

/**
 * The worker profile string the row prefixes. The plan's first node
 * wins (multi-agent plans ship the worker shape as the first node);
 * the session title is the fallback. Truncated to a canvas-friendly
 * width.
 */
function profileOf(session: HarnessSession): string {
  const plan = session.pendingPlan
  if (plan !== null && plan !== undefined && typeof plan === "object") {
    const record = plan as Record<string, unknown>
    if (Array.isArray(record.nodes) && record.nodes.length > 0) {
      const head = record.nodes[0]
      if (head !== null && typeof head === "object") {
        const entry = head as Record<string, unknown>
        const profile =
          typeof entry.profileKey === "string" && entry.profileKey.length > 0
            ? entry.profileKey
            : typeof entry.key === "string" && entry.key.length > 0
              ? entry.key
              : ""
        if (profile.length > 0) {
          return truncate(profile, 24)
        }
      }
    }
  }
  if (session.title.length > 0) {
    return truncate(session.title, 24)
  }
  return "(untitled)"
}

function truncate(value: string, max: number): string {
  if (value.length <= max) {
    return value
  }
  return `${value.slice(0, Math.max(1, max - 1))}…`
}

/**
 * Decide a session's priority bucket. Pure: `session + state + now`.
 * The order of checks encodes the precedence (P0 wins over P1 wins
 * over P2); ties fall through to the more severe bucket.
 *
 * Failure persistence: the harness reducer does not stamp the time
 * a turn failed (no clock in `reduceHarness` by design), so we surface
 * every `failed` turn as P1 until the user retries or closes the
 * session. That matches focus-mode intent — a failed turn is a
 * decision the user must take — and keeps the kernel shape unchanged.
 */
function classify(
  session: HarnessSession,
  state: HarnessState,
  nowUnixMs: number,
  stallThresholdMs: number,
  _failedWindowMs: number,
  evidenceWindowMs: number
): { readonly priority: AttentionPriority; readonly reason: AttentionReason } | null {
  if (session.identity.kind !== "remote") {
    // Pending (not-yet-adopted) sessions do not surface — the user has
    // not committed to them yet. The focus-mode default: silence.
    return null
  }
  // P0 — awaiting approval: the user must decide.
  if (session.turn.kind === "awaiting-approval") {
    return { priority: "p0", reason: "awaiting-approval" }
  }
  // P0 — stalled: thinking past the threshold with no heartbeat.
  if (session.turn.kind === "thinking") {
    const stalledFor = nowUnixMs - heartbeatAt(session, state.cursors)
    if (stalledFor > stallThresholdMs) {
      return { priority: "p0", reason: "stalled" }
    }
  }
  // P1 — failed: every failed turn surfaces until the user retries
  // or closes the session. The harness carries no failure timestamp,
  // and the reducer is clock-free by design; persistence is the
  // honest default.
  if (session.turn.kind === "failed") {
    return { priority: "p1", reason: "failed" }
  }
  // P1 — evidence unread while running (not in awaiting state).
  if (session.turn.kind === "thinking" && session.unread) {
    const since = nowUnixMs - heartbeatAt(session, state.cursors)
    if (since <= evidenceWindowMs + stallThresholdMs) {
      return { priority: "p1", reason: "evidence" }
    }
  }
  // P2 — running normally.
  if (session.turn.kind === "thinking") {
    return { priority: "p2", reason: "active" }
  }
  return null
}

/** Build the rationale one-liner for the row. */
function rationaleFor(
  item: { readonly reason: AttentionReason; readonly priority: AttentionPriority },
  session: HarnessSession,
  nowUnixMs: number,
  stallThresholdMs: number
): string {
  switch (item.reason) {
    case "awaiting-approval": {
      const scope = pendingScope(session)
      return scope
    }
    case "stalled": {
      const since = Math.round(
        (nowUnixMs - heartbeatAt(session, {})) / 1000
      )
      return `no heartbeat for ${Math.max(stallThresholdMs / 1000, since)}s`
    }
    case "evidence":
      return "unread output"
    case "failed":
      return `turn failed · ${session.turn.kind === "failed" ? session.turn.error.code : "unknown"}`
    case "active":
      return "running"
  }
}

function pendingScope(session: HarnessSession): string {
  const plan = session.pendingPlan
  if (plan === null || plan === undefined || typeof plan !== "object") {
    return "awaiting approval"
  }
  const record = plan as Record<string, unknown>
  if (typeof record.scope === "string" && record.scope.length > 0) {
    return record.scope
  }
  if (typeof record.intent === "string" && record.intent.length > 0) {
    return record.intent
  }
  return "awaiting approval"
}

/** Cast a session's identity to a typed SessionId. */
function typedSessionId(id: SessionKey): SessionId {
  return brandSessionId(id)
}

function correlationFor(session: HarnessSession): string {
  switch (session.turn.kind) {
    case "thinking":
      return session.turn.requestId
    case "awaiting-approval":
      return session.turn.requestId
    case "failed":
      return session.turn.requestId
    case "idle":
      return session.identity.id
  }
}

/**
 * Derive the attention signal from a snapshot. Pure — no listeners, no
 * side effects. Tests inject the snapshot directly; the production
 * AttentionSource owns the subscription wiring.
 */
export function deriveAttention(
  snapshot: ClientSnapshot,
  options?: {
    readonly nowUnixMs?: number
    readonly stallThresholdMs?: number
    readonly failedWindowMs?: number
    readonly evidenceWindowMs?: number
  }
): AttentionSignal {
  const nowUnixMs = options?.nowUnixMs ?? Date.now()
  const stallThresholdMs = options?.stallThresholdMs ?? DEFAULT_STALL_THRESHOLD_MS
  const failedWindowMs = options?.failedWindowMs ?? DEFAULT_FAILED_WINDOW_MS
  const evidenceWindowMs = options?.evidenceWindowMs ?? DEFAULT_EVIDENCE_WINDOW_MS

  const items: AttentionItem[] = []
  for (const session of snapshot.state.sessions) {
    const classification = classify(
      session,
      snapshot.state,
      nowUnixMs,
      stallThresholdMs,
      failedWindowMs,
      evidenceWindowMs
    )
    if (classification === null) {
      continue
    }
    const turnKind = session.turn.kind
    if (turnKind !== "thinking" && turnKind !== "awaiting-approval" && turnKind !== "failed") {
      // Idle sessions do not surface.
      continue
    }
    const reasonAtUnixMs = reasonAt(session, snapshot.state.cursors, nowUnixMs)
    items.push({
      sessionId: typedSessionId(session.identity.id),
      priority: classification.priority,
      reason: classification.reason,
      state: turnKind,
      profile: profileOf(session),
      title: session.title,
      durationMs: durationOf(session, snapshot.state.cursors, nowUnixMs),
      correlationId: correlationFor(session),
      rationale: rationaleFor(classification, session, nowUnixMs, stallThresholdMs),
      reasonAtUnixMs,
    })
  }

  // Stable sort: P0 < P1 < P2; inside a bucket, oldest reason first.
  const rank = { p0: 0, p1: 1, p2: 2 } as const
  items.sort((left, right) => {
    if (left.priority !== right.priority) {
      return rank[left.priority] - rank[right.priority]
    }
    return left.reasonAtUnixMs - right.reasonAtUnixMs
  })

  return {
    revision: snapshot.revision,
    generatedAtUnixMs: nowUnixMs,
    nowUnixMs,
    items,
  }
}

// ---------------------------------------------------------------------------
// AttentionSource — singleton on the kernel, listener pattern
// ---------------------------------------------------------------------------

export type AttentionListener = (signal: AttentionSignal) => void
export type Unsubscribe = () => void

/**
 * One source per kernel. The host gets its handle via
 * `kernel.addAttentionListener(...)` — the kernel owns the instance
 * and exposes a stable, singleton-shaped API.
 */
export class AttentionSource {
  private readonly listeners = new Set<AttentionListener>()
  private latest: AttentionSignal
  private readonly unsubscribe: Unsubscribe

  constructor(
    private readonly kernel: ClientKernel,
    private readonly nowUnixMs: () => number = () => Date.now()
  ) {
    this.latest = deriveAttention(kernel.snapshot(), { nowUnixMs: nowUnixMs() })
    this.unsubscribe = kernel.subscribe((snapshot) => {
      this.latest = deriveAttention(snapshot, { nowUnixMs: nowUnixMs() })
      for (const listener of [...this.listeners]) {
        listener(this.latest)
      }
    })
  }

  /** The freshest derived signal — recomputes once per kernel commit. */
  snapshot(): AttentionSignal {
    return this.latest
  }

  /** Subscribe to attention updates; matches `addEventListener` shape. */
  addAttentionListener(listener: AttentionListener): Unsubscribe {
    this.listeners.add(listener)
    return () => {
      this.listeners.delete(listener)
    }
  }

  /** Tear down the underlying subscription + listeners. Idempotent. */
  destroy(): void {
    this.unsubscribe()
    this.listeners.clear()
  }
}
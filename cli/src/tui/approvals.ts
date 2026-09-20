/**
 * Risk-tiered approval entries (issue #76).
 *
 * The typed view-model that drives the three approval densities
 * (compact / domain / stacked) and the ledger writer (receipts.ts)
 * that persists each approve/reject action as one immutable NDJSON
 * row.
 *
 * One entry per approval request. `risk` is the server-classified
 * density; `pendingAction` is the typed payload the receipt writer
 * fingerprints to detect duplicate decisions on the same approval.
 * `approvalId` is the dedupe key — the live message id when the
 * approval is first observed, stable across rebuilds.
 *
 * Pure data: builders take wire shapes (a session's `pendingPlan`
 * JsonDocument plus the message that announced it) and yield
 * ready-to-render `ApprovalEntry` values. No I/O, no OpenTUI.
 */

import type {
  HarnessSession,
  SessionId,
} from "../harness/state"
import { fingerprintFor } from "../kernel/receipts"

// ---------------------------------------------------------------------------
// Risk density — server-classified rendering tier
// ---------------------------------------------------------------------------

/**
 * The three densities the server can assign to an approval:
 *
 * - `compact`: bounded low-risk commands (e.g. `chat/sessions/{id}/approve`
 *   with no rich payload). One summary line + a one-block detail.
 * - `domain`: a single-resource operation (e.g. `runs/{id}/cancel`,
 *   a scheduled-job edit). Same line shape, plus per-domain fields.
 * - `stacked`: multi-worker orchestration — a plan with > 2 nodes.
 *   The detail block opens to a per-node list; each node has its own
 *   density decision.
 */
export type RiskClass = "compact" | "domain" | "stacked"

// ---------------------------------------------------------------------------
// Applicability — once / scoped / range / requester
// ---------------------------------------------------------------------------

/**
 * Where the approval's authority lives. `scope` is the operation the
 * approval covers (a resource path, an action key, etc.). `requester`
 * is the subject asking; `toolId` is optional (only set when the
 * approval gates a tool invocation). `runId` / `planId` surface the
 * domain anchor when present.
 */
export interface Applicability {
  readonly scope: string
  readonly requester: string
  readonly toolId: string | null
  readonly runId: string | null
  readonly planId: string | null
}

/** Compact's once / scoped / range label — the receipt column. */
export type ApplicabilityRange = "once" | "scoped" | "range"

// ---------------------------------------------------------------------------
// Pending action — typed per risk class
// ---------------------------------------------------------------------------

/** A single plan node within a stacked approval. */
export interface StackedNode {
  readonly id: string
  readonly title: string
  readonly profileKey: string
  readonly brief: string
  /** Each node carries its own density — a stacked plan can mix tiers. */
  readonly density: RiskClass
  /** Optional payload — tool-specific or domain-specific data. */
  readonly payload?: unknown
}

/**
 * Compact density — bounded low-risk commands. The narrowest payload.
 */
export interface CompactAction {
  readonly kind: "compact"
  readonly operation: string
  readonly effects: string
  readonly applicability: Applicability
  readonly range: ApplicabilityRange
}

/**
 * Domain density — operations that touch a single resource.
 */
export interface DomainAction {
  readonly kind: "domain"
  readonly operation: string
  readonly effects: string
  readonly applicability: Applicability
  readonly runId: string
  readonly reason: string | null
  readonly planPayload: unknown
}

/**
 * Stacked density — multi-worker orchestration (a plan with N nodes).
 */
export interface StackedAction {
  readonly kind: "stacked"
  readonly operation: string
  readonly effects: string
  readonly applicability: Applicability
  readonly nodes: readonly StackedNode[]
}

/**
 * Typed per-density payload. The discriminator matches `ApprovalEntry.risk`
 * — the renderer branches on `risk`, the receipt writer branches on
 * `pendingAction.kind`.
 */
export type PendingAction = CompactAction | DomainAction | StackedAction

// ---------------------------------------------------------------------------
// ApprovalEntry — the transcript-level record
// ---------------------------------------------------------------------------

/** Where the entry sits in the audit lifecycle. */
export type ApprovalOutcome = "pending" | "approved" | "rejected"

/**
 * One approval entry — the single source of truth for the renderer and
 * the receipt writer. `risk` decides the renderer tier; `approvalId`
 * is the dedupe key the writer uses.
 */
export interface ApprovalEntry {
  /** Stable id for expansion state — session-scoped, never collides. */
  readonly id: string
  /** The message correlation id (creator of the approval). */
  readonly messageId: string
  readonly createdAtUnixMs: number
  readonly sessionId: SessionId
  readonly approvalId: string
  readonly risk: RiskClass
  readonly applicability: Applicability
  readonly pendingAction: PendingAction
  /**
   * Stable sha256 over the action payload, or `null` when no fingerprint
   * was computed yet (the entry just arrived). The receipt writer
   * stamps the row as-is; duplicate detection compares later entries.
   */
  readonly fingerprint: string | null
  readonly outcome: ApprovalOutcome
  /** Optional trailing meta (model name, tokens) — same shape as message meta. */
  readonly trailingMeta: unknown | null
}

// ---------------------------------------------------------------------------
// Builders — derive entries from wire shapes
// ---------------------------------------------------------------------------

/**
 * The plan payload the server sent. Loose shape — both the legacy
 * `{ planSteps: string[] }` and the platform wire `{ nodes: [{id, key,
 * brief, profileKey, title}], edges: [...], estimateMinutes: number }`
 * shapes land here. The adapter recognises both.
 */
export interface ApprovalPlanPayload {
  readonly intent: string | null
  readonly scope: string | null
  readonly risk: string | null
  readonly steps: readonly string[]
  readonly diff: readonly string[]
  readonly estimateMinutes: number | null
  readonly nodes: readonly ApprovalPlanNode[]
}

export interface ApprovalPlanNode {
  readonly id: string
  readonly title: string
  readonly profileKey: string
  readonly brief: string
}

/**
 * Lift the legacy `unknown` plan payload (the harness stores it as
 * `unknown` for portability) into a typed `ApprovalPlanPayload`.
 * Mirrors `view.ts::approvalCardModel` but does NOT depend on it —
 * this builder runs in the pure data layer.
 */
export function planPayloadFrom(plan: unknown): ApprovalPlanPayload {
  const empty: ApprovalPlanPayload = {
    intent: null,
    scope: null,
    risk: null,
    steps: [],
    diff: [],
    estimateMinutes: null,
    nodes: [],
  }
  if (plan === null || typeof plan !== "object") {
    return empty
  }
  const record = plan as Record<string, unknown>
  const steps = planSteps(record)
  const nodes = planNodes(record)
  const estimate =
    typeof record.estimateMinutes === "number" && record.estimateMinutes > 0
      ? Math.round(record.estimateMinutes)
      : null
  return {
    intent: optionalString(record.intent),
    scope: optionalString(record.scope),
    risk: optionalString(record.risk),
    steps,
    diff: optionalString(record.diff)?.split("\n") ?? [],
    estimateMinutes: estimate,
    nodes,
  }
}

function optionalString(value: unknown): string | null {
  return typeof value === "string" && value.trim().length > 0
    ? value.trim()
    : null
}

function planSteps(record: Record<string, unknown>): readonly string[] {
  if (Array.isArray(record.planSteps)) {
    const steps = record.planSteps.filter(
      (step): step is string => typeof step === "string" && step.length > 0
    )
    if (steps.length > 0) {
      return steps
    }
  }
  return []
}

function planNodes(record: Record<string, unknown>): readonly ApprovalPlanNode[] {
  if (!Array.isArray(record.nodes)) {
    return []
  }
  const nodes: ApprovalPlanNode[] = []
  for (const candidate of record.nodes) {
    if (candidate === null || typeof candidate !== "object") {
      continue
    }
    const node = candidate as Record<string, unknown>
    const id = optionalString(node.id) ?? optionalString(node.key)
    const title = optionalString(node.title)
    const brief = optionalString(node.brief)
    const profileKey = optionalString(node.profileKey) ?? "impl"
    if (id === null && title === null && brief === null) {
      continue
    }
    nodes.push({
      id: id ?? `n${nodes.length + 1}`,
      title: title ?? brief ?? id ?? `n${nodes.length + 1}`,
      profileKey,
      brief: brief ?? title ?? "",
    })
  }
  return nodes
}

/**
 * The pending approval entry for an awaiting-approval turn. Returns
 * `null` when the session is not awaiting approval or carries no
 * pending plan payload — the entry pipeline emits nothing in that case.
 *
 * `messageId` is the creator message id (the assistant turn that
 * surfaced the approval card). `requester` is the active subject.
 */
export function pendingApprovalEntry(input: {
  readonly session: HarnessSession
  readonly messageId: string
  readonly requester: string
  readonly createdAtUnixMs: number
  readonly trailingMeta: unknown | null
}): ApprovalEntry | null {
  const { session, messageId, requester, createdAtUnixMs, trailingMeta } = input
  if (session.identity.kind !== "remote") {
    return null
  }
  if (session.turn.kind !== "awaiting-approval") {
    return null
  }
  if (session.pendingPlan === undefined || session.pendingPlan === null) {
    return null
  }
  const payload = planPayloadFrom(session.pendingPlan)
  const applicability: Applicability = {
    scope: payload.scope ?? `chat/sessions/${session.identity.id}/approve`,
    requester,
    toolId: null,
    runId: null,
    // planId is the first node id when a plan is present; mirrors the
    // upstream `plan_id` field that gets stamped on executed runs.
    planId: payload.nodes[0]?.id ?? null,
  }
  // Density: stacked when the plan has > 2 nodes; otherwise domain
  // (a single-resource approval with a plan payload is still domain).
  const risk: RiskClass = payload.nodes.length > 2 ? "stacked" : "domain"
  const effects = payload.steps.length > 0
    ? payload.steps.join("; ")
    : (payload.intent ?? applicability.scope)
  const pendingAction = buildPendingAction({
    risk,
    applicability,
    payload,
    operation: applicability.scope,
    effects,
  })
  return {
    id: `${session.identity.id}#approval`,
    messageId,
    createdAtUnixMs:
      createdAtUnixMs > 0 ? createdAtUnixMs : Date.now(),
    sessionId: session.identity.id,
    approvalId: `${session.identity.id}#${messageId}#approval`,
    risk,
    applicability,
    pendingAction,
    fingerprint: null,
    outcome: "pending",
    trailingMeta,
  }
}

interface BuildPendingInput {
  readonly risk: RiskClass
  readonly applicability: Applicability
  readonly payload: ApprovalPlanPayload
  readonly operation: string
  readonly effects: string
}

function buildPendingAction(input: BuildPendingInput): PendingAction {
  const { risk, applicability, payload, operation, effects } = input
  if (risk === "stacked") {
    return {
      kind: "stacked",
      operation,
      effects,
      applicability: withPlanId(applicability, payload),
      nodes: payload.nodes.map((node) => ({
        id: node.id,
        title: node.title,
        profileKey: node.profileKey,
        brief: node.brief,
        density: classifyNode(node),
      })),
    }
  }
  if (risk === "compact") {
    return {
      kind: "compact",
      operation,
      effects,
      applicability,
      range: "once",
    }
  }
  return {
    kind: "domain",
    operation,
    effects,
    applicability: withPlanId(applicability, payload),
    runId: applicability.runId ?? "unknown",
    reason: payload.scope,
    planPayload: payload,
  }
}

function withPlanId(
  applicability: Applicability,
  payload: ApprovalPlanPayload
): Applicability {
  // planId is the first node id when a plan is present; mirrors the
  // upstream `plan_id` field that gets stamped on executed runs.
  // Caller already set the outer applicability's planId; this is a
  // no-op when both are populated, and a safety net when the caller
  // forgot.
  if (applicability.planId !== null) {
    return applicability
  }
  const planId = payload.nodes[0]?.id ?? null
  return planId === null ? applicability : { ...applicability, planId }
}

/** Heuristic node-density classifier — small nodes are compact, big plans are domain. */
function classifyNode(node: ApprovalPlanNode): RiskClass {
  if (node.brief.length > 240) {
    return "domain"
  }
  return "compact"
}

// ---------------------------------------------------------------------------
// Fingerprint — per-entry hash for the receipt ledger
// ---------------------------------------------------------------------------

/**
 * Stable sha256 over the typed `pendingAction`. Two identical
 * `ApprovalEntry` values (same `risk`, same payload, same node ids)
 * produce the same fingerprint — the test contract and the out-of-band
 * duplicate detector's contract.
 *
 * Delegates to `fingerprintFor` in the kernel's receipts module (the
 * canonical canonicaliser). Re-exported here for ergonomics.
 */
export function approvalFingerprint(entry: ApprovalEntry): string {
  return fingerprintFor(entry.pendingAction)
}

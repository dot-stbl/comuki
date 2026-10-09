import type { WorkTaskDetailView } from "@/shared/api/_generated/types/WorkTaskDetailView"
import type { WorkTaskSourceRefView as WFView } from "@/shared/api/_generated/types/WorkTaskSourceRefView"

import type {
  WorkTaskResolutionOutcome,
  WorkTaskSourceRefView,
  WorkTaskStatus,
  WorkTaskSummary,
} from "@/domains/work/model/types"

/**
 * Wire-to-domain projection — the kubb-generated
 * <c>WorkTaskDetailView</c> is the host's
 * <c>GET /api/v1/work/tasks/{taskId}</c> payload. The mapper carries the
 * shape the screens render and is the only place that knows the
 * wire types (per `add-mission-cowork/architecture.md` §"Boundary discipline":
 * domain code never imports the wire types directly).
 */

const KNOWN_STATUSES: ReadonlySet<WorkTaskStatus> = new Set([
  "Draft",
  "Ready",
  "Active",
  "Blocked",
  "Resolved",
  "Cancelled",
])

const KNOWN_OUTCOMES: ReadonlySet<WorkTaskResolutionOutcome> = new Set([
  "Succeeded",
  "Waived",
  "Replaced",
  "Failed",
])

function asStatus(wire: string): WorkTaskStatus {
  return KNOWN_STATUSES.has(wire as WorkTaskStatus)
    ? (wire as WorkTaskStatus)
    : "Draft"
}

function asOutcome(
  wire: string | null,
): WorkTaskResolutionOutcome | null {
  if (wire === null) {
    return null
  }
  return KNOWN_OUTCOMES.has(wire as WorkTaskResolutionOutcome)
    ? (wire as WorkTaskResolutionOutcome)
    : null
}

function asVisibility(wire: string): "Project" | "Mission" {
  return wire === "Mission" ? "Mission" : "Project"
}

function asSourceKind(wire: string): WorkTaskSourceRefView["kind"] {
  switch (wire) {
    case "Native":
    case "GitHub":
    case "GitLab":
    case "Jira":
    case "YandexTracker":
      return wire
    default:
      return "Native"
  }
}

function asSourceRefs(wire: readonly WFView[] | undefined): WorkTaskSourceRefView[] {
  if (!wire) {
    return []
  }
  return wire.map((source) => ({
    kind: asSourceKind(source.kind),
    externalId: source.externalId,
    displayName: source.displayName ?? null,
  }))
}

/**
 * Project a kubb-generated <c>WorkTaskDetailView</c> onto the
 * dashboard's <c>WorkTaskSummary</c>. The mapper returns a stable
 * shape regardless of which wire fields are present — missing
 * optional fields become null or empty array.
 */
export function mapWireToWorkTaskSummary(wire: WorkTaskDetailView): WorkTaskSummary {
  return {
    id: wire.id,
    projectId: wire.projectId,
    title: wire.title,
    status: asStatus(wire.status),
    resolutionOutcome: asOutcome(wire.resolutionOutcome ?? null),
    attemptOrdinal: typeof wire.attemptOrdinal === "string"
      ? Number.parseInt(wire.attemptOrdinal, 10)
      : wire.attemptOrdinal,
    activeAttemptId: wire.activeAttemptId ?? null,
    visibility: asVisibility(wire.visibility),
    missionId: wire.missionId ?? null,
    sourceRefs: asSourceRefs(wire.sourceRefs),
    briefVersion: typeof wire.briefVersion === "string"
      ? Number.parseInt(wire.briefVersion, 10)
      : wire.briefVersion,
    createdAt: wire.createdAt,
    updatedAt: wire.updatedAt,
  }
}

/**
 * Runtime guard for an untyped work-task response.
 *
 * The page is handed a value typed as <c>WorkTaskSummary</c> by the
 * kubb path, but the wire can land in odd shapes during partial
 * rollouts — a row stripped of its <c>status</c> when the host's
 * status column drifts, a <c>null</c> where a string was, a
 * <c>sourceRefs</c> field that is not an array. A cast at the
 * call site would only silence the compiler and let a missing
 * <c>id</c> reach the fact list; the predicate checks the minimum
 * the page actually reads and lets the caller fall back to the
 * notFound state when the shape is wrong.
 *
 * The same shape <c>readStatusFromPayload</c> uses on the runs
 * mapper: an <c>unknown</c> → <c>Record</c> → per-field <c>typeof</c>
 * narrowing, returning a <c>value is T</c> so the call site keeps
 * the well-typed value past the guard.
 */
export function isWorkTaskSummary(value: unknown): value is WorkTaskSummary {
  if (value === null || typeof value !== "object") {
    return false
  }
  const record = value as Record<string, unknown>
  if (typeof record["id"] !== "string" || record["id"].length === 0) {
    return false
  }
  if (typeof record["projectId"] !== "string" || record["projectId"].length === 0) {
    return false
  }
  if (typeof record["title"] !== "string") {
    return false
  }
  if (typeof record["status"] !== "string" || !KNOWN_STATUSES.has(record["status"] as WorkTaskStatus)) {
    return false
  }
  if (typeof record["attemptOrdinal"] !== "number" || !Number.isFinite(record["attemptOrdinal"])) {
    return false
  }
  if (typeof record["visibility"] !== "string" || (record["visibility"] !== "Project" && record["visibility"] !== "Mission")) {
    return false
  }
  if (!Array.isArray(record["sourceRefs"])) {
    return false
  }
  if (typeof record["briefVersion"] !== "number" || !Number.isFinite(record["briefVersion"])) {
    return false
  }
  if (typeof record["createdAt"] !== "string") {
    return false
  }
  if (typeof record["updatedAt"] !== "string") {
    return false
  }
  return true
}
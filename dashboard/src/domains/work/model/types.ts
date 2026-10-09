/**
 * WorkTask domain projections — the FE view of the umbrella's
 * `add-work-management` WorkTask aggregate (see
 * `openspec/changes/add-work-management/specs/work-management/spec.md`
 * for the wire shape). The kubb-generated types live under
 * `@/shared/api/_generated/types/WorkTask*`; this module projects
 * them onto the domain-friendly shape the screens render.
 *
 * Status enum follows the wire form (PascalCase) — the dashboard
 * renders it verbatim per the umbrella's `add-work-management/design.md`
 * status vocabulary (Draft / Ready / Active / Blocked / Resolved /
 * Cancelled).
 */

export type WorkTaskStatus =
  | "Draft"
  | "Ready"
  | "Active"
  | "Blocked"
  | "Resolved"
  | "Cancelled";

export type WorkTaskResolutionOutcome =
  | "Succeeded"
  | "Waived"
  | "Replaced"
  | "Failed";

/**
 * The dashboard's projection of one WorkTask. Excludes private fields
 * (the attempt ledger internals) and aggregates only the caller-facing
 * surface the screens render.
 */
export interface WorkTaskSummary {
  id: string;
  projectId: string;
  title: string;
  status: WorkTaskStatus;
  resolutionOutcome: WorkTaskResolutionOutcome | null;
  attemptOrdinal: number;
  activeAttemptId: string | null;
  visibility: "Project" | "Mission";
  missionId: string | null;
  sourceRefs: WorkTaskSourceRefView[];
  briefVersion: number;
  createdAt: string;
  updatedAt: string;
}

/**
 * Per-source-ref wire projection. The mapper drops fields the
 * screens don't render (the integration `connection_id` is server-side).
 */
export interface WorkTaskSourceRefView {
  kind: "Native" | "GitHub" | "GitLab" | "Jira" | "YandexTracker";
  externalId: string;
  displayName: string | null;
}

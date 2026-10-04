/**
 * Procedure domain types — the operator's view of a procedure in three
 * modes (studio / live / replay). The wire shape (`ProcedureVersionResponse`,
 * `ProcedureTraceResponse`, `ProcedureLiveResponse`, `ProposedPatchResponse`)
 * is mapped into these domain types in `api/mappers.ts` so the UI never
 * imports kubb DTOs directly.
 *
 * Mode is the workbench's panel selector — `studio` for authoring /
 * inspection, `live` for an admitted run, `replay` for the
 * planned-vs-observed timeline. The page component reads the mode from
 * a URL search param (`?mode=studio|live|replay`) so the three readings
 * share one route and one decision vocabulary.
 */
import type { Status } from "@/shared/ui/status-badge"

/** A closed set of workbench panels — the operator does not switch
 * routes between them; the page reads the mode from `?mode=`. */
export type ProcedureMode = "studio" | "live" | "replay"

/** The three patch verbs the spec names: add, remove, rewire,
 * re-parameterize. The wire's `kind` discriminator is `add-node`,
 * `remove-node`, `rewire-edge`, `re-parameterize-node`. */
export type ProcedurePatchOperationKind =
  | "add-node"
  | "remove-node"
  | "rewire-edge"
  | "re-parameterize-node"

/** One edge reference inside a rewired edge — both before and after. */
export interface ProcedureEdgeRef {
  readonly fromNodeId: string
  readonly fromPort: string
  readonly toNodeId: string
}

/** One rewired edge in a patch — the edge removed and the edge
 * inserted by the same rewire op. */
export interface ProcedureRewiredEdge {
  readonly before: ProcedureEdgeRef
  readonly after: ProcedureEdgeRef
}

/** One per-parameter delta inside a re-parameterized node. */
export interface ProcedureParameterChange {
  readonly name: string
  readonly before: string | null
  readonly after: string | null
}

/** One re-parameterized node in a patch's diff. */
export interface ProcedureReParameterizedNode {
  readonly nodeId: string
  readonly changes: ReadonlyArray<ProcedureParameterChange>
}

/** One node inserted by the patch. */
export interface ProcedureAddedNode {
  readonly id: string
  readonly kindKey: string
  readonly parameters: Readonly<Record<string, string>>
}

/** The semantic diff of a patch against its base version — five
 * buckets cover the four spec verbs (add, remove, rewire,
 * re-parameterize) plus the cascade-orphaned edges that come with
 * RemoveNode. The shape is exactly what
 * `GraphPatchDiff` carries in the Application layer, projected to
 * the wire via `ProposedPatchResponse` and re-projected here. */
export interface ProcedurePatchDiff {
  readonly unchanged: boolean
  readonly summary: string
  readonly addedNodes: ReadonlyArray<ProcedureAddedNode>
  readonly removedNodeIds: ReadonlyArray<string>
  readonly rewiredEdges: ReadonlyArray<ProcedureRewiredEdge>
  readonly reParameterizedNodes: ReadonlyArray<ProcedureReParameterizedNode>
}

/** A brain-drafted patch surfaced for human review. */
export interface ProposedProcedurePatch {
  readonly patchId: string
  readonly baseVersionId: string
  readonly rationale: string
  readonly diff: ProcedurePatchDiff
}

/** One node of the compiled graph. */
export interface ProcedureNode {
  readonly id: string
  readonly kindKey: string
  readonly parameters: Readonly<Record<string, string>>
}

/** One typed-port wire between two nodes. */
export interface ProcedureEdge {
  readonly fromNodeId: string
  readonly fromPort: string
  readonly toNodeId: string
}

/** A compiled procedure version — the immutable, content-addressed
 * artifact the runtime pins and Studio renders. */
export interface ProcedureVersion {
  readonly versionId: string
  readonly projectId: string
  readonly procedureKey: string
  readonly catalogVersion: string
  readonly sourceRef: string
  readonly nodes: ReadonlyArray<ProcedureNode>
  readonly edges: ReadonlyArray<ProcedureEdge>
}

/** One trace event in the planned-vs-observed timeline. The
 * `nodeId` field carries the version id for the synthetic
 * `pin_recorded` event the admission binder stamps; for runtime
 * events it carries the graph node the event relates to. */
export interface ProcedureTraceEvent {
  readonly nodeId: string
  readonly eventType: string
  readonly detail: string
  readonly at: string
}

/** The full planned-vs-observed trace of one procedure-pinned run. */
export interface ProcedureTrace {
  readonly runId: string
  readonly pinnedVersionId: string
  readonly events: ReadonlyArray<ProcedureTraceEvent>
}

/** The drift verdict the runtime stamps once it has a planned-vs-observed
 * comparison. Null on the wire when no comparison has been recorded yet. */
export interface ProcedureDrift {
  readonly isWithinPolicy: boolean
  readonly summary: string
}

/** The Live run projection — the pinned version, the timeline, and
 * (when recorded) the drift verdict. Server-side composed from the
 * trace store so the dashboard does not stitch run + trace. */
export interface ProcedureLive {
  readonly runId: string
  readonly pinnedVersionId: string | null
  readonly procedureKey: string | null
  readonly projectId: string | null
  readonly pinnedAt: string | null
  readonly eventCount: number
  readonly events: ReadonlyArray<ProcedureTraceEvent>
  readonly drift: ProcedureDrift | null
}

/** The runtime status a stage on the live run path carries — the same
 * closed vocabulary the rest of the design system reads (see
 * `shared/ui/status-badge`). Re-exported here so the domain does
 * not leak the badge implementation. */
export type ProcedureStageStatus = Status

/** One row on the live run's path — a stage with its current status. */
export interface ProcedureStageRow {
  readonly name: string
  readonly status: ProcedureStageStatus
}

/** A snapshot of a live run — path + repair loop state + human gate
 * state + evidence tally. The workbench's `Live run` mode renders
 * from this shape. */
export interface LiveProcedureRun {
  readonly runId: string
  readonly taskTitle: string
  readonly pinnedVersion: string
  readonly stages: ReadonlyArray<ProcedureStageRow>
  readonly repair: {
    readonly generation: number
    readonly maxGenerations: number
    readonly status: "running" | "needs-approval" | "completed" | "failed"
  }
  readonly gate: {
    readonly status: "waiting" | "approved" | "rejected"
    readonly risk: "low" | "medium" | "high"
  }
  readonly evidence: {
    readonly passed: number
    readonly failed: number
  }
}

/** One event in the Replay panel's planned-vs-observed timeline. */
export interface ProcedureReplayEvent {
  readonly id: string
  readonly stageName: string
  readonly at: string
  readonly planned: string
  readonly observed: string
  readonly drift: "none" | "within-policy" | "outside-policy"
}

/** A Replay observation — events + generated DAG the runtime wrote. */
export interface ProcedureReplay {
  readonly runId: string
  readonly events: ReadonlyArray<ProcedureReplayEvent>
  readonly generatedNodes: ReadonlyArray<ProcedureNode>
  readonly generatedEdges: ReadonlyArray<ProcedureEdge>
}
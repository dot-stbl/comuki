import type {
  LiveProcedureRun,
  ProcedureEdge,
  ProcedureLive,
  ProcedureMode,
  ProcedureNode,
  ProcedureReplay,
  ProcedureReplayEvent,
  ProcedureRewiredEdge,
  ProcedureStageRow,
  ProcedureStageStatus,
  ProcedureTrace,
  ProcedureTraceEvent,
  ProcedureVersion,
  ProposedProcedurePatch,
} from "@/domains/procedures/model/types"
import type { ProcedureEdgeDto } from "@/shared/api/_generated/types/ProcedureEdgeDto"
import type { ProcedureNodeDto } from "@/shared/api/_generated/types/ProcedureNodeDto"
import type { ProcedureVersionResponse } from "@/shared/api/_generated/types/ProcedureVersionResponse"
import type { ProcedureLiveResponse } from "@/shared/api/_generated/types/ProcedureLiveResponse"
import type { ProcedureTraceResponse } from "@/shared/api/_generated/types/ProcedureTraceResponse"
import type { TraceEventDto } from "@/shared/api/_generated/types/TraceEventDto"
import type { ProposedPatchResponse } from "@/shared/api/_generated/types/ProposedPatchResponse"
import type { AddedNodeDto } from "@/shared/api/_generated/types/AddedNodeDto"
import type { RewiredEdgeDto } from "@/shared/api/_generated/types/RewiredEdgeDto"
import type { ReParameterizedNodeDto } from "@/shared/api/_generated/types/ReParameterizedNodeDto"
import type { ParameterChangeDto } from "@/shared/api/_generated/types/ParameterChangeDto"

/**
 * Wire → domain mappers for the procedures surface.
 *
 * Every mapper follows the same shape: the wire DTO is non-nullable (the
 * kubb generator strips optionals), every field is projected by name.
 * No `as` casts in domain code — the wire vocabulary may differ from
 * the domain vocabulary (see `ProcedureVersionResponse` ↔
 * `ProcedureVersion`), and a hand-written mapper keeps both surfaces
 * renameable without dragging the other.
 */

function nodeOf(dto: ProcedureNodeDto): ProcedureNode {
  return {
    id: dto.id,
    kindKey: dto.kindKey,
    parameters: dto.parameters,
  }
}

function edgeOf(dto: ProcedureEdgeDto): ProcedureEdge {
  return {
    fromNodeId: dto.fromNodeId,
    fromPort: dto.fromPort,
    toNodeId: dto.toNodeId,
  }
}

/** Wire `ProcedureVersionResponse` → domain `ProcedureVersion`. */
export function mapProcedureVersionResponse(
  response: ProcedureVersionResponse
): ProcedureVersion {
  return {
    versionId: response.versionId,
    projectId: response.projectId,
    procedureKey: response.procedureKey,
    catalogVersion: response.catalogVersion,
    sourceRef: response.sourceRef,
    nodes: response.nodes.map(nodeOf),
    edges: response.edges.map(edgeOf),
  }
}

/** Wire trace event → domain `ProcedureTraceEvent`. */
function traceEventOf(dto: TraceEventDto): ProcedureTraceEvent {
  return {
    nodeId: dto.nodeId,
    eventType: dto.eventType,
    detail: dto.detail,
    at: dto.at,
  }
}

/** Wire `ProcedureTraceResponse` → domain `ProcedureTrace`. */
export function mapProcedureTraceResponse(
  response: ProcedureTraceResponse
): ProcedureTrace {
  return {
    runId: response.runId,
    pinnedVersionId: response.pinnedVersionId,
    events: response.events.map(traceEventOf),
  }
}

/** Wire `ProcedureLiveResponse` → domain `ProcedureLive`. */
export function mapProcedureLiveResponse(
  response: NonNullable<ProcedureLiveResponse>
): ProcedureLive {
  return {
    runId: response.runId,
    pinnedVersionId: response.pinnedVersionId,
    procedureKey: response.procedureKey,
    projectId: response.projectId,
    pinnedAt: response.pinnedAt,
    eventCount: toEventCount(response.eventCount),
    events: response.events.map(traceEventOf),
    drift: response.drift
      ? {
          isWithinPolicy: response.drift.isWithinPolicy,
          summary: response.drift.summary,
        }
      : null,
  }
}

/** Wire's `eventCount` is `number | string` (kubb convention for int64);
 * the domain shape carries a plain number. A null string — a value the
 * wire can carry when the host has not stamped a count — coerces to 0
 * so the workbench's empty-state path renders. */
function toEventCount(input: number | string | undefined): number {
  if (typeof input === "number") {
    return Number.isFinite(input) ? input : 0
  }
  if (typeof input === "string") {
    const parsed = Number.parseInt(input, 10)
    return Number.isFinite(parsed) ? parsed : 0
  }
  return 0
}

function addedNodeOf(dto: AddedNodeDto): {
  id: string
  kindKey: string
  parameters: Readonly<Record<string, string>>
} {
  return {
    id: dto.id,
    kindKey: dto.kindKey,
    parameters: dto.parameters,
  }
}

function rewiredEdgeOf(dto: RewiredEdgeDto): ProcedureRewiredEdge {
  return {
    before: {
      fromNodeId: dto.before.fromNodeId,
      fromPort: dto.before.fromPort,
      toNodeId: dto.before.toNodeId,
    },
    after: {
      fromNodeId: dto.after.fromNodeId,
      fromPort: dto.after.fromPort,
      toNodeId: dto.after.toNodeId,
    },
  }
}

function parameterChangeOf(dto: ParameterChangeDto): {
  name: string
  before: string | null
  after: string | null
} {
  return {
    name: dto.name,
    before: dto.before,
    after: dto.after,
  }
}

function reParameterizedNodeOf(dto: ReParameterizedNodeDto): {
  nodeId: string
  changes: ReadonlyArray<{
    name: string
    before: string | null
    after: string | null
  }>
} {
  return {
    nodeId: dto.nodeId,
    changes: dto.changes.map(parameterChangeOf),
  }
}

/** Wire `ProposedPatchResponse` → domain `ProposedProcedurePatch`. */
export function mapProposedPatchResponse(
  response: ProposedPatchResponse
): ProposedProcedurePatch {
  return {
    patchId: response.patchId,
    baseVersionId: response.baseVersionId,
    rationale: response.rationale,
    diff: {
      unchanged: response.unchanged,
      summary: response.diffSummary,
      addedNodes: response.addedNodes.map(addedNodeOf),
      removedNodeIds: response.removedNodeIds,
      rewiredEdges: response.rewiredEdges.map(rewiredEdgeOf),
      reParameterizedNodes:
        response.reParameterizedNodes.map(reParameterizedNodeOf),
    },
  }
}

/** Stage row types — debug helper. The Live run fixture currently lives
 * in `model/procedure-fixtures.ts`; the real-mode path is composed
 * from the live-projection response + the planned run view (task 5.3
 * wires that). Kept here so the live-run path can be filled in
 * without changing the workbench. */

function statusOf(dto: {
  name: string
  status: string
}): ProcedureStageRow {
  // The wire's stage status is one of the seven design-system words;
  // the domain narrows to the closed set the workbench renders.
  const status: ProcedureStageStatus =
    dto.status === "succeeded" ? "success" : (dto.status as ProcedureStageStatus)
  return { name: dto.name, status }
}

export function mapLiveRunFixture(input: {
  taskTitle: string
  pinnedVersion: string
  stages: ReadonlyArray<{ name: string; status: string }>
  repair: LiveProcedureRun["repair"]
  gate: LiveProcedureRun["gate"]
  evidence: LiveProcedureRun["evidence"]
}): LiveProcedureRun {
  return {
    runId: "",
    taskTitle: input.taskTitle,
    pinnedVersion: input.pinnedVersion,
    stages: input.stages.map(statusOf),
    repair: input.repair,
    gate: input.gate,
    evidence: input.evidence,
  }
}

/** Replay event types — kept here so the Replay path can be filled in. */
export function mapReplayEvent(dto: {
  id: string
  stageName: string
  at: string
  planned: string
  observed: string
  drift: string
}): ProcedureReplayEvent {
  const drift: ProcedureReplayEvent["drift"] =
    dto.drift === "none" ||
    dto.drift === "within-policy" ||
    dto.drift === "outside-policy"
      ? dto.drift
      : "none"
  return {
    id: dto.id,
    stageName: dto.stageName,
    at: dto.at,
    planned: dto.planned,
    observed: dto.observed,
    drift,
  }
}

export function mapReplay(input: {
  runId: string
  events: ReadonlyArray<{
    id: string
    stageName: string
    at: string
    planned: string
    observed: string
    drift: string
  }>
  generatedNodes: ReadonlyArray<{
    id: string
    kindKey: string
    parameters: Readonly<Record<string, string>>
  }>
  generatedEdges: ReadonlyArray<{
    fromNodeId: string
    fromPort: string
    toNodeId: string
  }>
}): ProcedureReplay {
  return {
    runId: input.runId,
    events: input.events.map(mapReplayEvent),
    generatedNodes: input.generatedNodes.map((node) => ({
      id: node.id,
      kindKey: node.kindKey,
      parameters: node.parameters,
    })),
    generatedEdges: input.generatedEdges.map((edge) => ({
      fromNodeId: edge.fromNodeId,
      fromPort: edge.fromPort,
      toNodeId: edge.toNodeId,
    })),
  }
}

/** The closed set of workbench modes — the page reads from
 * `?mode=studio|live|replay`, the workbench accepts the same. */
export function normalizeMode(input: string | null): ProcedureMode {
  if (input === "live" || input === "replay") {
    return input
  }
  return "studio"
}
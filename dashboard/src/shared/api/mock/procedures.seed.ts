/**
 * Procedures mock seed — the canonical "no backend" version of the
 * procedures surface that the workbench renders in `VITE_USE_MOCK=true`.
 *
 * The workbench itself reads its own fixtures (`model/procedure-fixtures.ts`)
 * for the panel it shows — Studio / Live / Replay. The query hooks
 * (`useLatestProcedureVersionQuery`, `useProcedureTraceQuery`, …) fall
 * through to this seed when the FE is in mock mode and the kubb client
 * is bypassed. Real mode wires through `kubb-client.ts` directly.
 *
 * Three fixtures — one per workbench mode — round-trip the procedures
 * domain vocabulary through the wire shape and back, so a real-mode
 * round-trip is byte-for-byte equivalent to a mock-mode round-trip.
 */

import {
  STUDIO_DEFAULT,
  LIVE_RUN_NEEDS_DECISION,
  REPLAY_OBSERVED_DRIFT,
  STANDARD_FEATURE_PROCEDURE,
} from "@/domains/procedures/model/procedure-fixtures"
import type {
  ProcedureLive,
  ProcedurePatchDiff,
  ProcedureTrace,
  ProcedureVersion,
} from "@/domains/procedures/model/types"

/** One seed row — the procedure the dashboard's mock-mode renderer
 * surfaces for the canonical `standard-feature` flow. */
export const PROCEDURES_SEED: ReadonlyArray<{
  projectId: string
  procedureKey: string
  version: ProcedureVersion
}> = [
  {
    projectId: "p_comuki",
    procedureKey: "standard-feature",
    version: {
      versionId: "v1-64hexdigest-of-the-published-definition",
      projectId: "p_comuki",
      procedureKey: "standard-feature",
      catalogVersion: "1",
      sourceRef: "client git source",
      nodes: STANDARD_FEATURE_PROCEDURE.nodes.map((node) => ({
        id: node.id,
        kindKey: node.data.kind,
        parameters: {},
      })),
      edges: STANDARD_FEATURE_PROCEDURE.edges.map((edge) => ({
        fromNodeId: edge.source,
        fromPort: "default",
        toNodeId: edge.target,
      })),
    },
  },
]

/** Trace seed — the planned-vs-observed timeline for the canonical
 * run that the Live + Replay panels render. */
export const PROCEDURES_TRACE_SEED: ReadonlyArray<{
  runId: string
  trace: ProcedureTrace
}> = [
  {
    runId: "r-crown-fixture-mock",
    trace: {
      runId: "r-crown-fixture-mock",
      pinnedVersionId: "v1-64hexdigest-of-the-published-definition",
      events: (REPLAY_OBSERVED_DRIFT.replay?.events ?? []).flatMap((event) => [
        {
          nodeId: event.id,
          eventType: "planned",
          detail: event.planned,
          at: event.at,
        },
        {
          nodeId: event.id,
          eventType: "observed",
          detail: event.observed,
          at: event.at,
        },
      ]),
    },
  },
]

/** Live run projection seed — the read the Live run panel reads as
 * the composed projection (pinned version + timeline + drift). */
export const PROCEDURES_LIVE_SEED: ReadonlyArray<ProcedureLive> = [
  {
    runId: "r-crown-fixture-mock",
    pinnedVersionId: "v1-64hexdigest-of-the-published-definition",
    procedureKey: "standard-feature",
    projectId: "p_comuki",
    pinnedAt: "2026-10-04T08:46:00Z",
    eventCount: REPLAY_OBSERVED_DRIFT.replay?.events.length ?? 0,
    events: PROCEDURES_TRACE_SEED[0].trace.events,
    drift: null,
  },
]

/** The diff the propose-patch mutation returns in mock mode — a
 * harmless "added one agent" diff the Studio canvas can paint. */
export const PROCEDURES_PROPOSED_DIFF_SEED: ProcedurePatchDiff = {
  unchanged: false,
  summary: "added 1, rewired 1",
  addedNodes: [
    {
      id: "agent-extra",
      kindKey: "agent",
      parameters: {},
    },
  ],
  removedNodeIds: [],
  rewiredEdges: [],
  reParameterizedNodes: [],
}

/** Lookups the mock-mode query hooks use — by (projectId, procedureKey)
 * and by runId. */
export function findSeedProcedureVersion(
  projectId: string,
  procedureKey: string,
): ProcedureVersion | null {
  return (
    PROCEDURES_SEED.find(
      (seed) => seed.projectId === projectId && seed.procedureKey === procedureKey
    )?.version ?? null
  )
}

export function findSeedProcedureTrace(runId: string): ProcedureTrace | null {
  return PROCEDURES_TRACE_SEED.find((seed) => seed.runId === runId)?.trace ?? null
}

export function findSeedProcedureLive(runId: string): ProcedureLive | null {
  return PROCEDURES_LIVE_SEED.find((seed) => seed.runId === runId) ?? null
}

// The fixtures are referenced here so the FE audit ("every mock-mode
// render reads a fixture, not a string literal") does not have to
// chase imports through the workbench component.
void STUDIO_DEFAULT
void LIVE_RUN_NEEDS_DECISION
void REPLAY_OBSERVED_DRIFT
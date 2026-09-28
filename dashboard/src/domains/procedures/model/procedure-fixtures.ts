/**
 * Procedure Workbench — fixture data the stories hand to the component.
 *
 * Three shapes live here, one per mode the workbench ships. The component
 * itself is presentational and owns no state of its kind: every story passes a
 * `ProcedureFixture` in, the component reads its `mode` to choose the panel and
 * the controls, and the live run story wires its own local state on top.
 *
 * The names — *Intake*, *Plan*, *Execute*, *Verify*, *Repair loop*, *Human
 * gate* — are the product vocabulary for the six pipeline shapes. The graph
 * nodes carry the same six words so the connector arrows read as the same
 * plan twice over.
 *
 * The tail of the file (after `INVALID_PROCEDURE`) carries typed React
 * Flow-compatible node data types and four per-channel fixture metadata
 * objects — `STANDARD_FEATURE_PROCEDURE`, `HOTFIX_MANUAL_DEPLOY_GATE_PROCEDURE`,
 * `DOCS_ONLY_NO_DEPLOY_PROCEDURE`, and `INVALID_CYCLE_PROCEDURE` — that the
 * upcoming studio graph renders directly via `<ReactFlow>`.
 */
import type { Edge, Node } from "@xyflow/react"

export const PROCEDURE_NODE_NAMES = [
  "Intake",
  "Plan",
  "Execute",
  "Verify",
  "Repair loop",
  "Human gate",
] as const

export type ProcedureNodeName =
  (typeof PROCEDURE_NODE_NAMES)[number] | "Plan (self)"

/**
 * The three modes the workbench carries. Local to the component — the URL
 * route (when it lands) is the page's job, and this string is the panel's.
 */
export type ProcedureMode = "studio" | "live" | "replay"

/** A pin a node carries. Loop nodes are drawn with a visible boundary. */
export type ProcedureNodeKind = "step" | "loop" | "gate"

/**
 * One node on the procedure graph. The six pipeline shapes get one each.
 *
 * `kind` decides the drawn shape, not just the colour: a `loop` wraps its
 * neighbour in a hairline boundary, a `gate` reads at the end of the chain.
 */
export interface ProcedureNode {
  readonly id: string
  readonly name: ProcedureNodeName
  readonly kind: ProcedureNodeKind
  /**
   * The pin for the linear-fallback row. The semantic graph sits in the
   * centre column on wide screens and falls back to a vertical stack of
   * these nodes on narrow ones.
   */
  readonly col: number
  readonly row: number
}

/**
 * A connector between two nodes. Carries no reading of its own — the style of
 * the connector (active vs idle, planned vs observed) lives on the parent.
 */
export interface ProcedureEdge {
  readonly from: string
  readonly to: string
}

/** Where the procedure came from. The Studio header shows all three. */
export interface ProcedureSource {
  readonly channel: "standard-feature" | "comuki"
  readonly version: string
  readonly gitRef: string
}

/**
 * A stage on the live run path with its own status. The list under the
 * Studio's `Path` and the Live run's `Path` is the same shape — one status
 * per stage, in pipeline order.
 */
export interface ProcedureStageStatus {
  readonly name: ProcedureNodeName
  readonly status: "success" | "running" | "failed" | "waiting"
}

/**
 * A snapshot of a live run. Holds the path, the repair loop's current state,
 * what the human gate is doing, and the evidence tally.
 */
export interface LiveRunFixture {
  readonly taskTitle: string
  readonly pinnedVersion: string
  readonly stages: ReadonlyArray<ProcedureStageStatus>
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

/**
 * A replay observation — one row in the planned-vs-observed timeline.
 * `drift` says how far the actual run diverged from the plan: `none` is a
 * green run, `within-policy` is a yellow one (the harness noticed, but the
 * procedure still finished within its risk budget), and `outside-policy` is a
 * red one that needs a person.
 */
export interface ReplayEvent {
  readonly id: string
  readonly stageName: ProcedureNodeName
  readonly at: string
  readonly planned: string
  readonly observed: string
  readonly drift: "none" | "within-policy" | "outside-policy"
}

/** The Studio mode — what a hand sees when it opens the workbench to plan. */
export interface StudioFixture {
  readonly title: string
  readonly source: ProcedureSource
  readonly nodes: ReadonlyArray<ProcedureNode>
  readonly edges: ReadonlyArray<ProcedureEdge>
  readonly valid?: boolean
}

/** What every workbench mode hands the component. */
export interface ProcedureFixture {
  readonly mode: ProcedureMode
  readonly studio?: StudioFixture
  readonly live?: LiveRunFixture
  readonly replay?: {
    readonly events: ReadonlyArray<ReplayEvent>
    readonly generatedNodes: ReadonlyArray<ProcedureNode>
    readonly generatedEdges: ReadonlyArray<ProcedureEdge>
  }
  /**
   * The React Flow graph the Studio canvas draws, when one exists. Absent on
   * fixtures that predate the canvas or describe an invalid procedure — the
   * layered graph remains the fallback and the narrow-window reading.
   */
  readonly flow?: ProcedureFlowFixture
}

/** The default six nodes — one per pipeline shape, ordered left-to-right. */
export const DEFAULT_PROCEDURE_NODES: ReadonlyArray<ProcedureNode> = [
  { id: "intake", name: "Intake", kind: "step", col: 0, row: 0 },
  { id: "plan", name: "Plan", kind: "step", col: 1, row: 0 },
  { id: "execute", name: "Execute", kind: "step", col: 2, row: 0 },
  { id: "verify", name: "Verify", kind: "step", col: 3, row: 0 },
  { id: "repair", name: "Repair loop", kind: "loop", col: 4, row: 0 },
  { id: "human", name: "Human gate", kind: "gate", col: 5, row: 0 },
]

/**
 * Edges for the default procedure. Execute and Verify fan in to the Repair
 * loop, which sits between Verify and the Human gate. The graph drawn is
 * the one the studio writes by default.
 */
export const DEFAULT_PROCEDURE_EDGES: ReadonlyArray<ProcedureEdge> = [
  { from: "intake", to: "plan" },
  { from: "plan", to: "execute" },
  { from: "execute", to: "verify" },
  { from: "verify", to: "repair" },
  { from: "repair", to: "human" },
]

/**
 * The Studio story — what the operator sees on first open. Header reads
 * `standard-feature / comuki / published v4 / client git source`, the graph
 * is the default six nodes, the inspector under `Verify` shows the
 * evidence, the policy and the human gate threshold.
 */
export const STUDIO_DEFAULT: ProcedureFixture = {
  mode: "studio",
  studio: {
    title: "Standard procedure",
    source: {
      channel: "standard-feature",
      version: "comuki / published v4",
      gitRef: "client git source",
    },
    nodes: DEFAULT_PROCEDURE_NODES,
    edges: DEFAULT_PROCEDURE_EDGES,
  },
}

/**
 * The Live run story — Verify has just failed, the Repair loop is on its
 * first generation and running, the Human gate is waiting for a decision.
 * The attention panel names what needs the operator and the evidence tally
 * is `12 passed / 2 failed`.
 */
export const LIVE_RUN_NEEDS_DECISION: ProcedureFixture = {
  mode: "live",
  live: {
    taskTitle: "Add request tracing",
    pinnedVersion: "standard-feature@v4",
    stages: [
      { name: "Intake", status: "success" },
      { name: "Plan", status: "success" },
      { name: "Execute", status: "success" },
      { name: "Verify", status: "failed" },
      { name: "Repair loop", status: "running" },
      { name: "Human gate", status: "waiting" },
    ],
    repair: {
      generation: 1,
      maxGenerations: 2,
      status: "needs-approval",
    },
    gate: {
      status: "waiting",
      risk: "medium",
    },
    evidence: {
      passed: 12,
      failed: 2,
    },
  },
}

/**
 * The Replay story — a timeline of planned vs observed, with a selected
 * event whose actual run diverged but stayed inside the policy window. The
 * compact generated DAG under the detail is what the brain wrote when it
 * replayed the run.
 */
export const REPLAY_OBSERVED_DRIFT: ProcedureFixture = {
  mode: "replay",
  replay: {
    events: [
      {
        id: "evt-intake",
        stageName: "Intake",
        at: "13:02:11",
        planned: "read ticket, classify standard procedure",
        observed: "read ticket, classify standard procedure",
        drift: "none",
      },
      {
        id: "evt-plan",
        stageName: "Plan",
        at: "13:02:48",
        planned: "decompose into four parallel implementers",
        observed: "decompose into three parallel implementers",
        drift: "within-policy",
      },
      {
        id: "evt-execute",
        stageName: "Execute",
        at: "13:05:22",
        planned: "run three implementers against main",
        observed: "run three implementers against main",
        drift: "none",
      },
      {
        id: "evt-verify",
        stageName: "Verify",
        at: "13:08:09",
        planned: "smoke + contract + visual",
        observed: "smoke + contract (visual skipped: harness timeout)",
        drift: "within-policy",
      },
      {
        id: "evt-repair",
        stageName: "Repair loop",
        at: "13:09:41",
        planned: "rerun verifier on retries budget",
        observed: "rerun verifier on retries budget",
        drift: "none",
      },
      {
        id: "evt-human",
        stageName: "Human gate",
        at: "13:11:02",
        planned: "approve repair scope (medium risk)",
        observed: "approve repair scope (medium risk)",
        drift: "none",
      },
    ],
    generatedNodes: DEFAULT_PROCEDURE_NODES,
    generatedEdges: DEFAULT_PROCEDURE_EDGES,
  },
}

/**
 * A procedure that the workbench refuses to render — used by the Invalid
 * story. The graph has a node that depends on itself, which is the
 * minimum a malformed plan can be without being empty.
 */
export const INVALID_PROCEDURE: ProcedureFixture = {
  mode: "studio",
  studio: {
    title: "Invalid procedure",
    valid: false,
    source: {
      channel: "comuki",
      version: "draft / unpublished",
      gitRef: "local branch",
    },
    nodes: [
      { id: "intake", name: "Intake", kind: "step", col: 0, row: 0 },
      { id: "plan", name: "Plan", kind: "step", col: 1, row: 0 },
      // Plan refers to itself — a cycle the harness can't compile.
      { id: "plan-self", name: "Plan (self)", kind: "step", col: 1, row: 1 },
      { id: "execute", name: "Execute", kind: "step", col: 2, row: 0 },
    ],
    edges: [
      { from: "intake", to: "plan" },
      { from: "plan", to: "plan-self" },
      { from: "plan-self", to: "plan" },
      { from: "execute", to: "intake" },
    ],
  },
}
// ========================================================================
// React Flow-compatible node data types + per-channel fixture metadata.
// ========================================================================

/**
 * The channel a procedure belongs to. Each React Flow graph fixture
 * names its channel; the workbench reads it to colour the channel
 * row in the header and to drive per-channel policies.
 */
export type ProcedureChannel =
  | "standard-feature"
  | "hotfix"
  | "docs-only"
  | "comuki"

/**
 * The parallel lanes an Execute step fans out into. A macro pipeline reads
 * as a graph only when the fan-out and the join are drawn — these are the
 * lane labels the standard procedure's Execute splits into.
 */
export type ProcedureLaneName =
  | "implement api"
  | "implement ui"
  | "implement docs"

/** Anything a flow node may call itself: a macro shape or a parallel lane. */
export type ProcedureFlowLabel = ProcedureNodeName | ProcedureLaneName

/**
 * The data payload each React Flow node carries. `label` is the
 * pipeline shape rendered inside the cell, `kind` decides how the
 * cell is drawn (step / loop / gate), `channel` says which procedure
 * it came from, and `deployGate` flags a human gate as needing a
 * manual deploy, an auto deploy, or no deploy at all.
 */
export type ProcedureFlowNodeData = {
  readonly label: ProcedureFlowLabel
  readonly kind: ProcedureNodeKind
  readonly channel: ProcedureChannel
  readonly deployGate?: "manual" | "auto" | "none"
}

/** A React Flow node carrying {@link ProcedureFlowNodeData}. */
export type ProcedureFlowNode = Node<ProcedureFlowNodeData>

/** A React Flow edge between two procedure nodes. */
export type ProcedureFlowEdge = Edge

/**
 * The reason a graph is invalid. The harness rejects the procedure
 * before the workbench renders it; the fixture carries the reason so
 * the InvalidProcedure story can say what the operator should fix.
 */
export type ProcedureInvalidReason =
  | "cycle"
  | "missing-connector"
  | "orphan-node"

/**
 * A React Flow-compatible procedure graph. The four fixtures below
 * are the canonical shapes every channel ships; `valid: false`
 * surfaces a graph the harness refuses to compile.
 */
export interface ProcedureFlowFixture {
  readonly title: string
  readonly channel: ProcedureChannel
  readonly version: string
  readonly gitRef: string
  readonly nodes: ReadonlyArray<ProcedureFlowNode>
  readonly edges: ReadonlyArray<ProcedureFlowEdge>
  readonly valid: boolean
  readonly invalidReason?: ProcedureInvalidReason
}

/**
 * Resolve a node's React Flow `position` from its col / row layout
 * coordinates. 240px per column, 120px per row — wide enough for the
 * connector arcs to read at the standard zoom.
 */
function flowPosition(col: number, row: number): { x: number; y: number } {
  return { x: col * 240, y: row * 120 }
}

/** The edges of the standard six-node procedure in React Flow shape. */
const STANDARD_FLOW_EDGES: ReadonlyArray<ProcedureFlowEdge> = [
  // Plan fans out into the three parallel implement lanes…
  { id: "e-intake-plan", source: "intake", target: "plan" },
  { id: "e-plan-api", source: "plan", target: "implement-api" },
  { id: "e-plan-ui", source: "plan", target: "implement-ui" },
  { id: "e-plan-docs", source: "plan", target: "implement-docs" },
  // …and Verify is the join they all have to reach.
  { id: "e-api-verify", source: "implement-api", target: "verify" },
  { id: "e-ui-verify", source: "implement-ui", target: "verify" },
  { id: "e-docs-verify", source: "implement-docs", target: "verify" },
  // Verify passes to the human gate…
  { id: "e-verify-human", source: "verify", target: "human" },
  // …and fails into the repair boundary, which retries back into Verify.
  failEdge("e-verify-repair", "verify", "repair"),
  retryEdge("e-repair-verify", "repair", "verify"),
]

/**
 * The edge a failed Verify takes into the repair boundary. Red, because it
 * is the transition the duty engineer scans for.
 */
function failEdge(id: string, source: string, target: string): ProcedureFlowEdge {
  return {
    id,
    source,
    target,
    style: { stroke: "var(--st-failed)" },
  }
}

/**
 * The bounded retry the repair boundary takes back into Verify. Dashed and
 * labelled with its generation cap, because this is the one edge that is a
 * *construct* rather than a flow: the runtime unrolls it into new
 * generations instead of looping.
 */
function retryEdge(
  id: string,
  source: string,
  target: string
): ProcedureFlowEdge {
  return {
    id,
    source,
    target,
    label: "retry ≤ 2 gen",
    style: {
      stroke: "var(--st-waiting)",
      strokeDasharray: "5 4",
    },
  }
}

/**
 * The standard-feature procedure — Plan fans out into three parallel
 * implement lanes, Verify is the join, and the repair boundary reads as
 * the loop it is: a dashed back-edge with its generation cap written on
 * it. This is the graph every feature run rides.
 */
export const STANDARD_FEATURE_PROCEDURE: ProcedureFlowFixture = {
  title: "Standard procedure",
  channel: "standard-feature",
  version: "comuki / published v4",
  gitRef: "client git source",
  valid: true,
  nodes: [
    {
      id: "intake",
      type: "procedure",
      position: flowPosition(0, 1),
      data: { label: "Intake", kind: "step", channel: "standard-feature" },
    },
    {
      id: "plan",
      type: "procedure",
      position: flowPosition(1, 1),
      data: { label: "Plan", kind: "step", channel: "standard-feature" },
    },
    {
      id: "implement-api",
      type: "procedure",
      position: flowPosition(2, 0),
      data: {
        label: "implement api",
        kind: "step",
        channel: "standard-feature",
      },
    },
    {
      id: "implement-ui",
      type: "procedure",
      position: flowPosition(2, 1),
      data: {
        label: "implement ui",
        kind: "step",
        channel: "standard-feature",
      },
    },
    {
      id: "implement-docs",
      type: "procedure",
      position: flowPosition(2, 2),
      data: {
        label: "implement docs",
        kind: "step",
        channel: "standard-feature",
      },
    },
    {
      id: "verify",
      type: "procedure",
      position: flowPosition(3, 1),
      data: { label: "Verify", kind: "step", channel: "standard-feature" },
    },
    {
      id: "repair",
      type: "procedure",
      position: flowPosition(3, 3),
      data: {
        label: "Repair loop",
        kind: "loop",
        channel: "standard-feature",
      },
    },
    {
      id: "human",
      type: "procedure",
      position: flowPosition(4, 1),
      data: {
        label: "Human gate",
        kind: "gate",
        channel: "standard-feature",
        deployGate: "auto",
      },
    },
  ],
  edges: STANDARD_FLOW_EDGES,
}

/**
 * The hotfix procedure — same six nodes as standard-feature, but the
 * human gate is a manual deploy gate. Hotfix runs ship with a person
 * clicking the button, never the harness, because hotfixes are the
 * shape that breaks an auto-deploy policy when it is wrong.
 */
export const HOTFIX_MANUAL_DEPLOY_GATE_PROCEDURE: ProcedureFlowFixture = {
  title: "Hotfix procedure (manual deploy gate)",
  channel: "hotfix",
  version: "comuki / published v4",
  gitRef: "hotfix branch",
  valid: true,
  nodes: [
    {
      id: "intake",
      type: "procedure",
      position: flowPosition(0, 0),
      data: { label: "Intake", kind: "step", channel: "hotfix" },
    },
    {
      id: "plan",
      type: "procedure",
      position: flowPosition(1, 0),
      data: { label: "Plan", kind: "step", channel: "hotfix" },
    },
    {
      id: "execute",
      type: "procedure",
      position: flowPosition(2, 0),
      data: { label: "Execute", kind: "step", channel: "hotfix" },
    },
    {
      id: "verify",
      type: "procedure",
      position: flowPosition(3, 0),
      data: { label: "Verify", kind: "step", channel: "hotfix" },
    },
    {
      id: "repair",
      type: "procedure",
      position: flowPosition(3, 2),
      data: { label: "Repair loop", kind: "loop", channel: "hotfix" },
    },
    {
      id: "human",
      type: "procedure",
      position: flowPosition(4, 0),
      data: {
        label: "Human gate",
        kind: "gate",
        channel: "hotfix",
        deployGate: "manual",
      },
    },
  ],
  edges: [
    { id: "e-intake-plan", source: "intake", target: "plan" },
    { id: "e-plan-execute", source: "plan", target: "execute" },
    { id: "e-execute-verify", source: "execute", target: "verify" },
    { id: "e-verify-human", source: "verify", target: "human" },
    failEdge("e-verify-repair", "verify", "repair"),
    retryEdge("e-repair-verify", "repair", "verify"),
  ],
}

/**
 * The docs-only procedure — Intake / Plan / Verify / Human gate, no
 * Execute, no Repair loop, no deploy at all. The graph still
 * validates; the human gate carries `deployGate: "none"` so the
 * workbench renders the gate without a deploy button.
 */
export const DOCS_ONLY_NO_DEPLOY_PROCEDURE: ProcedureFlowFixture = {
  title: "Docs-only procedure (no deploy)",
  channel: "docs-only",
  version: "comuki / published v4",
  gitRef: "docs branch",
  valid: true,
  nodes: [
    {
      id: "intake",
      type: "procedure",
      position: flowPosition(0, 0),
      data: { label: "Intake", kind: "step", channel: "docs-only" },
    },
    {
      id: "plan",
      type: "procedure",
      position: flowPosition(1, 0),
      data: { label: "Plan", kind: "step", channel: "docs-only" },
    },
    {
      id: "implement-docs",
      type: "procedure",
      position: flowPosition(2, 0),
      data: {
        label: "implement docs",
        kind: "step",
        channel: "docs-only",
      },
    },
    {
      id: "verify",
      type: "procedure",
      position: flowPosition(3, 0),
      data: { label: "Verify", kind: "step", channel: "docs-only" },
    },
    {
      id: "human",
      type: "procedure",
      position: flowPosition(4, 0),
      data: {
        label: "Human gate",
        kind: "gate",
        channel: "docs-only",
        deployGate: "none",
      },
    },
  ],
  edges: [
    { id: "e-intake-plan", source: "intake", target: "plan" },
    { id: "e-plan-docs", source: "plan", target: "implement-docs" },
    { id: "e-docs-verify", source: "implement-docs", target: "verify" },
    { id: "e-verify-human", source: "verify", target: "human" },
  ],
}

/**
 * An invalid-cycle procedure — the graph the harness refuses to
 * compile. `Plan` depends on `Plan (self)` which depends back on
 * `Plan`, the minimum malformed plan. The fixture carries
 * `valid: false` and the reason so the InvalidProcedure story can
 * show the operator what to fix.
 */
export const INVALID_CYCLE_PROCEDURE: ProcedureFlowFixture = {
  title: "Invalid procedure (cycle)",
  channel: "standard-feature",
  version: "draft / unpublished",
  gitRef: "local branch",
  valid: false,
  invalidReason: "cycle",
  nodes: [
    {
      id: "intake",
      type: "procedure",
      position: flowPosition(0, 0),
      data: { label: "Intake", kind: "step", channel: "standard-feature" },
    },
    {
      id: "plan",
      type: "procedure",
      position: flowPosition(1, 0),
      data: { label: "Plan", kind: "step", channel: "standard-feature" },
    },
    {
      id: "plan-self",
      type: "procedure",
      position: flowPosition(1, 1),
      data: {
        label: "Plan (self)",
        kind: "step",
        channel: "standard-feature",
      },
    },
    {
      id: "execute",
      type: "procedure",
      position: flowPosition(2, 0),
      data: { label: "Execute", kind: "step", channel: "standard-feature" },
    },
  ],
  edges: [
    { id: "e-intake-plan", source: "intake", target: "plan" },
    { id: "e-plan-self", source: "plan", target: "plan-self" },
    { id: "e-self-plan", source: "plan-self", target: "plan" },
    { id: "e-execute-intake", source: "execute", target: "intake" },
  ],
}

import { useMemo, useState } from "react"
import {
  AlertOctagon,
  CheckCircle2,
  CircleAlert,
  CircleCheck,
  CircleDashed,
  GitBranch,
  GitCommit,
  GitFork,
  Pause,
  Play,
  RotateCcw,
  ShieldCheck,
  Workflow,
  XCircle,
} from "lucide-react"
import {
  Background,
  Controls,
  Handle,
  Position,
  ReactFlow,
  type Node,
  type NodeProps,
  type NodeTypes,
} from "@xyflow/react"
import "@xyflow/react/dist/style.css"

import { Button, StatusBadge } from "@/shared/ui"
import { cn } from "@/shared/lib/utils"

import type {
  LiveRunFixture,
  PlaybookEdge,
  PlaybookFixture,
  PlaybookMode,
  PlaybookNode,
  PlaybookNodeKind,
  PlaybookStageStatus,
  PlaybookFlowFixture,
  PlaybookFlowNodeData,
  ReplayEvent,
  StudioFixture,
} from "./model/playbook-fixtures"

import styles from "./playbook-workbench.module.css"

/**
 * Playbook Workbench — the operator's view of a playbook in three modes.
 *
 * One screen, three panels, one decision vocabulary. The mode (`studio`,
 * `live`, `replay`) is the panel the workbench is showing; the surrounding
 * chrome — the header, the graph, the inspector — is the same across all
 * three, because the operator does not want to learn two screens to answer
 * one question.
 *
 * The header is the studio's even when the live run is showing, because
 * the run is executing *this* playbook on *this* source. The graph sits in
 * the centre column; the inspector is the right column; on a narrow window
 * the inspector falls below the graph rather than squashing either.
 *
 * State is local. The component owns the active mode (unless one is passed
 * in) and the live run's "approve repair" / "pause run" buttons flip their
 * own state through `useState` so the stories can wire the play callbacks
 * without inventing a global store.
 */
export interface PlaybookWorkbenchProps {
  /** The shape the workbench renders. The mode on the fixture selects the panel. */
  readonly fixture: PlaybookFixture
  readonly flow?: PlaybookFlowFixture
  /**
   * Optional initial mode override. Stories that need a specific mode hand it
   * in; the component otherwise picks the mode the fixture declares.
   */
  readonly initialMode?: PlaybookMode
  /** Names the region for assistive tech. */
  readonly label?: string
  className?: string
}

interface PathRow {
  readonly key: string
  readonly label: string
  readonly status: PlaybookStageStatus["status"] | "queued"
}

/**
 * Resolve the workbench mode. An explicit prop wins, otherwise the fixture
 * declares it. The component never picks — picking is the caller's.
 */
function modeOf(
  fixture: PlaybookFixture,
  initialMode: PlaybookMode | undefined
): PlaybookMode {
  return initialMode ?? fixture.mode
}

/**
 * The header — what the playbook *is*, regardless of mode. Studio and Live
 * read the same three rows, in the same order, in the same voice.
 */
function StudioHeader({ studio }: { studio: StudioFixture }) {
  return (
    <header className={styles.header} data-test="workbench-header">
      <div className={styles.headerTitle}>
        <span className={styles.headerEyebrow}>playbook</span>
        <h2 className={styles.headerName}>{studio.title}</h2>
      </div>
      <dl className={styles.headerFacts}>
        <div className={styles.headerFact}>
          <dt className={styles.factLabel}>channel</dt>
          <dd className={styles.headerFactValue} data-test="workbench-channel">
            <GitBranch aria-hidden="true" />
            {studio.source.channel}
          </dd>
        </div>
        <div className={styles.headerFact}>
          <dt className={styles.factLabel}>version</dt>
          <dd className={styles.headerFactValue} data-test="workbench-version">
            <GitTag aria-hidden="true" />
            {studio.source.version}
          </dd>
        </div>
        <div className={styles.headerFact}>
          <dt className={styles.factLabel}>source</dt>
          <dd className={styles.headerFactValue} data-test="workbench-source">
            <GitFork aria-hidden="true" />
            {studio.source.gitRef}
          </dd>
        </div>
      </dl>
    </header>
  )
}

/**
 * The semantic graph. Six lanes by default; the loop node wraps its lane in
 * a hairline boundary so the boundary is visible at a glance, not inferred.
 * Below a narrow width, the grid collapses into a single column with
 * connectors drawn vertically — the linear fallback.
 */
function PlaybookGraph({
  nodes,
  edges,
  label,
  flow,
  onNodeSelect,
}: {
  nodes: ReadonlyArray<PlaybookNode>
  edges: ReadonlyArray<PlaybookEdge>
  label: string
  flow?: PlaybookFlowFixture
  onNodeSelect?: (nodeId: string) => void
}) {
  // Order columns by their declared col — the story may rearrange them, and
  // the graph draws what the data says, not what the index happens to be.
  const orderedNodes = useMemo(
    () => [...nodes].sort((left, right) => left.col - right.col),
    [nodes]
  )

  // The edges are drawn as a polyline through the centres of the columns.
  // On narrow windows the layout collapses to a single column and the
  // polyline runs top-to-bottom.
  const nodeById = useMemo(() => {
    const map = new Map<string, PlaybookNode>()
    for (const node of orderedNodes) {
      map.set(node.id, node)
    }
    return map
  }, [orderedNodes])

  if (flow) {
    return (
      <FlowCanvas
        flow={flow}
        label={label}
        onNodeSelect={onNodeSelect}
      />
    )
  }

  return (
    <div className={styles.graphFrame} data-test="workbench-graph">
      <div className={styles.graphGrid} role="img" aria-label={label}>
        {orderedNodes.map((node) => (
          <GraphCell key={node.id} node={node} isLoop={node.kind === "loop"} />
        ))}
      </div>
      <svg
        className={styles.graphConnectors}
        viewBox="0 0 100 100"
        preserveAspectRatio="none"
        aria-hidden="true"
      >
        {edges.map((edge, index) => {
          const from = nodeById.get(edge.from)
          const to = nodeById.get(edge.to)
          if (!from || !to) {
            return null
          }
          const x1 = (from.col + 1) * (100 / (orderedNodes.length + 1))
          const x2 = (to.col + 1) * (100 / (orderedNodes.length + 1))
          const y = 50
          return (
            <path
              key={`${edge.from}-${edge.to}-${index}`}
              d={`M ${x1} ${y} C ${(x1 + x2) / 2} ${y}, ${(x1 + x2) / 2} ${y}, ${x2} ${y}`}
              className={styles.graphEdge}
              data-test="workbench-edge"
            />
          )
        })}
      </svg>
    </div>
  )
}

type PlaybookFlowNode = Node<PlaybookFlowNodeData, "playbook">

const flowNodeTypes: NodeTypes = {
  playbook: PlaybookFlowNode,
}

function PlaybookFlowNode({
  data,
  selected,
}: NodeProps<PlaybookFlowNode>) {
  return (
    <div
      className={cn(
        styles.flowNode,
        data.kind === "loop" && styles.flowNodeLoop,
        data.kind === "gate" && styles.flowNodeGate,
        selected && styles.flowNodeSelected
      )}
      data-test="playbook-flow-node"
      data-node-kind={data.kind}
    >
      <Handle type="target" position={Position.Left} className={styles.flowHandle} />
      <span className={styles.flowNodeLabel}>{data.label}</span>
      <span className={styles.flowNodeMeta}>
        {data.kind} · {data.deployGate === "manual" ? "manual deploy" : data.deployGate === "none" ? "no deploy" : data.channel}
      </span>
      <Handle type="source" position={Position.Right} className={styles.flowHandle} />
    </div>
  )
}

function FlowCanvas({
  flow,
  label,
  onNodeSelect,
}: {
  flow: PlaybookFlowFixture
  label: string
  onNodeSelect?: (nodeId: string) => void
}) {
  const [selectedNodeId, setSelectedNodeId] = useState<string | null>(null)
  const nodes = flow.nodes.map((node) => ({
    ...node,
    selected: node.id === selectedNodeId,
  }))
  // React Flow owns a mutable edge array; the fixture is deliberately frozen
  // so a story cannot drift the graph under the canvas by accident.
  const edges = flow.edges.map((edge) => ({ ...edge }))

  return (
    <div className={styles.flowFrame} data-test="playbook-flow-canvas">
      <div className={styles.flowCanvas} aria-label={label}>
        <ReactFlow
          nodes={nodes}
          edges={edges}
          nodeTypes={flowNodeTypes}
          fitView
          fitViewOptions={{ padding: 0.24 }}
          nodesDraggable={false}
          nodesConnectable={false}
          elementsSelectable
          onNodeClick={(_, node) => {
            setSelectedNodeId(node.id)
            onNodeSelect?.(node.id)
          }}
          proOptions={{ hideAttribution: true }}
        >
          <Background color="var(--rule)" gap={24} size={1} />
          <Controls showInteractive={false} />
        </ReactFlow>
      </div>
      <ol className={styles.flowFallback} aria-label={`${label} linear fallback`}>
        {flow.nodes.map((node) => (
          <li key={node.id} className={styles.flowFallbackRow}>
            <span className={styles.flowFallbackIndex}>{node.position.x / 240 + 1}</span>
            <span>{node.data.label}</span>
            <span className={styles.flowFallbackMeta}>{node.data.kind}</span>
          </li>
        ))}
      </ol>
    </div>
  )
}

/**
 * One cell of the graph — a single node. The hairline rule under it carries
 * the connector's reading, and the loop's hairline border wraps the cell
 * when its kind is `loop`.
 */
function GraphCell({ node, isLoop }: { node: PlaybookNode; isLoop: boolean }) {
  return (
    <div
      className={cn(styles.graphCell, isLoop && styles.graphCellLoop)}
      data-test="workbench-node"
      data-node-id={node.id}
      data-node-kind={node.kind}
    >
      <span className={styles.graphCellName}>{node.name}</span>
      <span className={styles.graphCellKind}>{kindLabel(node.kind)}</span>
    </div>
  )
}

/** The little icon beside the header's version fact. Inline so it does not
 *  ship as a separate component for one use. */
function GitTag() {
  return <GitCommit aria-hidden="true" />
}

/** A human-readable name for a node kind. Kept short, two words at most. */
function kindLabel(kind: PlaybookNodeKind): string {
  switch (kind) {
    case "step":
      return "step"
    case "loop":
      return "loop"
    case "gate":
      return "gate"
  }
}

/**
 * The studio inspector — what the operator sees when they have a graph open.
 * Three rows, each answering a question the operator asks before they trust
 * the run: is it pinned? what does it look like when it fails? how big a
 * deal is the human gate?
 */
function StudioInspector({ studio }: { studio: StudioFixture }) {
  return (
    <aside
      className={styles.inspector}
      data-test="workbench-inspector"
      aria-label="Verify inspector"
    >
      <header className={styles.inspectorHead}>
        <span className={styles.inspectorEyebrow}>verify</span>
        <h3 className={styles.inspectorTitle}>Review the plan</h3>
      </header>

      <section className={styles.inspectorSection}>
        <h4 className={styles.inspectorSectionTitle}>evidence</h4>
        <ul className={styles.factList}>
          <li className={styles.factRow} data-test="workbench-evidence">
            <span className={styles.factKey}>test-report</span>
            <span className={styles.factStatusValue}>
              <ShieldCheck aria-hidden="true" />
              on
            </span>
          </li>
        </ul>
      </section>

      <section className={styles.inspectorSection}>
        <h4 className={styles.inspectorSectionTitle}>rules</h4>
        <ul className={styles.factList}>
          <li className={styles.factRow}>
            <span className={styles.factKey}>pass</span>
            <span className={styles.factStatusValue}>Review</span>
          </li>
          <li className={styles.factRow}>
            <span className={styles.factKey}>fail</span>
            <span className={styles.factStatusValue}>Repair loop</span>
          </li>
          <li className={styles.factRow}>
            <span className={styles.factKey}>max generations</span>
            <span className={styles.factStatusValue}>2</span>
          </li>
          <li className={styles.factRow}>
            <span className={styles.factKey}>human approval</span>
            <span className={styles.factStatusValue}>medium risk</span>
          </li>
        </ul>
      </section>

      <section className={styles.inspectorSection}>
        <h4 className={styles.inspectorSectionTitle}>source</h4>
          <p className={styles.inspectorNote}>
            Six pipeline shapes:{" "}
          {studio.nodes.map((node) => node.name).join(", ")}. Connectors are
          pinned at compile time — edges below the source column ride on the
          same loop boundary.
          </p>
          <p className={styles.inspectorNote}>
            Brain proposes a graph patch. The compiler validates schema, cycles,
            capabilities and profiles; a human publishes the immutable compiled
            version. Runtime follows that plan and never asks the model whether
            to execute a node.
          </p>
      </section>

      <ValidationStrip />
    </aside>
  )
}

/**
 * The bottom validation strip — a hairline separator and a small line of
 * text saying the playbook compiles and pins to a known release.
 */
function ValidationStrip() {
  return (
    <div className={styles.validation} data-test="workbench-validation">
      <span className={styles.validationDot} aria-hidden="true" />
      <span className={styles.validationLabel}>
        validates · pinned · no missing connectors
      </span>
    </div>
  )
}

/**
 * The live run inspector — the same shape as the studio inspector, but
 * showing the live state of the run. The attention panel is the headline;
 * the path and evidence are the receipts underneath it.
 */
function LiveRunInspector({
  live,
  onApproveRepair,
  onPauseRun,
  approved,
  paused,
}: {
  live: LiveRunFixture
  onApproveRepair: () => void
  onPauseRun: () => void
  approved: boolean
  paused: boolean
}) {
  return (
    <aside
      className={styles.inspector}
      data-test="workbench-inspector"
      aria-label="Live run inspector"
    >
      <header className={styles.inspectorHead}>
        <span className={styles.inspectorEyebrow}>attention</span>
        <h3 className={styles.inspectorTitle}>what needs me?</h3>
      </header>

      <section
        className={styles.attentionPanel}
        data-test="workbench-attention"
      >
        <p className={styles.attentionLead}>approve repair scope</p>
        <p className={styles.attentionNote}>
          The Repair loop is on generation {live.repair.generation} of{" "}
          {live.repair.maxGenerations}. Verify failed two checks; the loop wants
          to rerun them before handing back to the Human gate.
        </p>

        <dl className={styles.attentionFacts}>
          <div className={styles.attentionFact}>
            <dt className={styles.attentionKey}>evidence</dt>
            <dd
              className={styles.attentionValue}
              data-test="workbench-evidence"
            >
              <span className={styles.attentionPass}>
                {live.evidence.passed} passed
              </span>
              <span className={styles.attentionDivider} aria-hidden="true">
                /
              </span>
              <span className={styles.attentionFail}>
                {live.evidence.failed} failed
              </span>
            </dd>
          </div>
          <div className={styles.attentionFact}>
            <dt className={styles.attentionKey}>risk</dt>
            <dd className={styles.attentionValue}>{live.gate.risk}</dd>
          </div>
        </dl>

        <div className={styles.attentionControls}>
          <Button
            variant="default"
            size="sm"
            data-test="workbench-approve-repair"
            disabled={approved}
            onClick={onApproveRepair}
          >
            <CheckCircle2 aria-hidden="true" />
            {approved ? "Approved" : "Approve repair"}
          </Button>
          <Button
            variant="outline"
            size="sm"
            data-test="workbench-pause-run"
            onClick={onPauseRun}
          >
            {paused ? (
              <>
                <Play aria-hidden="true" />
                Resume
              </>
            ) : (
              <>
                <Pause aria-hidden="true" />
                Pause run
              </>
            )}
          </Button>
        </div>
      </section>

      <section className={styles.inspectorSection}>
        <h4 className={styles.inspectorSectionTitle}>path</h4>
        <ol className={styles.pathList}>
          {pathRows(live.stages).map((row) => (
            <li
              key={row.key}
              className={styles.pathRow}
              data-status={row.status}
              data-test="workbench-path-row"
            >
              <span className={styles.pathDot} aria-hidden="true">
                <PathIcon status={row.status} />
              </span>
              <span className={styles.pathLabel}>{row.label}</span>
              <span className={styles.pathStatus}>
                <StatusBadge status={row.status} size="sm">
                  {row.status}
                </StatusBadge>
              </span>
            </li>
          ))}
        </ol>
      </section>
    </aside>
  )
}

/**
 * The icons the path row uses to say what each stage is doing. The same
 * vocabulary the run page uses — Check, CircleDashed, AlertOctagon, X —
 * because the path is the same five words in both places.
 */
function PathIcon({
  status,
}: {
  status: PlaybookStageStatus["status"] | "queued"
}) {
  switch (status) {
    case "success":
      return <CircleCheck aria-hidden="true" />
    case "running":
      return <RotateCcw aria-hidden="true" />
    case "failed":
      return <XCircle aria-hidden="true" />
    case "waiting":
      return <CircleAlert aria-hidden="true" />
    case "queued":
      return <CircleDashed aria-hidden="true" />
  }
}

/**
 * Build the path rows from a list of stage statuses. The shape stays a
 * `PathRow[]` so the list order is owned by this function and not by the
 * caller.
 */
function pathRows(
  stages: ReadonlyArray<PlaybookStageStatus>
): ReadonlyArray<PathRow> {
  return stages.map((stage, index) => ({
    key: `${stage.name}-${index}`,
    label: stage.name,
    status: stage.status,
  }))
}

/**
 * The replay inspector — timeline on the left, selected event detail on the
 * right, the generated DAG in compact form underneath. The operator scans
 * the timeline for `within-policy` or `outside-policy` markers, clicks one,
 * reads the detail.
 */
function ReplayInspector({
  events,
  selectedEvent,
  generatedNodes,
  generatedEdges,
  onSelectEvent,
}: {
  events: ReadonlyArray<ReplayEvent>
  selectedEvent: ReplayEvent | null
  generatedNodes: ReadonlyArray<PlaybookNode>
  generatedEdges: ReadonlyArray<PlaybookEdge>
  onSelectEvent: (eventId: string) => void
}) {
  return (
    <aside
      className={styles.inspector}
      data-test="workbench-inspector"
      aria-label="Replay inspector"
    >
      <header className={styles.inspectorHead}>
        <span className={styles.inspectorEyebrow}>replay</span>
        <h3 className={styles.inspectorTitle}>planned vs observed</h3>
      </header>

      <section className={styles.inspectorSection}>
        <h4 className={styles.inspectorSectionTitle}>timeline</h4>
        <ol className={styles.timeline}>
          {events.map((event) => {
            const isSelected = selectedEvent?.id === event.id
            return (
              <li key={event.id}>
                <button
                  type="button"
                  className={cn(
                    styles.timelineRow,
                    isSelected && styles.timelineRowSelected
                  )}
                  data-drift={event.drift}
                  data-test="workbench-timeline-row"
                  aria-pressed={isSelected}
                  onClick={() => onSelectEvent(event.id)}
                >
                  <span className={styles.timelineAt}>{event.at}</span>
                  <span className={styles.timelineStage}>
                    {event.stageName}
                  </span>
                  <span
                    className={cn(
                      styles.timelineDrift,
                      event.drift === "within-policy" &&
                        styles.timelineDriftWarning,
                      event.drift === "outside-policy" &&
                        styles.timelineDriftDanger
                    )}
                  >
                    {driftLabel(event.drift)}
                  </span>
                </button>
              </li>
            )
          })}
        </ol>
      </section>

      {selectedEvent ? (
        <section className={styles.inspectorSection}>
          <h4 className={styles.inspectorSectionTitle}>event detail</h4>
          <div
            className={styles.eventDetail}
            data-test="workbench-event-detail"
          >
            <p className={styles.eventStage}>{selectedEvent.stageName}</p>
            <dl className={styles.eventPairs}>
              <div className={styles.eventPair}>
                <dt className={styles.eventKey}>planned</dt>
                <dd className={styles.eventValue}>{selectedEvent.planned}</dd>
              </div>
              <div className={styles.eventPair}>
                <dt className={styles.eventKey}>observed</dt>
                <dd
                  className={cn(
                    styles.eventValue,
                    selectedEvent.drift !== "none" && styles.eventValueDrifted
                  )}
                >
                  {selectedEvent.observed}
                </dd>
              </div>
              <div className={styles.eventPair}>
                <dt className={styles.eventKey}>drift</dt>
                <dd className={styles.eventValue}>
                  {driftLabel(selectedEvent.drift)}
                </dd>
              </div>
            </dl>
            <p className={styles.eventNote}>
              Actual differs but within policy — the harness noticed the branch
              count and the visual skip, and the playbook finished inside its
              risk budget. No replay needed.
            </p>
          </div>

          <div className={styles.generated}>
            <h4 className={styles.inspectorSectionTitle}>generated DAG</h4>
            <PlaybookGraph
              nodes={generatedNodes}
              edges={generatedEdges}
              label="Generated DAG from the replay"
            />
          </div>
        </section>
      ) : null}
    </aside>
  )
}

/** A short, human-readable label for the three drift states. */
function driftLabel(drift: ReplayEvent["drift"]): string {
  switch (drift) {
    case "none":
      return "no drift"
    case "within-policy":
      return "within policy"
    case "outside-policy":
      return "outside policy"
  }
}

/**
 * The workbench. Three modes, one chrome, local state for the controls that
 * change inside a session. The graph is always drawn; the inspector and
 * the controls swap based on the mode.
 */
export function PlaybookWorkbench({
  fixture,
  flow,
  initialMode,
  label = "Playbook workbench",
  className,
}: PlaybookWorkbenchProps) {
  const mode = modeOf(fixture, initialMode)
  const [selectedMode, setSelectedMode] = useState<PlaybookMode>(mode)
  const [approved, setApproved] = useState(false)
  const [paused, setPaused] = useState(false)
  const [selectedEventId, setSelectedEventId] = useState<string | null>(
    fixture.replay?.events[0]?.id ?? null
  )

  // The live run keeps its approve/pause buttons local — the harness is
  // nowhere near wired yet, and a Storybook story is the only consumer
  // today. When the page lands, these become store mutations and the local
  // state goes away.
  const onApproveRepair = () => setApproved(true)
  const onPauseRun = () => setPaused((value) => !value)

  const studio = fixture.studio
  const live = fixture.live
  const replay = fixture.replay
  const selectedEvent =
    replay?.events.find((event) => event.id === selectedEventId) ?? null

  return (
    <section
      className={cn(styles.workbench, className)}
      data-test="playbook-workbench"
      data-mode={selectedMode}
      aria-label={label}
    >
      {studio ? <StudioHeader studio={studio} /> : null}

      <ModeTabs selectedMode={selectedMode} onChange={setSelectedMode} />

      <div className={styles.body}>
        {selectedMode === "studio" && studio ? (
          studio.valid === false ? (
            <InvalidPlaybookNotice reason="The graph contains a cycle. Fix the edge before publishing this playbook." />
          ) : (
            <>
              <PlaybookGraph
                nodes={studio.nodes}
                edges={studio.edges}
                label="Playbook graph"
                flow={flow ?? fixture.flow}
              />
              <StudioInspector studio={studio} />
            </>
          )
        ) : null}

        {selectedMode === "live" && live ? (
          <>
            <LiveRunCenter live={live} paused={paused} />
            <LiveRunInspector
              live={live}
              onApproveRepair={onApproveRepair}
              onPauseRun={onPauseRun}
              approved={approved}
              paused={paused}
            />
          </>
        ) : null}

        {selectedMode === "replay" && replay ? (
          <>
            <ReplayCenter replay={replay} />
            <ReplayInspector
              events={replay.events}
              selectedEvent={selectedEvent}
              generatedNodes={replay.generatedNodes}
              generatedEdges={replay.generatedEdges}
              onSelectEvent={setSelectedEventId}
            />
          </>
        ) : null}
      </div>
    </section>
  )
}

/**
 * The mode strip — semantic tab buttons across the top. The three modes
 * are a closed set; the active one wears the brand rule under it. The
 * buttons are real buttons so the keyboard and the screen reader get the
 * same grammar as the rest of the kit.
 */
function ModeTabs({
  selectedMode,
  onChange,
}: {
  selectedMode: PlaybookMode
  onChange: (next: PlaybookMode) => void
}) {
  return (
    <div className={styles.modeTabs} role="tablist" aria-label="Workbench mode">
      <ModeTab
        id="studio"
        label="Studio"
        icon={<Workflow aria-hidden="true" />}
        selected={selectedMode === "studio"}
        onClick={() => onChange("studio")}
      />
      <ModeTab
        id="live"
        label="Live run"
        icon={<Play aria-hidden="true" />}
        selected={selectedMode === "live"}
        onClick={() => onChange("live")}
      />
      <ModeTab
        id="replay"
        label="Replay"
        icon={<RotateCcw aria-hidden="true" />}
        selected={selectedMode === "replay"}
        onClick={() => onChange("replay")}
      />
    </div>
  )
}

function ModeTab({
  id,
  label,
  icon,
  selected,
  onClick,
}: {
  id: PlaybookMode
  label: string
  icon: React.ReactNode
  selected: boolean
  onClick: () => void
}) {
  return (
    <button
      type="button"
      role="tab"
      aria-selected={selected}
      data-test={`workbench-tab-${id}`}
      className={cn(styles.modeTab, selected && styles.modeTabSelected)}
      onClick={onClick}
    >
      {icon}
      <span>{label}</span>
    </button>
  )
}

/**
 * The live run centre — the task title, the pinned version, and the path
 * that says where the run is standing. The path is the same five rows as
 * the inspector's, in a tighter grid so the operator sees the order at a
 * glance.
 */
function LiveRunCenter({
  live,
  paused,
}: {
  live: LiveRunFixture
  paused: boolean
}) {
  return (
    <div className={styles.liveCenter} data-test="workbench-live-center">
      <header className={styles.liveHead}>
        <span className={styles.liveEyebrow}>task</span>
        <h3 className={styles.liveTitle}>{live.taskTitle}</h3>
        <p className={styles.livePinned}>
          pinned {live.pinnedVersion}
          {paused ? (
            <span className={styles.livePinnedBadge}>
              <Pause aria-hidden="true" />
              paused
            </span>
          ) : null}
        </p>
      </header>
      <ol className={styles.livePath}>
        {pathRows(live.stages).map((row) => (
          <li
            key={row.key}
            className={styles.livePathRow}
            data-status={row.status}
            data-test="workbench-live-path"
          >
            <span className={styles.livePathDot} aria-hidden="true">
              <PathIcon status={row.status} />
            </span>
            <span className={styles.livePathLabel}>{row.label}</span>
          </li>
        ))}
      </ol>
    </div>
  )
}

/**
 * The replay centre — the generated DAG under a header that says what the
 * replay is of. The DAG is the same one the inspector shows in compact
 * form; the centre has more room for the connectors.
 */
function ReplayCenter({
  replay,
}: {
  replay: NonNullable<PlaybookFixture["replay"]>
}) {
  return (
    <div className={styles.replayCenter} data-test="workbench-replay-center">
      <header className={styles.replayHead}>
        <span className={styles.replayEyebrow}>replay</span>
        <h3 className={styles.replayTitle}>run replay</h3>
        <p className={styles.replayNote}>
          {replay.events.length} events · the brain wrote this DAG when it
          replayed the run.
        </p>
      </header>
      <PlaybookGraph
        nodes={replay.generatedNodes}
        edges={replay.generatedEdges}
        label="Generated DAG from the replay"
      />
    </div>
  )
}

/**
 * The invalid studio reading — the workbench refuses to render the graph
 * and says so. Used by the InvalidPlaybook story when the data carries a
 * cycle or a missing connector.
 */
export function InvalidPlaybookNotice({ reason }: { reason: string }) {
  return (
    <div className={styles.invalid} data-test="workbench-invalid">
      <AlertOctagon aria-hidden="true" />
      <div className={styles.invalidBody}>
        <p className={styles.invalidTitle}>invalid playbook</p>
        <p className={styles.invalidNote}>{reason}</p>
      </div>
    </div>
  )
}

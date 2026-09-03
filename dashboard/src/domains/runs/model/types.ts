import type { Status } from "@/shared/ui/status-badge"

export type RunStatus = Status

/**
 * One invocation of a worker profile inside a run's plan.
 *
 * The brain does not pick from a fixed catalog of stages — it emits a graph of
 * profile invocations with dependencies, and it invents the step's name itself.
 * So the two names on this record are not interchangeable:
 *
 * - `profile` is the **identity**. Profiles are a closed, declared catalog
 *   living in the client's git; the brain can only pick from what exists there.
 *   It is the only axis with stable identity across runs, and it is the only
 *   one worth aggregating on — a jammed profile is fixed by editing that
 *   profile in git.
 * - `label` is **prose**. It is whatever the brain decided this step is called
 *   for this ticket, in the product's own language. It rides on the item, it
 *   shows in lists and in the run graph, and it is never aggregated: free text
 *   from a language model produces forty columns that are really six things.
 */
export interface WorkItem {
  /** Unique inside its run. Dependencies and `RunSummary.current` point at it. */
  id: string
  /** Catalog key of the worker profile that runs this item. The identity. */
  profile: string
  /** The brain's own name for this step. A label, never a key. */
  label: string
  status: RunStatus
  /** Ids of items in the same run that must finish before this one starts. */
  dependsOn: string[]
  cost?: number
  tokens?: number
  /** Wall-clock start, `HH:MM` inside the run. Absent while queued. */
  startedAt?: string
}

export interface RunSummary {
  id: string
  /**
   * The project this run belongs to, by id.
   *
   * A project is an attribute of the row, not a mode the screen is in — the
   * duty engineer watches the whole swarm at once, so every list mixes them.
   * The consequence lives in the actions column: permission is resolved per
   * row against *this* id, so the same person can approve the row above and
   * be refused on the row below, with the refusal naming the project.
   */
  projectId: string
  app: string
  title: string
  status: RunStatus
  /** Id of the work item the run is standing on right now. */
  current: string
  model: "worker" | "lead"
  cost: number
  tokens: number
  durationSec: number
  done: boolean
  /** The plan: an arbitrary graph, not a fixed pipeline. */
  workItems: WorkItem[]
}

export interface DiffLine {
  kind: "ctx" | "add" | "del"
  line: string
  text: string
}

export interface DiffFile {
  path: string
  added: number
  deleted: number
  lines: DiffLine[]
}

export interface TraceEvent {
  time: string
  status: RunStatus
  text: string
}

export interface GateCheck {
  name: string
  status: RunStatus
}

export interface WorkItemInspector {
  role: "worker" | "lead" | "judge"
  env: string
  tokens: string
  cost: string
  inputs: Array<{ icon: string; label: string; detail?: string }>
  outputs: Array<{ icon: string; label: string; detail?: string }>
  files: DiffFile[] | null
  gate: GateCheck[] | null
  events: TraceEvent[]
}

export interface RunDetail extends RunSummary {
  brief: string
  rules: string[]
  revision: { rules: string; sdk: string }
  events: TraceEvent[]
}

/**
 * One object inside a run's artifact bundle. The host writes immutable
 * artifact pointers to MinIO once the run terminates; the FE renders them as
 * named links the operator clicks to fetch the bundle over its signed URI.
 *
 * The wire shape (`ArtifactPointer`) carries `uri: string`, but the domain
 * normalises it to `URL` so the screen can `.href` it without re-parsing.
 */
export interface ArtifactPointer {
  name: string
  uri: URL
  size: number
  contentType: string
}

/**
 * One page of run-artifact pointers — what the host returns for one run.
 * The list is empty when the run has not been packaged yet (still in flight,
 * or the packager has not yet observed the terminal transition).
 *
 * `projectId` / `runId` are echoed from the path because the wire page
 * carries them; the screen keeps them on the domain object so callers do not
 * have to thread the URL params through alongside the body.
 */
export interface RunArtifacts {
  projectId: string
  runId: string
  /** Bundle objects — empty when the run has not been packaged yet. */
  items: ArtifactPointer[]
}

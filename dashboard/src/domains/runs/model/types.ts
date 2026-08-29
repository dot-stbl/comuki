import type { Status } from "@/shared/ui/status-badge"

export type RunStatus = Status

export interface RunStage {
  key: string
  label: string
  status: RunStatus
  lane?: "a" | "b"
}

export interface RunSummary {
  id: string
  app: string
  title: string
  status: RunStatus
  current: string
  model: "worker" | "lead"
  cost: number
  tokens: number
  durationSec: number
  done: boolean
  stages: RunStage[]
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

export interface StageInspector {
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

export type RunStatusFilter = "all" | RunStatus

export interface RunsFilter {
  query: string
  app: string
  status: RunStatusFilter
}

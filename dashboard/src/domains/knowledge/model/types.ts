export type KnowledgeKind = "rule" | "doc" | "skill"
export type RuleKind = "hard" | "soft"
export type EvalResult = "pass" | "fail"
export type EvalDelta = "+" | "-" | "="

export interface KnowledgeEntry {
  id: string
  kind: KnowledgeKind
  title: string
  scope: string
  ruleKind?: RuleKind
  revision: string
  pinned: boolean
  summary: string
  body: string
  updated: string
}

export interface EvalCase {
  task: string
  before: EvalResult
  after: EvalResult
  delta: EvalDelta
}

export interface KnowledgeRevision {
  rules: string
  sdk: string
  updated: string
}

export interface KnowledgeSnapshot {
  revision: KnowledgeRevision
  rulesActive: number
  rulesHard: number
  rulesSoft: number
  entries: KnowledgeEntry[]
  eval: EvalCase[]
}

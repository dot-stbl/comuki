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
  /**
   * The pinned rule set the mock library describes. `null` in real mode: the
   * host's knowledge surface serves *documents* (ingested chunks), not the
   * control-plane rule set, and a revision that does not exist must not be
   * invented to fill the reading.
   */
  revision: KnowledgeRevision | null
  /** Rule counts; `null` with the revision they belong to. */
  rulesActive: number | null
  rulesHard: number | null
  rulesSoft: number | null
  entries: KnowledgeEntry[]
  /**
   * The golden-task harness. Empty in real mode — there is no eval feed on
   * the wire — and the page hides the section rather than drawing an empty
   * before/after table.
   */
  eval: EvalCase[]
}

/** One search hit off `GET /api/v1/knowledge/search`, as the page reads it. */
export interface KnowledgeHit {
  documentId: string
  chunkId: string
  snippet: string
  /** Cosine similarity in [0, 1] — higher is closer. */
  score: number
}

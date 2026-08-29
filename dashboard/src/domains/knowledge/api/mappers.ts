import type {
  EvalCase,
  KnowledgeEntry,
  KnowledgeSnapshot,
} from "@/domains/knowledge/model/types"
import type {
  SeedEvalCase,
  SeedKnowledgeEntry,
  SeedKnowledgeSnapshot,
} from "@/shared/api/mock/knowledge.seed"

export function toKnowledgeEntry(seed: SeedKnowledgeEntry): KnowledgeEntry {
  return {
    id: seed.id,
    kind: seed.kind,
    title: seed.title,
    scope: seed.scope,
    ruleKind: seed.ruleKind,
    revision: seed.revision,
    pinned: seed.pinned,
    summary: seed.summary,
    body: seed.body,
    updated: seed.updated,
  }
}

export function toEvalCase(seed: SeedEvalCase): EvalCase {
  return {
    task: seed.task,
    before: seed.before,
    after: seed.after,
    delta: seed.delta,
  }
}

export function toKnowledgeSnapshot(
  seed: SeedKnowledgeSnapshot
): KnowledgeSnapshot {
  return {
    revision: { ...seed.revision },
    rulesActive: seed.rulesActive,
    rulesHard: seed.rulesHard,
    rulesSoft: seed.rulesSoft,
    entries: seed.entries.map(toKnowledgeEntry),
    eval: seed.eval.map(toEvalCase),
  }
}

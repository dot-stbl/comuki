import type {
  EvalCase,
  KnowledgeEntry,
  KnowledgeHit,
  KnowledgeSnapshot,
} from "@/domains/knowledge/model/types"
import type {
  SeedEvalCase,
  SeedKnowledgeEntry,
  SeedKnowledgeSnapshot,
} from "@/shared/api/mock/knowledge.seed"
import type { KnowledgeDocumentsPage } from "@/shared/api/_generated/types/KnowledgeDocumentsPage"
import type { KnowledgeDocumentSummary } from "@/shared/api/_generated/types/KnowledgeDocumentSummary"
import type { KnowledgeSearchHitView } from "@/shared/api/_generated/types/KnowledgeSearchHitView"

/* Two sources, one domain shape. The mock path maps the control-plane seed
   (rules, revisions, the golden-task harness). The wire path maps the host's
   *documents* surface — ingested chunks with counts — onto the same entry
   rows the library already draws, degrading the fields a document does not
   carry. */

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

/* ------------------------------------------------------------------ *
 * The wire — `GET /api/v1/knowledge/documents` and `…/search`.
 *
 * The spec declares the response schemas now, so the wire shapes are the
 * kubb-generated `KnowledgeDocumentsPage` / `KnowledgeDocumentSummary` /
 * `KnowledgeSearchHitView` (the host's views, camelCased by the
 * serializer). Counters arrive as `number | string` and scores as
 * `number | string` per the spec's reading-from-string allowance; the
 * mappers normalise at this edge.
 * ------------------------------------------------------------------ */

/** An ISO instant as the row's "updated" word — date, not clock time. */
function toUpdated(iso: string): string {
  const parsed = Date.parse(iso)
  return Number.isNaN(parsed)
    ? "—"
    : new Date(parsed).toISOString().slice(0, 10)
}

/**
 * A document onto the library's entry row.
 *
 * A document carries no rule kind, no pin and no body text on this surface —
 * the row's marks degrade to none, the summary states what *is* known (the
 * source, the shape, the chunk and token counts), and the sheet's body says
 * where the text lives rather than inventing prose.
 */
export function knowledgeDocumentToEntry(
  doc: KnowledgeDocumentSummary
): KnowledgeEntry {
  return {
    id: doc.id,
    // Every document the ingest surface accepts lands as a doc row; the rule
    // and skill kinds are the control-plane vocabulary and never arrive here.
    kind: "doc",
    title: doc.title,
    // The wire marks a global-corpus document with a null project; the
    // library's scope column needs a word, and "global" is the host's own.
    scope: doc.projectId ?? "global",
    ruleKind: undefined,
    revision: doc.sourceRef,
    pinned: false,
    summary: `${doc.source} · ${doc.mimeType} · ${Number(doc.chunkCount)} chunks · ${Number(doc.tokenCount)} tokens`,
    body: `Full text is not served by the documents API — open the source at ${doc.sourceRef}.`,
    updated: toUpdated(doc.createdAt),
  }
}

/** A documents page onto the real-mode snapshot: entries, nothing else. */
export function knowledgeDocumentsToSnapshot(
  page: KnowledgeDocumentsPage
): KnowledgeSnapshot {
  return {
    revision: null,
    rulesActive: null,
    rulesHard: null,
    rulesSoft: null,
    entries: page.items.map(knowledgeDocumentToEntry),
    eval: [],
  }
}

/** A wire hit onto the page's hit shape. */
export function knowledgeHitWireToHit(
  hit: KnowledgeSearchHitView
): KnowledgeHit {
  return {
    documentId: hit.documentId,
    chunkId: hit.chunkId,
    snippet: hit.snippet,
    score: Number(hit.score),
  }
}

/**
 * A search hit onto the library's entry row — every field a real value from
 * the wire: the snippet is the summary (and the sheet's body), the score is
 * the figure, the ids are the meta. Nothing here is invented; nothing the
 * wire carries is dropped.
 */
export function knowledgeHitToEntry(hit: KnowledgeHit): KnowledgeEntry {
  return {
    id: hit.chunkId,
    kind: "doc",
    title: `match ${Math.round(hit.score * 100)}%`,
    scope: hit.documentId,
    ruleKind: undefined,
    revision: hit.chunkId,
    pinned: false,
    summary: hit.snippet,
    body: hit.snippet,
    updated: "—",
  }
}

## Purpose

Extends the Knowledge capability (defined in the separately-tracked `add-knowledge-spec` change, issue #160) with a Mission-artefact-derived **Wiki**: a self-maintaining set of structured pages with a link graph, sourced from worker outputs across Missions. The Wiki lives in the `knowledge` capability because it is a source-owned, revisioned document/chunk corpus — the same substrate as the rest of Knowledge. The first iteration ingests worker outputs as Knowledge sources (via the existing `knowledge.ingest` surface); follow-on iterations add purpose-built wiki pages and a link-graph adjacency list.

This file is the umbrella delta that will merge with the `add-knowledge-spec` delta at archive time; the canonical main spec lives in `openspec/specs/knowledge/spec.md` after both deltas are applied. The shared Wiki-vs-rest-of-Knowledge shape lives here; the rest of Knowledge's requirements stay in `add-knowledge-spec`.

## ADDED Requirements

### Requirement: Wiki as a Knowledge corpus
A Project MAY maintain a Wiki derived from Mission artefacts. The Wiki is a Knowledge sub-corpus: same `SourceDocument` / `MemoryEmbedding` / pgvector surface, distinguished by `SourceKind = wiki` and a new `WikiPage` subtype that carries, in addition to the standard source-document fields, a stable `WikiPageId` (UUIDv7), an optional `WikiPageKind` (`glossary` | `how-to` | `decision-record` | `incident` | `reference`), an `UpdatedByMissionId` reference (the Mission whose worker produced the latest edit), and a `LinkGraph` adjacency list of `(targetWikiPageId, kind)` pairs (`see-also` | `supersedes` | `derived-from`).

The Wiki SHALL be ingested through the same `knowledge.ingest` REST/MCP surface as every other Knowledge source — a wiki page is a `SourceDocument` with `SourceKind = wiki`. The Body MUST be markdown with the existing frontmatter subset (`title`, `kind`, `link_graph`, `version`, `supersedes`); chunks SHALL be paragraph-aware with the same `Knowledge:Ingest:ChunkTokenTarget` as the rest of Knowledge.

The Wiki is private to its Project by default (Project-scoped `SourceDocument`, no global corpus exposure). A wiki page MAY be promoted to the global corpus only via the existing knowledge-global write scope (i.e. an unrestricted caller).

#### Scenario: Worker output ingested as a wiki page
- **WHEN** a worker in Mission `M` of Project `P` calls `knowledge.ingest` with `source=wiki`, `sourceRef=mission:<MId>/output:<fingerprint>`, body carrying a markdown document and frontmatter `kind: decision-record`
- **THEN** the Wiki gains one new `SourceDocument` row under Project `P`; its `WikiPageKind = decision-record`; `LinkGraph` carries whatever edges the frontmatter declared; the worker returns the resulting `sourceDocumentId` to the platform for the Mission scope (per `memory` capability's "Mission-scoped memory facts" requirement, where the resulting `wiki-page-id` becomes a Mission-scoped memory fact so other workers can find it).

#### Scenario: Wiki page references another wiki page
- **WHEN** an ingested wiki page declares `link_graph: [[page:abc, see-also]]`
- **THEN** the link-graph adjacency list records `(thisPage, abc, see-also)` and downstream link-graph queries can return transitive adjacency (e.g. "what does page X reach in ≤ 2 hops?") without re-parsing the body.

#### Scenario: Wiki page supersedes older wiki page
- **WHEN** a wiki page declares `supersedes: page:<oldId>` and `link_graph: [[page:<oldId>, supersedes]]`
- **THEN** the older page becomes `stale-but-auditable` per the existing revision/generation rule (its chunks drop out of retrieval; its row survives for provenance). The new page becomes the active generation; the Brain sees the supersedes edge when it cites either side.

### Requirement: Wiki generation from Mission outputs
The platform SHALL expose a Wiki generation capability that turns a Mission's worker outputs into candidate wiki pages. Generation is **opt-in** (per Project policy `wiki_generation_enabled`) and **advisory** (Brain proposes a set of candidate pages with citations to the source outputs; humans / owners approve or reject). Generation does NOT auto-write wiki pages; it produces a `WikiGenerationProposal` that names each candidate page, its `WikiPageKind`, its body draft, and its link-graph candidate.

#### Scenario: Wiki generation proposal created
- **WHEN** Mission `M` in Project `P` ends with `wiki_generation_enabled = true` and at least one worker output is suitable
- **THEN** a `WikiGenerationProposal` exists for `M`, listing candidate pages with kind, body draft, link-graph candidates, and citations to the originating worker outputs; the proposal is visible to Mission owners via the existing attention-inbox surface.

#### Scenario: Owner rejects all candidates
- **WHEN** Mission owners reject the entire `WikiGenerationProposal`
- **THEN** no `SourceDocument` row is written; the proposal carries the rejection reason; Mission completion proceeds unchanged; no Wiki state changes.

#### Scenario: Owner accepts one page
- **WHEN** Mission owners approve a subset of the `WikiGenerationProposal`
- **THEN** only the approved candidates become `SourceDocument` rows (via `knowledge.ingest` with `source=wiki`); rejected candidates are recorded in the proposal's audit; Mission completion proceeds.

### Requirement: Wiki retrieval
A `Wiki`-scoped retrieval query (a Context Fabric `SourceRef` of kind `knowledge-wiki` against a Project) SHALL match the rest of the Knowledge plane — same visibility-before-retrieval, same hybrid RRF pipeline (per `context-fabric`'s "Hybrid retrieval" requirement, since the Wiki is built on the same `MemoryEmbedding` table), same generation pinning. The Wiki's link-graph adjacency is exposed as an additional Context Fabric operation (`WikiLinkGraph`) for queries of the form "what does page P reference?" or "what references page P?".

#### Scenario: Brain consults the Wiki
- **WHEN** the planner compiles a Context Pack for a Mission and the Project has Wiki pages
- **THEN** the Wiki source is queried alongside the rest of Knowledge; the resulting pages appear with their citations and link-graph adjacency visible to the Brain.

#### Scenario: Link-graph traversal
- **WHEN** the Brain asks "what does wiki page `<id>` link to?"
- **THEN** the response is the adjacency list of that page (forward edges by default; reverse adjacency available via a parameter) without re-parsing page bodies. The response carries the same freshness/generation fields as the rest of Knowledge retrieval.
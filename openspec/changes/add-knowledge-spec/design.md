## Context

The Knowledge module landed in S10 (`Comuki.Modules.Knowledge.{Domain,
Application,Infrastructure}` + host composition in `Comuki.Host/Knowledge`
and `Comuki.Host/Mcp`). It exposes a paragraph-aware chunker, a swappable
embedding client (`noop` default / `openai` opt-in / `voyage` reserved),
pgvector cosine similarity search, REST endpoints under
`/api/v1/knowledge/*`, and two MCP tools (`knowledge.search`,
`knowledge.ingest`). The corpus is project-scoped or global (null
`projectId`), gated by the same subject-scope filter every other read
path uses.

None of this has an OpenSpec capability entry under `openspec/specs/`.
`add-mission-cowork/specs/context-fabric/spec.md`'s "Knowledge and
procedural sources" requirement assumes exactly such a capability exists
and contracts against it — stable source key, immutable revision hash,
active generation, provenance on search hits. The user-facing capability
table in `openspec/README.md` lists 19 capabilities; `knowledge` is not
one of them. This change fixes the omission.

## Goals / Non-Goals

**Goals:**

- Backfill the existing shipped Knowledge behavior into a `knowledge`
  capability spec, faithful to the code and the existing tests, naming
  each requirement's status as **existing, verified**.
- Name the contract `add-context-fabric` (#96) needs (stable source key,
  immutable revision hash, active generation, provenance on search hits)
  as **planned, not yet implemented** requirements in the same spec,
  with the implementation deferred to a follow-up change.
- Surface the concrete gap between the `IKnowledgeIngestor` XML docstring
  (claims supersede-by-`(projectId, source, sourceRef)`) and the actual
  implementation (inserts a new row on every call) so the gap can be
  resolved in one place later. This gap is the literal content of
  decomposition.md's Open Question 1.
- Stay docs-only: no `platform/` or `tests/` code changes, no migrations,
  no `comuki.slnx` edits.

**Non-Goals (design-level):**

- Implementing stable source key, content hash, revision, generation,
  or provenance on search hits. Spec only — a follow-up change owns the
  code, schema, and migration work.
- Changing the MCP tool surface. `knowledge.search` and `knowledge.ingest`
  are wired in `McpServer`'s dispatcher switch today; the spec describes
  them as-is.
- Adding a `KnowledgeSource` table or turning the doc worker into a
  dispatcher. The spec records today's heartbeat-only behavior as fact.
- A pgvector → Qdrant swap or any other vector-store evaluation. The
  pgvector `embedding` column on `knowledge.memory_embeddings` is the
  contract; the spec describes the column as out-of-model raw SQL, same
  pattern as `memory_facts.embedding`.
- Enforcing the `knowledge:admin` permission. Declared in code, granted
  to `PlatformAdmin` in `RoleMatrix`, never checked anywhere — documented
  as a reserved/unused key, not a behavioral requirement.

## Decisions

### 1. Backfill vs. fresh author — backfill, faithful to the code

**Choice:** the 13 existing-behavior requirements describe what the code
does today, verified file by file. The XML docstrings in
`IKnowledgeIngestor.cs` and `KnowledgeIngestResult.cs` were read for
context but NOT treated as authoritative when they disagree with the
implementation (see decision 2). Test files were read for cross-check,
but not invented when they don't cover a path.

**Why:** OpenSpec specs describe user/API-visible behavior and are the
contract the platform publishes. A backfilled spec that copies aspirational
prose into `SHALL/MUST` would publish a contract the platform does not
honor, and any downstream change (e.g. `add-context-fabric`) reading
from that contract would silently inherit a lie. The cost of being
faithful here is one extra decision (decision 2) and one extra set of
"planned, not yet implemented" requirements (decisions 3–5).

### 2. The supersede gap is named as a planned requirement, not as fact

**Choice:** the backfilled ingestion requirement describes what
`PgKnowledgeIngestor.IngestAsync` actually does — every call creates an
independent `SourceDocument` row plus its chunk set, no
lookup-by-`(projectId, source, sourceRef)`, no delete/replace of prior
chunks, no unique index to make supersede possible. The
revision/generation behavior instead becomes one of the planned
requirements that `add-context-fabric`'s "Reingested document" scenario
assumes.

**Why — the concrete gap:**

- `IKnowledgeIngestor.cs` (lines 7–9) XML doc: *"The ingestion is
  idempotent at the `SourceDocument` level: re-ingesting the same
  (`projectId`, `source`, `sourceRef`) supersedes the previous row and
  replaces its chunks."*
- `KnowledgeIngestResult.cs` (line 6) XML doc: *"`(new or superseded)`"*.
- `PgKnowledgeIngestor.cs` (lines 87–89): `SourceDocument.Create(...)`
  then `context.SourceDocuments.Add(document); await
  context.SaveChangesAsync(...)`. There is no SELECT-before-INSERT, no
  DELETE, no UPDATE on prior chunks, no transaction wrapping a "find
  prior and replace" sequence.
- `SourceDocumentConfiguration.cs` (lines 52–53): the only index is
  `ix_source_documents_project_created` on `(ProjectId, CreatedAt)`.
  There is no unique index over `(project_id, source, source_ref)` that
  would make a `WHERE` for an existing row cheap, and no DB-level
  constraint that would make supersede possible even if the code asked.
- `tests/unit/.../PgKnowledgeIngestorShould.cs` and
  `tests/integration/.../PgKnowledgeSearcherShould.cs` have no test
  exercising reingest-supersedes-prior-chunks — the gap is unobserved
  by the test suite today.

The platform's "sourceable, version-aware, provenance-friendly" knowledge
contract that `add-context-fabric` reads from is therefore exactly this
gap. The follow-up change (or `add-context-fabric` itself) must add: a
stable source key, a content hash, a `(project_id, source_key)`
unique index (or equivalent lookup), revision/generation tracking on
reingest, and provenance on search hits.

**Rejected:** treating the docstring as authoritative. That would publish
a contract the implementation does not honor and would let any future
reader believe `IngestAsync` is idempotent when it is not.

### 3. Today's architecture is documented, not redesigned

**Choice:** the spec describes the shipped architecture (chunk-2-embed
through `PgKnowledgeIngestor`, search through `PgKnowledgeSearcher`,
embedding client selectable via `Knowledge:Embedding:Provider`,
raw-SQL pgvector column outside the EF model, one-schema-per-DbContext,
heartbeat-only doc worker) as the contract. It does not propose
replacing the embedding provider, swapping the vector store, or
re-shaping the search API.

**Why:** `add-knowledge-spec` is a backfill change. Architectural
decisions are owned by the change that introduces them; this one only
records what's there.

**Rejected:** proposing a "v2" ingestion pipeline in the same spec.
That would conflate docs with design and obscure the gap the follow-up
must close.

### 4. The three planned requirements live alongside the existing ones

**Choice:** the spec carries 13 `### Requirement:` blocks for current
behavior and 3 `### Requirement:` blocks for the planned behavior. Both
prefixed with a `Status:` line on the requirement body. Scenarios for
the planned requirements still use RFC `SHALL` wording (a spec states
the contract) but the `Status:` line makes unambiguous that the
implementation is missing.

**Why:** `add-context-fabric` (#96) reads the spec at author time and
needs to see the *target contract*, not just the *current behavior*.
Splitting into two specs would force #96 to either re-stamp the planned
requirements or read from a spec that doesn't exist yet. Keeping them
together means the contract is published once, with the
implemented/unimplemented boundary explicit per requirement.

**Rejected:** a separate `add-knowledge-revisions` change authored before
this one. The user's decision was to land *this* spec first; the
implementation follows in a follow-up. Authoring the implementation
first would invert the dependency.

### 5. The unused `knowledge:admin` key is documented as fact, not a requirement

**Choice:** the spec notes in prose that `knowledge:admin` exists in
the permission catalog and is granted to `PlatformAdmin` only, but is
not enforced by any endpoint or MCP tool today. There is no
`### Requirement:` block with `WHEN/THEN` scenarios for it.

**Why:** a spec that invents behavior for an unused key would publish a
contract nobody honors. A spec that ignores the key entirely would
forget that it was declared and assigned — a future change could
silently double-grant or revoke it without a spec trail. Documenting
as fact keeps the audit trail without inventing behavior.

### 6. What stays out of this change (defer-and-name, not defer-and-forget)

The 3 planned requirements (stable source key + content hash, revision
tracking, provenance on search hits) are the named gap that this change
exists to publish. The implementation of those requirements is deferred
to either:

- a dedicated `add-knowledge-revisions` change (preferred — a single
  reviewable slice that lands schema, ingest path, search result type,
  and tests), or
- folded into `add-context-fabric` (#96)'s own change as a dependency.

Which path the follow-up takes is an Open Question (see below); this
spec is neutral on it.

## Today's architecture (cross-reference for the spec)

- **Module layout:** `Comuki.Modules.Knowledge.{Domain, Application,
  Infrastructure}` — Domain owns `SourceDocument`, `MemoryEmbedding`,
  `SourceKind`, `EmbeddingProviderKind`. Application owns the port
  interfaces (`IKnowledgeIngestor`, `IKnowledgeSearcher`,
  `IEmbeddingClient`, `IKnowledgeDocumentReader`). Infrastructure owns
  the EF Core (`KnowledgeDbContext`, `SourceDocumentConfiguration`,
  `MemoryEmbeddingConfiguration`, `KnowledgeDatabase`), the Postgres
  implementations (`PgKnowledgeIngestor`, `PgKnowledgeSearcher`,
  `PgKnowledgeDocumentReader`), the chunker, the embedding clients
  (`NoopEmbeddingClient`, `OpenAIEmbeddingClient`), and the hosted
  service (`KnowledgeIngestBackgroundService`).
- **Embedding client:** `IEmbeddingClient` returns a fixed-length
  `float[]` (default 1536, matches `EmbeddingSql.Dimensions`).
  Selectable via `Knowledge:Embedding:Provider` = `noop` (default,
  deterministic, no API key) / `openai` (requires
  `Knowledge:Embedding:ApiKeyEnvRef`, throws `InvalidOperationException`
  at registration if unset) / `voyage` (declared, throws
  `NotSupportedException` — reserved, unimplemented).
- **REST endpoints:** `POST /api/v1/knowledge/ingest` (`knowledge:write`),
  `GET /api/v1/knowledge/documents` (`knowledge:read`), `GET
  /api/v1/knowledge/search` (`knowledge:read`). Page size for documents
  clamped to `[1, 100]`; `topK` for search clamped to `[1, 1000]`;
  `minSimilarity` for search clamped to `[0.0, 1.0]`.
- **MCP tools:** dispatcher exposes `knowledge.search` and
  `knowledge.ingest` (exact wire names — NOT `search_knowledge`; this
  was verified against `McpServer.cs`'s switch on lines 140–141 and
  `McpToolCatalog.cs`'s `tools` array on lines 25, 42). Gated by
  `McpToolPermissionMap`: missing catalog entry or unauthenticated
  subject = deny.
- **Worker caller scoping:** `knowledge.search` from a worker caller
  NEVER accepts a client-supplied `projectId` — the project is forced
  from the worker's lease. A worker with no active lease gets
  `IsError=true` text result; no search runs.
- **Subject caller scoping:** a subject may pass `projectId` explicitly
  or omit it (omit falls under the read scope rules: an unrestricted
  caller sees everything; a restricted caller sees the global corpus
  plus its assigned projects; a projectId outside scope returns empty,
  never 403/404, to avoid confirming project existence).
- **Doc worker heartbeat:** `KnowledgeIngestBackgroundService` polls on
  `Knowledge:Ingest:PollIntervalSeconds` (default 60, floored to 1s
  even if configured ≤0), logs start/stop, a tick that throws is caught
  and logged and does not stop the loop. Today's tick body is a heartbeat
  only — no `KnowledgeSource` table exists yet, so no automatic ingest
  dispatch happens. The class's own TODO comment (`// TODO(worker-registry)`)
  flags this as the dispatcher boundary for a future change.
- **Storage layout:** Postgres schema `knowledge`, tables
  `source_documents` and `memory_embeddings`, plus their own
  `__ef_migrations_history`. The pgvector `embedding` column is
  raw-SQL-managed and lives outside the EF model — same separation as
  `memory_facts.embedding`. `memory_embeddings` carries a unique index
  `(source_document_id, chunk_index)`.

## Open Questions

- **Who implements the 3 planned requirements — a dedicated
  `add-knowledge-revisions` change, or absorbed into
  `add-context-fabric` (#96)?** A dedicated change is cleaner (one
  reviewable slice: schema migration, ingest path update, search hit
  type update, tests). Absorbing into #96 couples #96's review with the
  schema work, which may or may not be desired. This spec is neutral;
  the decision is owned by the change that actually authors the
  implementation. Mirror of decomposition.md's own open-question style.
- **`KnowledgeSource` table for the doc worker.** When it lands (a
  follow-up to this spec, separate from the revision/generation work),
  `KnowledgeIngestBackgroundService` becomes the dispatcher. That change
  is also out of scope here; this spec records today's heartbeat-only
  behavior.

## Risks / Trade-offs

- **[Backfill locks in current behavior]** — once this spec is synced
  into `openspec/specs/knowledge/spec.md`, any change to ingestion,
  search, or MCP behavior must go through an OpenSpec change, not a
  silent edit. This is the desired posture, but worth flagging: the
  13 backfilled requirements are now load-bearing.
- **[Planned requirements as `SHALL/MUST` create a published contract
  the code does not honor]** — mitigated by the per-requirement `Status:
  planned` line. `add-context-fabric` (#96) must read the status, not
  just the wording.
- **[One schema, one DbContext, but pgvector lives outside EF]** — the
  spec calls out the `embedding` column as raw-SQL-managed. A future
  EF-pgvector provider adoption is a separate decision.

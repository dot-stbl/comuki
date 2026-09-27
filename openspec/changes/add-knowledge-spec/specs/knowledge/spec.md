## Purpose

Source-owned document/chunk corpus for the Comuki platform. Each source
document is registered with an origin kind (`git` | `upload` | `url`), an
origin pointer, a MIME type, and an optional project scope (null = global
corpus). Ingestion paragraph-splits the text, embeds each chunk through a
swappable embedding provider (default `noop`, opt-in `openai`, reserved
`voyage`), and persists one row per chunk to the `knowledge.memory_embeddings`
table with a pgvector cosine-similarity index. Search returns the closest
chunks under the caller's subject scope. The corpus is exposed via a REST
surface (`/api/v1/knowledge/*`) and two MCP tools (`knowledge.search`,
`knowledge.ingest`). A doc-worker heartbeat runs in the background but
does not yet dispatch real ingest work — that awaits a `KnowledgeSource`
table in a later change.

## ADDED Requirements

### Requirement: Source document aggregate

**Status: existing, verified against `Comuki.Modules.Knowledge.Domain/SourceDocument.cs`, `SourceKind.cs`, `SourceKindKeys.cs`.**

A source document SHALL belong to exactly one project or to the global
corpus (a `SourceDocument.ProjectId` value of `null`). Each document SHALL
carry: a `Title` (human-readable, non-empty after trim), a `Source` of
kind `git` | `upload` | `url` (wire keys `SourceKindKeys.Git` |
`SourceKindKeys.Upload` | `SourceKindKeys.Url`), a non-empty
`SourceRef` (origin pointer — git URL+ref, upload blob id, or fetched URL),
a non-empty `MimeType` (normalized to lower-case on persist), and a
`CreatedAt` timestamp. The `Title`, `SourceRef`, and `MimeType` properties
SHALL each be required and SHALL be rejected as `ArgumentException` if
empty or whitespace; the document SHALL be persisted only after all
required fields validate.

#### Scenario: Create source document

- **WHEN** `SourceDocument.Create(projectId, title, source, sourceRef, mimeType, now)` is called with non-empty `title`, `sourceRef`, and `mimeType`
- **THEN** a `SourceDocument` is returned with a fresh `Id` (UUIDv7), `ProjectId` set to the argument (which may be null), trimmed title, trimmed sourceRef, lower-cased mimeType, and `CreatedAt` set to the supplied `now`.

#### Scenario: Empty title rejected

- **WHEN** `SourceDocument.Create` is called with a whitespace `title`
- **THEN** the call throws `ArgumentException("title must not be empty", "title")` and no document is persisted.

#### Scenario: Global corpus is a null projectId

- **WHEN** a caller registers a document with `projectId = null`
- **THEN** the persisted row's `project_id` column is SQL `NULL`, and the document belongs to the global corpus (visible to every subject under the read scope rules; writable only by an unrestricted caller).

### Requirement: Paragraph-aware chunking

**Status: existing, verified against `Comuki.Modules.Knowledge.Infrastructure/Chunking/Chunker.cs`, `Configuration/KnowledgeIngestOptions.cs`, `tests/unit/Comuki.Modules.Knowledge.Unit/ChunkerShould.cs`.**

The chunker SHALL split the input text on double-newline paragraph
boundaries (`\n\n`, with `\r\n` normalized to `\n`) and SHALL pack
consecutive paragraphs into chunks up to a target token estimate
(`Knowledge:Ingest:ChunkTokenTarget`, default 500, range `[64, 8192]`).
An over-target paragraph (whose own estimated token count exceeds
`targetTokens`) SHALL become its own chunk — paragraphs SHALL NEVER be
split mid-paragraph. The token estimate SHALL be computed as
`Math.Ceiling(whitespaceSeparatedWordCount / 0.75)`, where
`TokensPerWord = 0.75`. An empty or whitespace-only text SHALL yield an
empty chunk list. `targetTokens <= 0` SHALL throw
`ArgumentOutOfRangeException`.

#### Scenario: Pack paragraphs up to target

- **WHEN** `Chunker.Chunk(text, 500)` is called with three short paragraphs separated by blank lines whose combined estimated tokens are ≤ 500
- **THEN** the returned list contains a single chunk equal to the three paragraphs re-joined by blank lines.

#### Scenario: Over-target paragraph stays whole

- **WHEN** `Chunker.Chunk(text, 500)` is called with one paragraph whose estimated tokens exceed 500 and no preceding packed paragraphs
- **THEN** the list contains one chunk equal to that single paragraph; the chunker does not split it mid-sentence.

#### Scenario: Empty text yields no chunks

- **WHEN** `Chunker.Chunk("", 500)` or `Chunker.Chunk("   ", 500)` is called
- **THEN** the returned list is empty.

### Requirement: Ingestion writes chunks and embeddings (independent rows per call)

**Status: existing, verified against `Comuki.Modules.Knowledge.Infrastructure/Persistence/PgKnowledgeIngestor.cs`, `Domain/MemoryEmbedding.cs`, `Infrastructure/Persistence/Configurations/SourceDocumentConfiguration.cs`, `tests/unit/Comuki.Modules.Knowledge.Unit/PgKnowledgeIngestorShould.cs`. Note: this requirement describes ACTUAL behavior, not the supersede claim in `IKnowledgeIngestor.cs`'s XML doc.**

A successful `IKnowledgeIngestor.IngestAsync` call SHALL write ONE
`SourceDocument` row and, when the chunker yields at least one chunk,
one `MemoryEmbedding` row per chunk in the same transaction. Each call
SHALL create an independent, permanent `SourceDocument` — there is no
lookup-by-`(projectId, source, sourceRef)`, no delete of prior chunks,
no supersede of prior rows. The `MemoryEmbedding` row carries the chunk
text, zero-based chunk index within the source, the chunk's estimated
token count, and the document's `ProjectId`. The pgvector `embedding`
column SHALL be back-filled per row via raw SQL using the embedder's
vector output; when the `knowledge.memory_embeddings.embedding` column
is absent on the deployment, the vector UPDATE SHALL be skipped
gracefully and the rows SHALL persist without vectors. When the embedder
returns a vector count that differs from the chunk count, the call SHALL
throw `InvalidOperationException` and abort. Zero-chunk text (no
paragraphs after blank-line splitting) SHALL still commit the
`SourceDocument` row with `ChunksWritten = 0`.

#### Scenario: Successful ingest writes chunks

- **WHEN** `IngestAsync` is called with text that the chunker splits into N paragraphs and `pgvector.embedding` is present
- **THEN** the call returns `KnowledgeIngestResult(documentId, ChunksWritten: N)`, exactly N `memory_embeddings` rows exist linked to that `SourceDocument`, and N raw-SQL UPDATEs have written vectors into the `embedding` column.

#### Scenario: Empty text commits a document with zero chunks

- **WHEN** `IngestAsync` is called with empty or whitespace-only text
- **THEN** the call returns `KnowledgeIngestResult(documentId, ChunksWritten: 0)`, one `SourceDocument` row exists, and zero `memory_embeddings` rows exist for it.

#### Scenario: Embedding count mismatch aborts

- **WHEN** the embedder returns a vector count that differs from the chunk count
- **THEN** the call throws `InvalidOperationException` describing the mismatch, and the transaction is rolled back (no `SourceDocument` row, no `memory_embeddings` rows).

#### Scenario: pgvector column absent — vectors skipped, rows persist

- **WHEN** the `knowledge.memory_embeddings.embedding` column does not exist on the connected database
- **THEN** the call returns successfully with `ChunksWritten` equal to the chunk count, the `SourceDocument` and `memory_embeddings` rows are committed, and a structured log entry notes that vectors were not persisted.

#### Scenario: Re-ingesting the same origin is NOT a supersede

- **WHEN** `IngestAsync` is called twice with the same `(projectId, source, sourceRef)` but different text
- **THEN** both calls succeed; two distinct `SourceDocument` rows exist (each with a fresh `Id` and `CreatedAt`), and each row carries its own `memory_embeddings` set. The first document's chunks are NOT replaced, marked stale, or otherwise distinguished from the second's. (This is the gap the planned `Revision and generation tracking` requirement names.)

### Requirement: Project and global write scope

**Status: existing, verified against `PgKnowledgeIngestor.IsProjectWritable`, `tests/unit/.../PgKnowledgeIngestorShould.cs` (`RefuseIngestForProjectOutsideScopeAsync`, `RefuseIngestForGlobalCorpusWhenRestrictedAsync`, `AllowUnrestrictedCallerAnyProjectAsync`).**

`IngestAsync` SHALL refuse writes outside the caller's scope with a
loud error, mapping to HTTP 403 (not a silent no-op). An unrestricted
caller (platform-scope role or system consumer) MAY write to any
project OR the global corpus. A restricted caller MAY write only to
projects it is assigned to and SHALL NEVER write to the global corpus.
A write whose target is outside the caller's scope SHALL throw
`ProviderForbiddenException` with code `knowledge.project_out_of_scope`
and SHALL NOT persist any `SourceDocument` row.

#### Scenario: Unrestricted caller writes any project

- **WHEN** an unrestricted subject calls `IngestAsync` with a `projectId` they are not assigned to
- **THEN** the call succeeds; a `SourceDocument` row is written under that `projectId`.

#### Scenario: Unrestricted caller writes global corpus

- **WHEN** an unrestricted subject calls `IngestAsync` with `projectId = null`
- **THEN** the call succeeds; a `SourceDocument` row is written with `project_id = NULL`.

#### Scenario: Restricted caller refused outside its scope

- **WHEN** a restricted subject calls `IngestAsync` with a `projectId` that is not in its assigned project set
- **THEN** the call throws `ProviderForbiddenException` with code `knowledge.project_out_of_scope` and a message naming the rejected target; no `SourceDocument` row is written.

#### Scenario: Restricted caller refused global corpus

- **WHEN** a restricted subject calls `IngestAsync` with `projectId = null`
- **THEN** the call throws `ProviderForbiddenException` with code `knowledge.project_out_of_scope` and the message "the current subject may not ingest a global (cross-project) knowledge document"; no `SourceDocument` row is written.

### Requirement: pgvector cosine similarity search

**Status: existing, verified against `Infrastructure/Persistence/PgKnowledgeSearcher.cs`, `Persistence/Stores/EmbeddingSql.cs`, `tests/unit/.../PgKnowledgeSearcherShould.cs`, `tests/integration/.../PgKnowledgeSearcherShould.cs`.**

`IKnowledgeSearcher.SearchAsync` SHALL embed the query through the
configured `IEmbeddingClient`, issue a raw-SQL cosine-distance SELECT
against `knowledge.memory_embeddings.embedding`, and return the top
hits as `KnowledgeSearchHit` records. The hit list SHALL be ordered by
cosine similarity descending. The `topK` argument SHALL be validated
to lie in `[1, 1000]` and SHALL throw `ArgumentOutOfRangeException`
otherwise. The `minSimilarity` argument SHALL be validated to lie in
`[0.0, 1.0]` and SHALL throw `ArgumentOutOfRangeException` otherwise.
A blank `query` SHALL throw `InvalidOperationException("query required")`
before any database touch. When the `embedding` column is absent on the
connected database, the search SHALL return an empty list (graceful
degradation, not an error).

#### Scenario: Returns cosine-similarity-ordered hits

- **WHEN** `SearchAsync(query, projectId, topK, minSimilarity)` is called with valid arguments and the `embedding` column is present
- **THEN** the returned hits are ordered by similarity descending, each `KnowledgeSearchHit` carries `{ ChunkId, SourceDocumentId, ChunkText, Similarity }`, and at most `topK` hits are returned (subject to the `minSimilarity` floor).

#### Scenario: Blank query rejected before DB touch

- **WHEN** `SearchAsync("", null, 8, 0.2f)` is called
- **THEN** the call throws `InvalidOperationException("query required")` and no SQL is executed.

#### Scenario: topK out of range rejected

- **WHEN** `SearchAsync` is called with `topK = 0` or `topK = 1001`
- **THEN** the call throws `ArgumentOutOfRangeException(nameof(topK), …, "topK must be in [1, 1000]")`.

#### Scenario: minSimilarity out of range rejected

- **WHEN** `SearchAsync` is called with `minSimilarity = -0.1f` or `minSimilarity = 1.1f`
- **THEN** the call throws `ArgumentOutOfRangeException(nameof(minSimilarity), …, "minSimilarity must be in [0.0, 1.0]")`.

#### Scenario: pgvector column absent — empty list

- **WHEN** the `embedding` column does not exist on the connected database
- **THEN** the call returns an empty list and a structured log entry notes that pgvector is absent; no exception is thrown.

### Requirement: Project and global read scope

**Status: existing, verified against `PgKnowledgeSearcher.SearchAsync`'s scope clause and `EmbeddingSql.CosineSearchSql`'s `Visibility` + `Narrowing` clauses, plus `tests/integration/.../PgKnowledgeSearcherShould.cs` (`ExcludeAnotherProjectsChunkFromScopedSearchAsync`).**

Search SHALL apply the caller's subject scope as a SQL filter (visibility
clause), independent of the caller-supplied `projectId` argument
(narrowing clause). A `projectId` argument that names a project outside
the caller's scope SHALL return an empty list (NEVER 403/404, to avoid
confirming project existence). An unrestricted caller SHALL search any
project and the global corpus. A restricted caller with no explicit
`projectId` SHALL see the global corpus plus its own assigned projects,
NEVER another project's chunks regardless of similarity score.

#### Scenario: Unrestricted caller searches any project

- **WHEN** an unrestricted subject calls `SearchAsync` with any `projectId`
- **THEN** the result includes every chunk in that project (subject to `minSimilarity` and `topK`).

#### Scenario: Restricted caller scoped to global + own projects

- **WHEN** a restricted subject calls `SearchAsync` with `projectId = null`
- **THEN** the result includes only chunks whose `project_id` is NULL or is in the caller's assigned project set.

#### Scenario: Project outside scope returns empty (not 403)

- **WHEN** a restricted subject calls `SearchAsync` with a `projectId` they are not assigned to
- **THEN** the result is an empty list and a structured log entry notes the out-of-scope project; no 403/404 is returned.

#### Scenario: Higher-similarity chunk in another project is excluded

- **WHEN** two projects both contain a chunk with similarity above `minSimilarity` and only one is in the caller's scope
- **THEN** the in-scope project's chunk is in the result; the out-of-scope project's chunk is NOT, regardless of similarity score.

### Requirement: Embedding provider selection

**Status: existing, verified against `Configuration/KnowledgeEmbeddingOptions.cs`, `KnowledgeInfrastructureExtensions.AddKnowledgeEmbeddingClient`, `Embeddings/NoopEmbeddingClient.cs`, `tests/unit/.../KnowledgeOptionsShould.cs`, `NoopEmbeddingClientShould.cs`, `OpenAIEmbeddingClientShould.cs`.**

The embedding provider SHALL be selected via
`Knowledge:Embedding:Provider`. Three values SHALL be supported:

- `noop` — default. Deterministic random `float[]` vectors, no network
  call, no API key. Used in dev, tests, and any deployment that does
  not require real embeddings.
- `openai` — uses `Knowledge:Embedding:ApiKeyEnvRef` to read the API
  key from the named env var (never the key itself). If
  `Provider = openai` and `ApiKeyEnvRef` is unset / resolves to a
  blank key, the host registration SHALL throw
  `InvalidOperationException` and refuse to start.
- `voyage` — declared, but `NotSupportedException("Knowledge:Embedding:Provider=voyage is reserved — no embedder is shipped yet; switch to openai or noop.")` is thrown at resolution time. Reserved for a future embedder; no behavior today.

The provider SHALL produce fixed-length `float[]` vectors whose length
equals `Knowledge:Embedding:Dimensions` (default 1536, matching
`EmbeddingSql.Dimensions`). The embedder SHALL honor
`Knowledge:Embedding:BatchSize` (default 32, range `[1, 256]`) when
batching multiple chunks in one round-trip.

#### Scenario: noop is the default

- **WHEN** the host starts with `Knowledge:Embedding` absent from configuration
- **THEN** the registered `IEmbeddingClient` is `NoopEmbeddingClient(Dimensions = 1536)` and no API key is read.

#### Scenario: openai without API key env ref fails startup

- **WHEN** the host starts with `Provider = openai` and `ApiKeyEnvRef` unset
- **THEN** `ValidateOnStart` throws or the registration factory throws `InvalidOperationException` ("Knowledge:Embedding:Provider=openai requires Knowledge:Embedding:ApiKeyEnvRef to name an env var with the key."); the host refuses to start.

#### Scenario: voyage is reserved

- **WHEN** the host starts with `Provider = voyage`
- **THEN** resolving `IEmbeddingClient` throws `NotSupportedException` ("Knowledge:Embedding:Provider=voyage is reserved — no embedder is shipped yet; switch to openai or noop.").

### Requirement: Document listing

**Status: existing, verified against `Application/Documents/IKnowledgeDocumentReader.cs`, `Application/Documents/KnowledgeDocumentsPage.cs`, `Infrastructure/Persistence/PgKnowledgeDocumentReader.cs`, `tests/unit/.../KnowledgeDocumentsPagingShould.cs`.**

`IKnowledgeDocumentReader.ListAsync(projectId, page, pageSize)` SHALL
return a `KnowledgeDocumentsPage` of `KnowledgeDocumentSummary` rows
ordered newest-first (`CreatedAt DESC`, tie-broken by `Id DESC`),
together with the 1-based `page`, the `pageSize` actually used, and a
`total` count matching the filter. The subject-scope query filter of
`KnowledgeDbContext` SHALL apply — out-of-scope documents are absent
from both items and total. Each row SHALL carry the document's `Id`,
`ProjectId`, `Title`, `Source` (wire key `git` | `upload` | `url`),
`SourceRef`, `MimeType`, the chunk count (joined aggregate over
`memory_embeddings`), the token total across chunks (joined aggregate),
and `CreatedAt`. `page` SHALL be normalized to `>= 1`; `pageSize`
SHALL be clamped to `[1, 100]`. When `projectId` is supplied, only
documents in that project SHALL be listed.

#### Scenario: Newest-first page

- **WHEN** the caller asks for `page = 1, pageSize = 25` with no `projectId` filter
- **THEN** the returned items are ordered by `CreatedAt DESC` (then `Id DESC`), the `total` is the total visible-to-caller document count, and `page` / `pageSize` in the response echo the normalized values.

#### Scenario: Out-of-scope documents excluded

- **WHEN** a restricted subject with one assigned project calls `ListAsync(projectId = null, …)`
- **THEN** the items include only documents whose `project_id` is NULL (global corpus) or matches the caller's assigned project; `total` reflects the same count.

#### Scenario: pageSize clamped

- **WHEN** the caller asks for `pageSize = 0`, `pageSize = -5`, or `pageSize = 1000`
- **THEN** the response uses a `pageSize` clamped to the nearest bound in `[1, 100]` (0/-5 → 1; 1000 → 100).

### Requirement: REST surface

**Status: existing, verified against `Comuki.Host/Knowledge/KnowledgeModuleEndpoints.cs`, `Comuki.Host/ApiRoutes.cs`, `tests/integration/.../KnowledgeShould.cs`.**

The knowledge module SHALL expose three REST endpoints under
`/api/v1/knowledge`:

- `POST /api/v1/knowledge/ingest` — body `KnowledgeIngestRequest`
  (projectId optional, title required, source required, sourceRef
  required, mimeType required, text required). Permission
  `knowledge:write`. Returns 200 `KnowledgeIngestResponse`
  `{ sourceDocumentId, chunksWritten }`.
- `GET /api/v1/knowledge/documents` — query params `projectId` (Guid,
  optional), `page` (int, default 1), `pageSize` (int, default 25).
  Permission `knowledge:read`. Returns 200 `KnowledgeDocumentsPage`.
- `GET /api/v1/knowledge/search` — query params `q` (string, required),
  `projectId` (Guid, optional), `topK` (int, default 8), `minSimilarity`
  (float, default 0.2). Permission `knowledge:read`. Returns 200
  `KnowledgeSearchResponse { items: [{ documentId, chunkId, snippet,
  score }] }`.

The search endpoint SHALL return 400 with code `knowledge.query_required`
when `q` is missing or blank; 400 with code
`knowledge.top_k_out_of_range` when `topK` is outside `[1, 1000]`; 400
with code `knowledge.min_similarity_out_of_range` when `minSimilarity` is
outside `[0.0, 1.0]`.

#### Scenario: Ingest returns the new document id

- **WHEN** `POST /api/v1/knowledge/ingest` is called with a valid body by a subject holding `knowledge:write`
- **THEN** the response is 200 `{"sourceDocumentId": "...", "chunksWritten": N}`, where N is the chunk count produced by the chunker.

#### Scenario: Search requires a non-blank query

- **WHEN** `GET /api/v1/knowledge/search` is called without `q` or with `q=` blank
- **THEN** the response is 400 ProblemDetails with `code = "knowledge.query_required"`.

#### Scenario: Search validates topK

- **WHEN** `GET /api/v1/knowledge/search?…&topK=0` (or `topK=1001`) is called
- **THEN** the response is 400 ProblemDetails with `code = "knowledge.top_k_out_of_range"`.

#### Scenario: Search validates minSimilarity

- **WHEN** `GET /api/v1/knowledge/search?…&minSimilarity=-0.1` (or `minSimilarity=1.1`) is called
- **THEN** the response is 400 ProblemDetails with `code = "knowledge.min_similarity_out_of_range"`.

### Requirement: MCP tools

**Status: existing, verified against `Comuki.Host/Mcp/McpToolCatalog.cs`, `Comuki.Host/Mcp/McpToolHandlers.cs` (`KnowledgeSearchAsync`, `KnowledgeIngestAsync`, `McpKnowledgeSearch`), `Comuki.Host/Mcp/McpToolPermissionMap.cs`, `Comuki.Host/Mcp/McpServer.cs` (dispatch switch on lines 140–141).**

The MCP dispatcher SHALL expose two tools named `knowledge.search` and
`knowledge.ingest` (exact wire names — NOT `search_knowledge`). Each
tool's invocation SHALL be gated by `McpToolPermissionMap`: a missing
catalog entry SHALL deny, an unauthenticated subject SHALL deny, and a
subject without the required permission SHALL deny with the JSON-RPC
error code surfaced as `permission.denied`.

`knowledge.search` SHALL accept arguments `query` (required), `projectId`
(optional), `topK` (int, default 5), `minSimilarity` (float, default
0.5). A worker caller SHALL NEVER accept a client-supplied `projectId`;
the project SHALL be forced from the worker's active work-item lease.
A worker caller with no active project SHALL receive a tool result with
`IsError=true` and text content "knowledge.search is unavailable: no
active work item, no project scope."; no search runs. A subject caller
MAY pass `projectId` explicitly or omit it; omission falls under the
read scope rules.

`knowledge.ingest` SHALL accept arguments `title` (required), `source`
(required, wire key), `sourceRef` (required), `mimeType` (required),
`text` (required), `projectId` (optional). Missing or whitespace
required arguments SHALL return a JSON-RPC error with
`InvalidParams`. On success the tool result payload SHALL carry
`{ sourceDocumentId, chunksWritten }`.

#### Scenario: Worker caller cannot pass projectId

- **WHEN** a worker caller invokes `knowledge.search` with `projectId = "<some-guid>"` in the arguments
- **THEN** the dispatcher ignores the supplied `projectId`, forces the project from the worker's lease, and runs the search against that project (or returns `IsError=true` text result when no lease exists).

#### Scenario: Worker with no active project cannot search

- **WHEN** a worker caller without an active work-item lease invokes `knowledge.search`
- **THEN** the tool result has `IsError=true`, content text "knowledge.search is unavailable: no active work item, no project scope.", and no `IKnowledgeSearcher.SearchAsync` call is made.

#### Scenario: Subject caller passes explicit projectId

- **WHEN** a subject caller invokes `knowledge.search` with `projectId = "<some-guid>"`
- **THEN** the dispatcher passes that `projectId` to `IKnowledgeSearcher.SearchAsync`, which enforces the read scope rules (out-of-scope `projectId` returns empty, in-scope returns hits).

#### Scenario: Subject caller omits projectId

- **WHEN** a subject caller invokes `knowledge.search` without `projectId`
- **THEN** the dispatcher passes `projectId = null` to `IKnowledgeSearcher.SearchAsync`; an unrestricted subject sees the global corpus plus every project, a restricted subject sees the global corpus plus their assigned projects.

#### Scenario: Tool name dispatch

- **WHEN** the JSON-RPC dispatcher switch receives a `tools/call` with `name = "knowledge.search"` or `name = "knowledge.ingest"`
- **THEN** it routes to `McpToolHandlers.KnowledgeSearchAsync` or `McpToolHandlers.KnowledgeIngestAsync` respectively; any other tool name does NOT match these branches.

#### Scenario: Missing catalog entry denies

- **WHEN** the dispatcher receives `tools/call` for a tool name not present in `McpToolPermissionMap`
- **THEN** the call is denied (no handler is invoked).

### Requirement: Permissions

**Status: existing, verified against `Comuki.Modules.Identity.Domain/Permissions/Permissions.cs` and `Roles/RoleMatrix.cs`. The grants are an explicit per-role list — not a seniority threshold — because `Approver` (seniority 40) sits between `Member` (30) and `ProjectAdmin` (60) yet holds neither `knowledge:read` nor `knowledge:write`.**

The knowledge module SHALL gate every read and write surface behind
two permission keys: `knowledge:read` (list, search, REST GET, MCP
`knowledge.search`) and `knowledge:write` (ingest, REST POST, MCP
`knowledge.ingest`). The role matrix SHALL grant `knowledge:read` to
exactly five roles: `PlatformAdmin`, `Operator`, `ProjectAdmin`,
`Member`, and `Viewer` (the `Approver` role SHALL NOT receive
`knowledge:read`). The role matrix SHALL grant `knowledge:write` to
exactly three roles: `PlatformAdmin`, `Operator`, and `ProjectAdmin`
(neither `Member` nor `Approver` SHALL receive `knowledge:write`).
A third key `knowledge:admin` SHALL exist in the permission catalog
and SHALL be granted to `PlatformAdmin` only; as of this spec, no
endpoint or MCP tool checks it. `knowledge:admin` is recorded here as
a documented fact — an unused/reserved key, NOT a behavioral
requirement with `WHEN/THEN` scenarios.

#### Scenario: knowledge:read granted to the five roles

- **WHEN** a subject holding `PlatformAdmin`, `Operator`, `ProjectAdmin`, `Member`, or `Viewer` invokes a `knowledge:read`-gated endpoint or MCP tool
- **THEN** access is granted.

#### Scenario: knowledge:read denied to Approver

- **WHEN** a subject holding the `Approver` role invokes a `knowledge:read`-gated endpoint or MCP tool
- **THEN** access is denied; the read surface is not entered. (Approver sits at seniority 40, above `Member` (30) and below `ProjectAdmin` (60), yet holds neither knowledge permission — the grant is an explicit role list, not a seniority threshold.)

#### Scenario: knowledge:write granted to the three roles

- **WHEN** a subject holding `PlatformAdmin`, `Operator`, or `ProjectAdmin` invokes a `knowledge:write`-gated endpoint or MCP tool
- **THEN** access is granted.

#### Scenario: knowledge:write denied to Viewer

- **WHEN** a subject holding the `Viewer` role invokes a `knowledge:write`-gated endpoint or MCP tool
- **THEN** access is denied; the write surface is not entered.

#### Scenario: knowledge:write denied to Member

- **WHEN** a subject holding the `Member` role invokes a `knowledge:write`-gated endpoint or MCP tool
- **THEN** access is denied; the write surface is not entered. (`Member` holds `knowledge:read` but not `knowledge:write` — the two grants are independent and cannot be inferred from each other or from seniority.)

#### Scenario: knowledge:write denied to Approver

- **WHEN** a subject holding the `Approver` role invokes a `knowledge:write`-gated endpoint or MCP tool
- **THEN** access is denied; the write surface is not entered.

#### Scenario: knowledge:admin is granted to PlatformAdmin only

- **WHEN** a subject holding the `PlatformAdmin` role is checked against `knowledge:admin`
- **THEN** the check returns true. Every other role returns false.

#### Scenario: knowledge:admin is not currently checked

- **WHEN** any knowledge REST endpoint or MCP tool resolves a permission check
- **THEN** the check is for `knowledge:read` or `knowledge:write` — not `knowledge:admin`. (`knowledge:admin` exists in the catalog and is granted to `PlatformAdmin`, but no surface in the platform reads it today.)

### Requirement: Doc worker heartbeat

**Status: existing, verified against `Infrastructure/Hosted/KnowledgeIngestBackgroundService.cs` and `tests/unit/.../KnowledgeIngestBackgroundServiceShould.cs`.**

A host hosted service SHALL poll the knowledge corpus on
`Knowledge:Ingest:PollIntervalSeconds` (default 60, range `[1, 86400]`,
floored to `>= 1s` even if configured at or below zero). The service
SHALL log start and stop. Each tick that throws SHALL be caught and
logged as an error, the loop SHALL NOT stop, and the next interval
SHALL be honored. Today's tick body SHALL be a heartbeat only — the
class logs `"knowledge doc worker heartbeat (no sources yet)"` and
returns `Task.CompletedTask`. No `KnowledgeSource` table exists yet,
so the loop SHALL NOT dispatch any real ingest work. The
`// TODO(worker-registry)` comment in the source file names the
future dispatcher boundary.

#### Scenario: Heartbeat-only loop

- **WHEN** the host starts and `KnowledgeIngestBackgroundService` runs for two poll intervals with no other activity
- **THEN** the service logs "knowledge doc worker started (interval Xs)", then per-tick logs "knowledge doc worker heartbeat (no sources yet)", and the loop continues; no `IKnowledgeIngestor.IngestAsync` call is made.

#### Scenario: Tick exception does not stop the loop

- **WHEN** a tick body throws
- **THEN** the exception is logged at error level as "knowledge doc worker tick failed; continuing", the loop does NOT exit, and the next interval's tick is attempted.

#### Scenario: Interval floored to 1 second

- **WHEN** `Knowledge:Ingest:PollIntervalSeconds` is configured as 0 or negative
- **THEN** the effective interval is `TimeSpan.FromSeconds(1)` (the floored value, not the configured value), and the host's `ValidateOnStart` annotation `[Range(1, 86_400)]` rejects the configuration if it is negative.

### Requirement: Persistence layout

**Status: existing, verified against `Infrastructure/Persistence/KnowledgeDatabase.cs`, `Infrastructure/Persistence/Configurations/MemoryEmbeddingConfiguration.cs`, `Migrations/`.**

The knowledge module SHALL persist its data in Postgres schema
`knowledge`. The schema SHALL contain the tables `source_documents` and
`memory_embeddings`, plus its own `__ef_migrations_history` (one
migrations history per DbContext). The `memory_embeddings` table SHALL
have a unique index `(source_document_id, chunk_index)` (named
`ix_memory_embeddings_source_chunk`). The pgvector `embedding` column
on `memory_embeddings` SHALL be created by the module's migration (out
of band of the EF model, conditionally — the column exists only when
the pgvector extension is present) and SHALL be written and queried
exclusively through raw SQL (`EmbeddingSql`). The module's migrator
loop SHALL apply this schema in the existing
`EnsureSchema → MigrateAsync` flow.

#### Scenario: Schema applied through shared migrator

- **WHEN** the migrator runs with the `knowledge` schema registered
- **THEN** schema `knowledge` and tables `source_documents` and `memory_embeddings` exist, plus `knowledge.__ef_migrations_history`, and the unique index `ix_memory_embeddings_source_chunk` is in place.

#### Scenario: embedding column conditional on pgvector

- **WHEN** the connected database does not have the pgvector extension
- **THEN** the `knowledge.memory_embeddings.embedding` column is absent; raw-SQL `EmbeddingSql.EmbeddingColumnExistsSql` returns false; `IngestAsync` and `SearchAsync` both handle the absent column gracefully (IngestAsync skips the vector UPDATE; SearchAsync returns an empty list).

### Requirement: Stable source key and content hash

**Status: planned — not implemented; specifies the contract `add-context-fabric` (#96) needs.**

In a follow-up change, `SourceDocument` SHALL carry a stable
**source key** distinct from the mutable `SourceRef`, plus an
immutable **content hash** of the ingested text. The source key SHALL
be stable across re-ingestions of the same logical source
(distinguishing "this is the same source" from "this is a new pointer
to a possibly-different document"). The content hash SHALL be
computed at ingest time over the chunkable text and SHALL never change
for a given content. A unique index SHALL enforce at most one active
`SourceDocument` per `(project_id, source_key)`. Today, no such
fields exist: `SourceRef` is a free-form string with no hashing and no
uniqueness constraint, and every `IngestAsync` call creates an
independent, permanently-active `SourceDocument` row (see the
"Ingestion writes chunks and embeddings" requirement for the current
behavior).

#### Scenario: Same source key, same content — single active row

- **WHEN** `IngestAsync` is called twice with the same `(projectId, source_key)` and the chunkable text is identical
- **THEN** the second call SHALL resolve to the existing `SourceDocument` row (no new row), the existing content hash SHALL match the recomputed hash, and the call returns the same `SourceDocumentId` with the same `ChunksWritten` count (no duplication of chunks).

#### Scenario: Same source key, new content — new revision

- **WHEN** `IngestAsync` is called with a `(projectId, source_key)` that already has an active `SourceDocument`, and the new content hash differs
- **THEN** a new revision of the source SHALL be created; prior chunks SHALL be marked stale-but-auditable (not deleted); and the new revision SHALL be marked the active generation.

### Requirement: Revision and generation tracking on reingest

**Status: planned — not implemented; specifies the contract `add-context-fabric` (#96) needs.**

In a follow-up change, re-ingesting under the same stable source key
SHALL create a new revision, mark the new revision the **active
generation**, and mark prior chunks stale-but-auditable. Search
results SHALL only surface chunks from the currently active
generation per source key. The audit history of prior generations
SHALL remain queryable for provenance / rewind, even though it is
not surfaced in normal retrieval. Today, no revision or generation
concept exists at all — every ingest is an independent, permanently
active `SourceDocument`, so re-ingestion accumulates without
invalidation. This is the literal gap
`add-mission-cowork/decomposition.md` Open Question 1 names.

#### Scenario: Reingested document

- **WHEN** a document with the same stable source key has new content
- **THEN** a new revision/generation becomes active, old chunks are stale but auditable, and new retrieval uses one active generation.

#### Scenario: Active generation is the only one surfaced

- **WHEN** a `SearchAsync` is called after a reingest that produced revision 2 of a source
- **THEN** the result includes chunks from revision 2 and does NOT include chunks from revision 1; revision 1's chunks remain in storage and are reachable through an audit / provenance query path (not through normal search).

### Requirement: Provenance on search hits

**Status: planned — not implemented; specifies the contract `add-context-fabric` (#96) needs.**

In a follow-up change, `KnowledgeSearchHit` SHALL carry source key,
revision id, and generation, not only `SourceDocumentId`, so Context
Pack citations can resolve provenance end-to-end. The
`KnowledgeSearchHit` record today carries only
`{ ChunkId, SourceDocumentId, ChunkText, Similarity }`. Once
provenance fields are added, every hit SHALL be addressable by
`(sourceKey, revisionId, generation)` and SHALL carry enough context
to render a citation back to its canonical source.

#### Scenario: Search hit carries provenance

- **WHEN** `SearchAsync` returns a hit against a chunk whose source key is `K`, revision id is `R`, and generation is `G`
- **THEN** the hit carries `sourceKey = "K"`, `revisionId = "R"`, `generation = G` in addition to the existing `ChunkId`, `SourceDocumentId`, `ChunkText`, and `Similarity`; downstream Context Pack assembly can cite the chunk by `(sourceKey, revisionId)` without re-deriving it.

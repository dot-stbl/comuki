## Purpose

Defines the repository-symbol graph (CodeGraph) for each Repository attached to a Project: parsed symbols, files, call-edges, and impact paths. The graph is built from the client's source code and exposed as a Context Fabric source so Brain and workers can answer "where is X defined / who calls Y / what breaks if I change Z" without re-deriving it on every Brain invocation. First iteration covers TypeScript and C# (the only stacks Comuki writes and the only stacks its customers ship in plan) using tree-sitter as the parser front-end; the spec stays parser-agnostic on the wire.

## ADDED Requirements

### Requirement: Graph aggregate per Repository
A CodeGraph SHALL belong to exactly one Repository and SHALL carry: an `Id`, the `RepositoryId` (source of truth for what it graphs), the active parser generation (one of `ts@v1`, `csharp@v1`, with extensions reserved but unused in v1), the set of indexed files with their content hash, the symbol table keyed by stable symbol id (`<fileHash>:<qualifiedName>`), the call-edge table keyed by `(callerSymbolId, calleeSymbolId)`, and the `CreatedAt` / `UpdatedAt` stamps. The graph SHALL be immutable across a given generation; a re-index produces a new generation and demotes the previous one to `auditable-only` (stale rows survive for provenance but never surface in retrieval).

#### Scenario: First-time index
- **WHEN** a Repository `R` is attached to a Project and `CodeGraph.BuildFor(R)` runs against its checked-out source at the active `Repository.Generation` (the source-repository generation, not the parser generation)
- **THEN** exactly one `CodeGraph` row exists for `R`, its `ParserGeneration` matches the parser version that produced it, and the file set covers every `*.ts` / `*.tsx` (under `src/`) and `*.cs` (under `platform/src/`, excluding `Migrations/`, `bin/`, `obj/`) that the Repository owns.

#### Scenario: Re-index after source change
- **WHEN** the Repository's source generation changes (`Repository.Generation` bumps) and a re-index runs
- **THEN** the previous `CodeGraph` row stays queryable through the audit path; a new row carries the new symbol set; retrieval only ever surfaces the active generation.

### Requirement: Symbol table
The graph SHALL carry, per `language`, the kinds that downstream consumers can ask about:

- TypeScript — `module`, `function`, `class`, `method`, `interface`, `typeAlias`, `enumMember` (resolved via tree-sitter `typescript`/`tsx` grammars).
- C# — `namespace`, `class`, `struct`, `interface`, `record`, `method`, `property`, `enum`/`enumMember`, `delegate` (resolved via tree-sitter `c_sharp` grammar).

Each symbol record SHALL carry `stableId`, `kind`, `qualifiedName`, `fileHash`, `startLine`, `endLine`, `signaturePreview` (truncated, ≤ 256 chars), and a `Modifiers` set (`abstract`, `sealed`, `partial`, `static`, `async`, `export`, `default`, …, language-appropriate). Symbols without a stable qualified name (anonymous expressions, top-level statements without a binding) SHALL be excluded from the symbol table — only named bindings participate in queries.

#### Scenario: Named export surfaces
- **WHEN** a TypeScript file exports `export function parseSku(input: string): Sku` at line 17
- **THEN** the symbol table contains one entry with `kind=function, qualifiedName=parseSku, fileHash=<that file>, startLine=17, signaturePreview="export function parseSku(input: string): Sku"`, and the symbol is reachable by query.

#### Scenario: Anonymous expression excluded
- **WHEN** a TypeScript file contains `const _ = useState(0)` at top level
- **THEN** no symbol record is created for that binding.

### Requirement: Call-edge table
The graph SHALL carry, per call site, an edge `(callerSymbolId, calleeSymbolId)` with a `confidence ∈ { resolved, bestGuess }`. `resolved` means the parser saw the callee symbol directly (identifier matched a symbol in the table); `bestGuess` means the parser matched by import path and local binding but could not resolve the final symbol (typical for dynamic dispatch and externally imported names not in the indexed scope). Edges inside the same file are required; cross-file edges are required where the parser can resolve them; cross-module edges are best-effort.

#### Scenario: Direct call resolve
- **WHEN** `parseSku` calls `validateSku` in the same file
- **THEN** the call-edge table contains one edge `(parseSku → validateSku, confidence=resolved)`.

#### Scenario: External callee not surfaced
- **WHEN** `parseSku` calls `externalFn` imported from `node_modules`
- **THEN** no edge exists for the `externalFn` call — the indexed scope is the Repository's own source; node_modules is excluded from the symbol table by construction.

### Requirement: File entry
The graph SHALL carry, per indexed file, a `FileEntry { fileHash, relativePath, language, sizeBytes, sha256, lastParsedAt }`. The `relativePath` is the path inside the Repository's checked-out source. `FileEntry.language` SHALL be one of `typescript`, `tsx`, `csharp`. Files in directories explicitly excluded from the index (e.g. `Migrations/`, `bin/`, `obj/`, `node_modules/`, `.git/`, `dist/`, `build/`) SHALL NOT appear in the file set.

#### Scenario: Migration file excluded
- **WHEN** a C# `platform/src/modules/Foo/Infrastructure/Migrations/20260901_Initial.cs` exists in the Repository
- **THEN** the file set has zero entries under `Migrations/`; the indexer logs the path with a reason.

#### Scenario: Node modules excluded
- **WHEN** the Repository contains `node_modules/...`
- **THEN** zero files from under `node_modules/` appear in the file set.

### Requirement: Impact path
The graph SHALL answer "what symbols and tests does this change affect?" by traversing the in-degree / out-degree adjacency list up to a configurable `depthMax` (default 3, range `[1, 5]`). An impact path SHALL be returned as an ordered list `[(originSymbol, edge, calleeSymbol)]` plus the set of leaf symbols reached. The traversal SHALL be deterministic for the same graph + origin + depth (`grep`-stable) — the same query returns the same path in the same order across runs.

#### Scenario: Direct impact
- **WHEN** a caller asks "what does changing `parseSku` break?" with `depthMax = 1`
- **THEN** the impact path lists every direct caller of `parseSku` (out-degree from `parseSku` is empty — `parseSku` has no callees in the result set; the result is its reverse adjacency up to depth 1).

#### Scenario: Depth-bounded
- **WHEN** a caller asks "what does changing `parseSku` break?" with `depthMax = 3`
- **THEN** the result set grows transitively up to 3 edges; symbols at depth > 3 are NOT included; the path length matches `depthMax + 1` symbols at most.

### Requirement: Context Fabric source adapter
The CodeGraph SHALL be exposed to Context Fabric as a `SourceRef` of kind `code-graph` with stable reference `{RepositoryId, ParserGeneration}`. Reads SHALL go through the same Context Fabric adapter surface as every other source — the Brain does NOT see a CodeGraph-specific API. Retrieval over a `code-graph` source SHALL be the union of:

1. `SymbolLookup(query)` — exact qualified-name match + qualified-name prefix matches (single-segment at a time);
2. `ImpactPaths(query)` — same as the previous requirement, exposed as a Context Fabric operation.

A query that mixes `SymbolLookup` and free-text is rejected at the adapter boundary (no hybrid mode in v1) — the planner picks one operation per request.

#### Scenario: Symbol lookup by qualified name
- **WHEN** the planner compiles a Context Pack and needs the body of `parseSku`
- **THEN** it issues `SymbolLookup("parseSku")` against the active `code-graph` source for the Repository; the result carries `{ stableId, kind, fileHash, startLine, endLine, signaturePreview }` and the planner follows up with a handle read for the body excerpt.

#### Scenario: Impact query for a planned change
- **WHEN** the Brain reports "I plan to change `parseSku`"
- **THEN** the planner can issue `ImpactPaths(originSymbolId = parseSku, depthMax = 3)` against the same source; the result feeds the risk/breadth note on the Brain's plan card.

### Requirement: Indexer cadence
A `CodeGraphIndexer` worker SHALL run on Repository attach, on every Repository generation bump, and on a heartbeat (default 5 minutes, range `[60s, 1h]`). Indexing MUST be incremental — re-indexing one file updates only that file's entries (the symbol rows carry `fileHash` so old rows whose file changed become stale). A full re-index MAY run on a `force=true` operator Decision and SHALL leave the previous generation as `auditable-only`.

#### Scenario: Heartbeat re-indexes changed file
- **WHEN** the heartbeat runs and a single file's content hash differs from the file entry's `sha256`
- **THEN** only that file is re-parsed; the resulting new symbol rows carry the same `fileHash` updated entries; unrelated files keep their existing entries; the new graph generation's `UpdatedAt` advances.

#### Scenario: Heartbeat on unchanged source
- **WHEN** the heartbeat runs and no file content hash differs
- **THEN** no new graph generation is created; the active generation stays unchanged; no work runs.

### Requirement: Access and visibility
A CodeGraph is a Repository-scoped source; the visibility rules of the host Project's attachments apply unchanged. A Repository with `AccessLevel = Read` exposes its CodeGraph read-only; a Repository with `AccessLevel = Write` allows the indexer to run and exposes read-side graphs. The graph SHALL NOT leak symbols from a Repository the caller has no read access to. Cross-Project queries SHALL NOT see neighbour Repositories unless the caller has the corresponding read access (per `repositories` capability's neighbour-visibility rules).

#### Scenario: Read-only Repository exposes a graph
- **WHEN** Project `P` has a `Read` attachment to Repository `R`
- **THEN** `P`'s Context Fabric queries against `R`'s code-graph source return hits exactly as for a `Write` attachment, except the planner cannot mark files changed or ask the indexer to refresh.

#### Scenario: No-access neighbour excluded
- **WHEN** Project `P` has no attachment to Repository `R'` but `R'` is a graph-neighbour of an attached Repository
- **THEN** `P`'s planner does NOT include `R'`'s code-graph source in any Context Pack; queries against `R'`'s source return empty.
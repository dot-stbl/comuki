## Why

The Knowledge module — ingestion (paragraph-aware chunker + embedding client),
pgvector-backed cosine search, MCP `knowledge.search` / `knowledge.ingest`
tools, REST surface, project/global scope — shipped in S10 but has no
OpenSpec capability entry under `openspec/specs/`. The
`add-mission-cowork/decomposition.md` Open Question 1 flagged this gap and
asked whether `add-context-fabric` (#96) needs an explicit Knowledge
spec to read against, or whether generic Knowledge should be excluded
from the first Context Fabric slice. The user decision
(2026-09-24, decomposition §Decisions) was to write a Knowledge
capability spec first as its own change — **add-knowledge-spec** (issue
#160) — landing before `add-context-fabric` (#96), because #96's
"Knowledge and procedural sources" requirement reads from this contract
(stable source key, immutable revision hash, active generation, provenance).

This change also names the exact gap between the existing
`IKnowledgeIngestor` XML docstring (which claims re-ingestion is idempotent
and supersedes prior chunks) and the actual implementation (which inserts
a new row on every call). That gap is the concrete content of
decomposition.md's Open Question 1 and is what motivates the planned
revision/generation requirements that context-fabric will rely on.

## What Changes

- ADD the `knowledge` capability spec. The spec **backfills** the existing
  ingest/search/MCP/REST/permissions/storage/embedding/doc-worker behavior
  faithfully against the shipped code (docs only — no code changes from
  this change).
- ADD forward-looking requirements in the same spec for the contract
  `add-context-fabric` (#96) needs from Knowledge: stable source key,
  immutable revision hash, active generation, provenance on search hits.
  These are NOT implemented by this change — they are spec-only, marking
  the contract gap and naming it as deferred to a follow-up change.

## Capabilities

### New Capabilities

- `knowledge`: source-owned document/chunk corpus, paragraph-aware chunking,
  pgvector cosine search, project/global scope, embedding provider selection,
  REST + MCP surface, permissions, doc-worker heartbeat, storage layout.
  Includes both the backfilled current behavior and the planned
  revision/generation/provenance contract that context-fabric depends on.

### Modified Capabilities

None. This change touches no other spec file. `add-context-fabric` (#96)
will read from `knowledge/spec.md` once that change is authored.

## Impact

- **Docs only for the backfill portion.** All 13 backfill requirements
  describe behavior already shipped in `Comuki.Modules.Knowledge.*` and
  `Comuki.Host/Knowledge` + `Comuki.Host/Mcp`. No code, migration, or
  configuration change.
- **Spec-only for the planned portion.** The 3 forward-looking requirements
  (stable source key + content hash, revision/generation tracking,
  provenance on search hits) define a contract that no code in this repo
  implements today. Their implementation is deferred to a follow-up change
  (either absorbed into `add-context-fabric` (#96) or as a dedicated
  `add-knowledge-revisions` change) and is out of scope for this PR.
- No migrations, no `comuki.slnx` changes, no dependency changes.
- No changes to `openspec/specs/` (the main capability tree). The delta
  spec under `openspec/changes/add-knowledge-spec/specs/knowledge/spec.md`
  is the source of truth for this change and will be synced into
  `openspec/specs/knowledge/spec.md` at apply/archive time, at which
  point the new capability also gets a row in `openspec/README.md`'s
  capability map table. Neither sync is in scope for this change.

## Non-goals

- Implementing the 3 planned requirements (revision/generation/provenance
  / stable source key / content hash) in this change — they are deferred
  to a follow-up.
- Changing the MCP tool surface: tool names `knowledge.search` and
  `knowledge.ingest` are locked in code today and this spec backfills
  them as-is.
- Swapping pgvector for Qdrant (or any other vector store). The pgvector
  column on `memory_embeddings` is the contract.
- A doc-agent auto-ingest loop. The `KnowledgeIngestBackgroundService`
  heartbeat is documented as current behavior; the `KnowledgeSource`
  table that would let it dispatch real work is not part of this change.
- Enforcing the unused `knowledge:admin` permission key. It is declared
  in `Permissions.cs` and granted to `PlatformAdmin` in `RoleMatrix.cs`,
  but as of this spec no endpoint or MCP tool checks it. The spec records
  this as a documented fact (an unused/reserved key), not a behavioral
  requirement with a `WHEN/THEN` scenario.

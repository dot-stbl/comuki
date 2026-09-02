# Memory Specification

## Purpose

Defines long-term memory facts (pgvector-ready), the shared MemoryDigest used
to assemble Brain context, ephemeral sweep, and the learning-candidates queue.
Durable memory lives in the orchestrator — agents stay stateless.

## Requirements

### Requirement: Memory facts table
The platform SHALL store memory facts in `memory_facts` with: scope
(`user` | `project` | `global`), subject id, kind (`standing` | `ephemeral`),
topic key, text, optional embedding vector (768 dimensions when present),
source (`chat` | `human` | `run` | `learning-approved`), created_by,
created_at, and superseded_at. Writing the same topic key SHALL supersede the
previous visible row (stamp `superseded_at`) rather than delete it.

#### Scenario: Supersede keeps audit
- **WHEN** a fact is written for an existing topic key in the same scope
- **THEN** the prior row is superseded and excluded from default search while
  remaining queryable for audit

### Requirement: Visibility and ephemeral TTL
Search and digest SHALL see only non-superseded facts. Ephemeral facts SHALL
expire after 14 days from creation; a background sweep SHALL delete expired
ephemeral rows. Standing facts have no TTL.

#### Scenario: Ephemeral past TTL
- **WHEN** an ephemeral fact is older than 14 days
- **THEN** it is invisible to search/digest and removed by the sweep worker

### Requirement: Search without embeddings
`IMemoryStore.Search` SHALL rank by cosine similarity when embeddings are
present on both query and rows; otherwise it SHALL fall back to
scope/kind/freshness ranking so facts remain usable with embeddings off.

#### Scenario: Embeddings disabled
- **WHEN** no embedding is supplied on the query
- **THEN** search still returns visible facts ordered by standing-first then
  freshest

### Requirement: MemoryDigest assembly
A shared `MemoryDigest.Build` SHALL assemble Brain context: top relevant
facts (lexical overlap stand-in until query embeddings land) plus freshest
standing facts not already included, deduplicated. Both chat and orchestration
callers SHALL use the same digest service. When the host has not wired the
real digest, an empty digest stub MAY be registered so composition still
boots; an empty digest means "nothing fed" and MUST NOT invent facts.

#### Scenario: Empty digest stub
- **WHEN** the host registers the empty digest fallback
- **THEN** digest build returns empty content and callers do not journal a
  fabricated context

### Requirement: Brain memory.search tool
The Brain agent loop SHALL expose a `memory.search` tool that queries the
memory store (default scope global) and returns ranked fact text for grounding.
Tool calls are part of the Brain's observable tool surface.

#### Scenario: Brain searches facts
- **WHEN** the Brain invokes `memory.search` during a plan/diagnose turn
- **THEN** the tool returns visible facts from the store without writing

### Requirement: Learning candidates queue
Learning candidates SHALL live in `learning_candidates` (not in
`memory_facts`) with pattern, source ref, repeat counter, and status
`pending` | `approved` | `rejected`. Approval SHALL NOT silently write rules
into client git in v0 — the table is the queue; the approve→PR path is a
later delivery.

#### Scenario: Candidate is not a fact
- **WHEN** a verify-failure pattern is recorded as a learning candidate
- **THEN** it does not appear in memory search until explicitly approved into
  facts/rules by a later workflow

### Requirement: Persistence layout
Memory tables (`chat_messages`, `chat_checkpoints`, `memory_facts`,
`learning_candidates`) SHALL use a module-private migrations history
(`__comuki_memory`). Chat module may remap Voluta checkpoints onto the
`chat_checkpoints` contract name while owning `chat_sessions` /
`chat_messages` under `__comuki_chat`.

#### Scenario: Separate migration histories
- **WHEN** the migrator applies schemas
- **THEN** memory and chat histories do not collide with orchestration /
  identity / projects

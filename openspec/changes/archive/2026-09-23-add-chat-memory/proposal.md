# add-chat-memory

## Proposal

Chat (Voluta in Comuki.Host) and the Brain need a memory system: short-term
session memory (resumable days later) and long-term fact memory (semantic
search via pgvector). The Brain itself stays stateless — callers assemble
its context through one shared digest service. A separate learning-candidates
queue collects rule candidates from PR comments / verify failures until a
human approves them into the client's git.

This is the design contract for S5 (#5). Main specs for chat/brain land with
the implementation; this change pins the memory model.

## Design

### Short-term (session)

- `chat_messages` — every message, Postgres.
- `chat_checkpoints` — Voluta graph state (jsonb): current node, pending
  approve-interrupt, collected variables. Sessions resumable days later.
- Auto-archive after 30 days of inactivity.

### Long-term (facts) — `memory_facts` + pgvector

One table for all facts:

| Column | Notes |
|---|---|
| id, scope (`user`\|`project`\|`global`), subject_id | who/what it belongs to |
| kind | `standing` (decisions) \| `ephemeral` (task-scoped, TTL 14d) |
| topic_key | canonicalization key: same topic → write supersedes old rows |
| text, embedding vector | embedding computed on write |
| source | `chat` \| `human` \| `run` \| `learning-approved` |
| created_by, created_at, superseded_at | superseded rows excluded from default search |

- **Read**: digest auto-injected into prompts (top-K by cosine + freshest)
  AND a `memory.search` tool for deep dives.
- **Write**: ONLY via `memory.write` tool (graph decides, or human command
  «запомни»); same topic_key ⇒ old row superseded, not deleted (audit).
- **Forget**: `/forget {id}` tool + `/memory list` surface; ephemeral kind
  auto-expires (14d sweep job).

### Brain context assembly (variant Z)

- `MemoryDigest.Build(task, scope)` — one shared service in Comuki.Host.
- Called by BOTH brain callers: the chat graph and Orchestration
  (auto-replan). What was fed to the brain is journaled (audit/replay).
- The Brain additionally holds a `memory.search` tool for deep dives;
  every tool call lands in the journal.

### Learning candidates — separate `learning_candidates` table

NOT in memory_facts (different lifecycle). Queue: pattern, source ref
(PR comment / verify failure), repeat counter, status
`pending → approved | rejected`. Approve ⇒ PR into the client's git
(profiles/rules), not a silent write.

## Memory / file plan

- Postgres migration: chat_messages, chat_checkpoints, memory_facts (+pgvector
  extension), learning_candidates.
- Host: MemoryDigest service, memory tools registration, chat hub wiring.
- Host.Brain: memory.search tool alongside its catalog/runs/plan tools.

## Open questions

- Embedding model/provider for memory_facts (MEAI embeddings vs external) —
  spike in S5; must be off-switchable (facts work without embeddings via
  kind/scope/freshness ranking).
- Context window budget for digest (top-K default) — tune during S5.

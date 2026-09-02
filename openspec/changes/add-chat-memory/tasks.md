# add-chat-memory — tasks

Status vs master (Wave 5 landed; see also `backfill-chat-memory` + main
`specs/chat` + `specs/memory`):

- [x] Migration: chat_messages, chat_checkpoints, memory_facts (+pgvector column), learning_candidates
- [x] MemoryStore (Infrastructure): write (supersede by topic_key), search (cosine path ready + freshness/kind/scope fallback), sweep ephemeral 14d
- [x] MemoryDigest.Build(task, scope) — shared service exists; host still may register EmptyMemoryDigest until fully wired
- [~] memory.search / memory.write / memory.forget / memory.list tools — Brain exposes `memory.search`; write/forget/list slash/tools partial vs original list
- [x] Voluta chat graph: sessions over checkpoints, HITL approve (`POST …/approve`)
- [x] Brain memory.search tool; digest injection by callers is stub-tolerant
- [~] /init wizard skeleton steps wired to tools — slash catalog + InitNode present; full wizard depth TBD
- [x] Tests exist around store/digest/chat surfaces (see module test projects)

Remaining debt tracked in `backfill-chat-memory` / product issues: full digest
wiring in Host composition, learning-candidate approve→client-git, complete
memory tool surface beyond search.

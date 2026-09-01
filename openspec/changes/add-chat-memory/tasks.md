# add-chat-memory — tasks

- [ ] Migration: chat_messages, chat_checkpoints, memory_facts (+pgvector), learning_candidates
- [ ] MemoryStore (Infrastructure): write (supersede by topic_key), search (cosine + freshness + kind/scope fallback without embeddings), sweep ephemeral 14d
- [ ] MemoryDigest.Build(task, scope) — shared service, journaled output
- [ ] memory.search / memory.write / memory.forget / memory.list tools
- [ ] Voluta chat graph: sessions over checkpoints, recall step, HITL approve
- [ ] Brain memory.search tool; digest injected by callers (chat + orchestration)
- [ ] /init wizard skeleton steps wired to tools
- [ ] Tests: supersede semantics, ephemeral sweep, digest ranking, checkpoint resume

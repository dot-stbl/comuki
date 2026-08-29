-- Postgres init for Comuki. Runs once on first container start
-- (docker-entrypoint-initdb.d semantics).
--
-- pgvector is the in-database vector store for Comuki.Platform.Knowledge
-- (Phase 5 — Slice 2). Per comuki-decisions.md § "База знаний и документация":
-- code stays in worktree (agent grep), but docs/specs/rules go into the
-- retrieval store and are served to agents via MCP. Embeddings live here.
--
-- Vector dimensions match the embedding model we wire in Phase 5
-- (default for sentence-transformers/all-MiniLM-L6-v2 is 384; override
-- if we switch providers).

CREATE EXTENSION IF NOT EXISTS vector;

-- Comuki database bootstrap. Runs once on the first Postgres start
-- (docker-entrypoint-initdb.d semantics).
--
-- The migrator also runs `DatabaseSchemaEnsurer.EnsureAsync` for every
-- schema before applying migrations, so this file is a belt-and-braces
-- bring-up: it guarantees the pgvector extension and all ten module
-- schemas exist before any Comuki binary touches the database.

-- Vector store for the knowledge module (embeddings in schema
-- `knowledge`). Requires a pgvector-enabled image (pgvector/pgvector).
CREATE EXTENSION IF NOT EXISTS vector;

-- One schema per module DbContext; each keeps its own
-- __ef_migrations_history table inside its schema.
CREATE SCHEMA IF NOT EXISTS orchestration;
CREATE SCHEMA IF NOT EXISTS identity;
CREATE SCHEMA IF NOT EXISTS projects;
CREATE SCHEMA IF NOT EXISTS memory;
CREATE SCHEMA IF NOT EXISTS knowledge;
CREATE SCHEMA IF NOT EXISTS chat;
CREATE SCHEMA IF NOT EXISTS intake;
CREATE SCHEMA IF NOT EXISTS costs;
CREATE SCHEMA IF NOT EXISTS artifacts;
CREATE SCHEMA IF NOT EXISTS scheduler;

# `.agents/docs/` — index

The agent-facing documentation tree for Comuki. Lives next to
`STATE.md` / `ROADMAP.md` so any agent reading orientation finds the
operational details in the same place.

| Section                   | Purpose                                                                                  |
|---------------------------|------------------------------------------------------------------------------------------|
| [`architecture/`](./architecture/) | Project design artifacts — decisions, architecture, stack, project structure, slice-0 spec. |
| [`operations/`](./operations/)     | How the platform runs — install, storage, OIDC, MinIO, OpenAPI codegen, Postgres schemas, FE env. |
| [`product/`](./product/)           | FE requirements and task breakdown (drives the S7 follow-up work). |
| [`design-system/`](./design-system/) | Comuki design tokens + shadcn mappings + dashboard screens (HTML/CSS, source for the Ladle catalog). |

## What's where

### Operations (read first when wiring a slice)

| Doc                                          | Read when |
|----------------------------------------------|-----------|
| [install.md](./operations/install.md)        | Deploying the migrator; setting `COMUKI_DB` / `COMUKI_MIGRATOR_DB_PASSWORD`. |
| [runbook.md](./operations/runbook.md)        | On-call guide for self-hosted Comuki — quick start, bootstrap admin, OIDC, backup/restore, upgrade, troubleshooting (issue #10 T11.1). |
| [backup.md](./operations/backup.md)          | Per-store backup + restore procedure — `pg_dump` per schema, `mc mirror` for MinIO, retention policy. |
| [storage.md](./operations/storage.md)        | Postgres + MinIO + Victoria retention; per-store policies. |
| [oauth-oidc.md](./operations/oauth-oidc.md)  | Wiring an OIDC provider; browser-driven start flow; `OidcAccountLinker`. |
| [minio.md](./operations/minio.md)            | Run-artifact bucket topology, lifecycle policy, compose `minio-init`, host config. |
| [openapi-codegen.md](./operations/openapi-codegen.md) | BE emits `artifacts/openapi.json`; FE regenerates via kubb. Fail-fast guard. |
| [database-schemas.md](./operations/database-schemas.md) | 8 schemas, one per DbContext; per-schema `__ef_migrations_history`; Migrator loop. |
| [fesettings.md](./operations/fesettings.md)  | `VITE_*` env contract; mock-first vs real-backend switch. |

### Architecture (read for design intent)

| Doc                                                                  | Read when |
|----------------------------------------------------------------------|-----------|
| [architecture/README.md](./architecture/README.md)                   | First read — five-doc set covering decisions, architecture, stack, project structure, slice-0. |
| [architecture/comuki-decisions.md](./architecture/comuki-decisions.md) | "Why did we pick X?" — alternatives, trade-offs. |
| [architecture/comuki-architecture.md](./architecture/comuki-architecture.md) | Workflow, services, MVP vs "later". |
| [architecture/comuki-stack.md](./architecture/comuki-stack.md)       | Concrete stack — components, storage, durable infra, observability, API. |
| [architecture/comuki-project-structure.md](./architecture/comuki-project-structure.md) | Repo layout, C#-layers, agents, deploy. |
| [architecture/comuki-slice-0.md](./architecture/comuki-slice-0.md)   | The S3 e2e proof — pull-model, Translator/gRPC, pi-as-headless-agent. |
| [architecture/comuki-v1-scope-draft.md](./architecture/comuki-v1-scope-draft.md) | The v1 milestone scope draft. |
| [architecture/adr-0001-ui-kit-react-aria.md](./architecture/adr-0001-ui-kit-react-aria.md) | The React Aria decision. |

### Design system

| Doc                                                       | Read when |
|-----------------------------------------------------------|-----------|
| [design-system/Comuki Design System.md](./design-system/Comuki%20Design%20System.md) | Tokens, palette, status semantics. |
| `design-system/styles/{tokens,components,globals}.css`    | The CSS source. |
| `design-system/dashboard/*.{js,jsx,css}`                  | Dashboard screens catalog (also served by Ladle). |

### Product

| Doc                                                          | Read when |
|--------------------------------------------------------------|-----------|
| [product/comuki-task-breakdown.md](./product/comuki-task-breakdown.md) | Per-slice task numbering for S5–S11; where individual tasks come from. |
| [product/comuki-fe-requirements.md](./product/comuki-fe-requirements.md) | FE screens required by the v1 spec. |

## Status

v1 milestone implementation is complete on `master` (`947d116`) —
all 15 v1 slices (S0–S14) landed; 24 of 29 GitHub issues are
closed; the 5 remaining open issues track work beyond v1 core
scope (S7 FE dashboard pages, S9 cross-cutting kit, S10
knowledge, S11 polish, post-1.0 backlog). Live status lives in
[`.agents/STATE.md`](../STATE.md).

See [`.agents/ROADMAP.md`](../ROADMAP.md) for the per-phase status
table.

## Other entry points

| Where you are                                              | Where to look |
|------------------------------------------------------------|---------------|
| Just opened this repo and want a high-level overview       | [AGENTS.md](../../AGENTS.md) |
| Looking for the latest shipped status                       | [STATE.md](../STATE.md) |
| Looking for the per-phase design intent                      | [ROADMAP.md](../ROADMAP.md) |
| Looking for a specific operational detail (OIDC, MinIO, …) | this index → Operations |
| Looking for the architectural why                           | this index → Architecture |
| Looking for the design tokens / palette                    | this index → Design system |
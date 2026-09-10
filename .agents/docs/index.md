# `.agents/docs/` — index

The agent-facing documentation tree for Comuki. Lives next to
`STATE.md` / `ROADMAP.md` so any agent reading orientation finds the
operational details in the same place.

| Section                   | Purpose                                                                                  |
|---------------------------|------------------------------------------------------------------------------------------|
| [`architecture/`](./architecture/) | Project design artifacts — decisions, architecture, stack, project structure, slice-0 spec. |
| [`operations/`](./operations/)     | How the platform runs — install, storage, OIDC, MinIO, OpenAPI codegen, Postgres schemas, FE env. |
| [`product/`](./product/)           | FE requirements, task breakdown, owner product decisions. |
| [`design-system/`](./design-system/) | Comuki design tokens + shadcn mappings + dashboard screens (HTML/CSS, source for the Ladle catalog). |
| [`audits/`](./audits/)             | One-shot architecture / product / security / testing / DI / dependency / performance audits. |

## What's where

### Operations (read first when wiring a slice)

| Doc                                          | Read when |
|----------------------------------------------|-----------|
| [install.md](./operations/install.md)        | Deploying the migrator; setting `COMUKI_DB` / `COMUKI_MIGRATOR_DB_PASSWORD`. |
| [runbook.md](./operations/runbook.md)        | On-call guide for self-hosted Comuki — quick start, bootstrap admin, OIDC, backup/restore, upgrade, troubleshooting (issue #10 T11.1). |
| [backup.md](./operations/backup.md)          | Per-store backup + restore procedure — `pg_dump` per schema, `mc mirror` for MinIO, retention policy. |
| [storage.md](./operations/storage.md)        | Postgres + MinIO + Victoria retention; per-store policies. |
| [oauth-oidc.md](./operations/oauth-oidc.md)  | Wiring an OIDC provider; browser-driven start flow; `OidcAccountLinker`. |
| [security.md](./operations/security.md)      | Operational security posture — token storage, cookie attributes, rate limits, CSRF / XSS / open-redirect defences, audit log scope. |
| [minio.md](./operations/minio.md)            | Run-artifact bucket topology, lifecycle policy, compose `minio-init`, host config. |
| [openapi-codegen.md](./operations/openapi-codegen.md) | BE emits `artifacts/openapi.json`; FE regenerates via kubb. Fail-fast guard. |
| [scheduler.md](./operations/scheduler.md)     | Scheduled-job dispatcher (S15 / #44): cron → ephemeral worker + sentry observability. |
| [database-schemas.md](./operations/database-schemas.md) | 10 schemas, one per DbContext; per-schema `__ef_migrations_history`; Migrator loop. |
| [fesettings.md](./operations/fesettings.md)  | `VITE_*` env contract; mock-first vs real-backend switch. |
| [proxy.md](./operations/proxy.md)            | Optional in-process reverse proxy for OpenAI/Anthropic-compatible endpoints; virtual keys + budgets. |
| [connect-source.md](./operations/connect-source.md) | Wiring a tracker webhook (GH/GL/YT/Jira) end-to-end; per-provider env-var secret pattern. |
| [host-internals.md](./operations/host-internals.md) | `HostComposer.Compose` — the orchestrator host's single composition root: every DI registration, every endpoint, every background service. |
| [observability.md](./operations/observability.md) | ActivitySource / Meter naming convention (`comuki.*`), spans to expect, metrics to alert on; OTLP pipeline to Victoria. |

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
| [architecture/post-1.0-backlog.md](./architecture/post-1.0-backlog.md) | The 4 v2-deferred issues (#47–#50) and their scope. |
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
| [product/product-decisions.md](./product/product-decisions.md) | Owner answers to the 43 product-audit questions (2026-09-08). |

### Audits

| Doc | Read when |
|-----|-----------|
| [audits/architecture-audit-2026-09-04.md](./audits/architecture-audit-2026-09-04.md) | Earlier architecture scan (godif / csharp / ts / openspec). |
| [audits/architecture-audit-report.md](./audits/architecture-audit-report.md) | Architecture audit 2026-09-08. |
| [audits/audit-product-report.md](./audits/audit-product-report.md) | Product / business-logic audit. |
| [audits/security-audit-report.md](./audits/security-audit-report.md) | Security findings. |
| [audits/testing-audit-report.md](./audits/testing-audit-report.md) | Testing gaps. |
| [audits/di-lifetime-audit-report.md](./audits/di-lifetime-audit-report.md) | DI lifetime / scope. |
| [audits/dependency-audit-report.md](./audits/dependency-audit-report.md) | NuGet / npm / bun deps. |
| [audits/performance-audit-report.md](./audits/performance-audit-report.md) | Performance findings. |

## Status

v1 milestone is complete on `master` (`fa659fd`) — 24 slices landed
(15 original v1 core + 9 follow-on: 5 FE wire-up, 2 polish waves,
1 admin endpoints, 1 docs sweep) plus the `#11` Post-1.0 backlog
slice (13 sub-slices shipped 2026-09-04 → 2026-09-07). **50 of 50
GitHub issues closed** (0 open) on 2026-09-08. 4 deferred issues
(#47, #48, #49, #50) closed with "v2 backlog" note. Live status lives
in [`.agents/STATE.md`](../STATE.md).

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
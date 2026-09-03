---
milestone: v1
status: wave-6-complete-and-fe-wiring-slices-1-through-5-merged
last_updated: 2026-09-04
progress:
  total_slices: 15
  completed_slices: 15
  percent: 100
  issues_closed: "1,2,3,4,5,6,12,14,15,16,17,18,19,20,21,22,23,24,25,26,27,28,29,13"
  issues_open: "7,8,9,10,11"
  open_notes: |
    #7 S7 — FE dashboard pages still consume mock seeds in some domains.
    #8 S9 — Cross-cutting kit, cost/budgets, optional proxy.
    #9 S10 — Knowledge (pgvector + MCP + docs worker).
    #10 S11 — v1.0 polish: security review, load test, onboarding docs sync.
    #11 — post-1.0 backlog (fleet, autonomy, merge-queue, domain-user, eval).
---

# Project State

## Current Position

All 15 v1 slices landed on master (`947d116`). 24 of 29 GitHub issues are
closed; the 5 that remain open track feature-aspirational work beyond v1
core scope.

**Slice cadence (merge commit SHAs on `master`):**

| Slice | Issue | Title | SHA |
|---|---|---|---|
| S0 | #1 | Skeleton platform/: shared · modules · engine · host | landed |
| S1 | #2 | Runs · queue · journal | landed |
| S2 | #3 | Compute: Docker provider + scale v0 | landed |
| S3 | #4 | Translator · worker image · gRPC (Slice 0 e2e) | landed |
| S4 | #12 | Identity: users · API keys · RBAC · OIDC | landed (Wave 5, `feature/audit-rules-batch-d`) |
| S5 | #5 | Chat (Voluta в Host) + Host.Brain + approve | landed |
| S6 | #6 | Intake: GH · GL · Yandex Tracker · Jira + sync-back | landed |
| S7 | #7 | FE ядро + SignalR realtime | partial — see note |
| S9 | #8 | Cross-cutting kit + cost/budgets + optional proxy | open |
| S10 | #9 | Knowledge (opt-in): pgvector · MCP · docs worker | open |
| S11 | #10 | v1.0 polish: security · load · onboarding · docs sync | open |
| S12 | #14 | agents/ TS-пакеты: agent-core · worker-sdk · dev-sdk | landed |
| S13 | #15 | control-plane дефолты: профили · каталог · chat-commands | landed |
| S14 | #16 | CI: GitHub Actions | landed |

> Note: S8 (#13) was originally scoped but its deliverable (Kubernetes compute
> provider, FreeSlots ScalePolicy, OTel business spans, Grafana as-code) was
> folded into Wave 6 alongside S9 cost work; issue #13 is closed in this
> housekeeping commit. S7 (#7) core plumbing (kubb-generated client,
> code-based router, kubb-client transport, login/OIDC flow, runs/identity/
> projects/inbox wire-up slices 1–5) is merged; the open work is FE dashboard
> pages replacing mock seeds.

**Polish + feature issues closed in Wave 6** (#17–#28):

| # | Title |
|---|---|
| 17 | HostComposer lacks registered IExceptionHandler |
| 18 | Split multi-type files into one type per file (Intake provider DTOs) |
| 19 | SignalR EnableDetailedErrors leaks stack traces in production |
| 20 | Six endpoints hand-roll ProblemDetails instead of TypedResults.Problem |
| 21 | Dev DB password committed in Comuki.Migrator/appsettings.json |
| 22 | CostsModuleEndpoints and RunsController lack RequiresPermission |
| 23 | CostsModuleEndpoints uses literal route instead of ApiRoutes.ProjectCosts |
| 24 | Drop Dto suffix from 12 Intake provider wire records |
| 25 | Cap folder file count at 3 across 40+ production folders |
| 26 | Introduce real Postgres schemas per DbContext (orchestration/identity/projects/memory/chat/intake/costs) |
| 27 | Inbound PR-review: admit GH/GL pull requests as tickets + pr-review profile |
| 28 | Run artifact bundle in MinIO (S3) keyed by project/run-id |
| 29 | Wire OpenAPI emission + align kubb with console.x pattern |

**FE wire-up slices 1–5** (post-#29, dashboard domain wire-through):

| # | Title | SHA |
|---|---|---|
| wire-runs | slice 1: kubb-client transport, runs queries, mappers | `b8a2407` |
| wire-identity | slice 2: login/me/oidc, kubb client + mappers | `fcafe09` |
| wire-projects | slice 3: projects queries/mutations wired | `6ff578c` |
| wire-inbox | slice 4: inbox queries/mutations wired | `c760569` |
| wire-oidc | slice 5: browser-driven OIDC start + callback | `9f731f6` |

## Что живёт (после 947d116)

### Backend

- **Каркас**: `platform/src/{shared,modules,engine,host}` + `platform/build`
  (format gate + `dotnet format --severity hidden`).
- **Engine**: `Comuki.Engine.Orchestration` (runs / queue / claim-lease
  `SKIP LOCKED` / journal / reaper) · `Comuki.Engine.Compute` (Docker +
  Kubernetes providers, `KubernetesComputeProvider` использует `batch/v1 Job`
  с `backoffLimit=0` / `ttlSecondsAfterFinished`, ScaleSupervisor cycle).
- **Shared**: `Comuki.Shared.Kernel` (ids, exceptions, subject scoping) ·
  `Comuki.Shared.Contracts` (gRPC, brain, queue, journal, plans, memory,
  control-plane) · `Comuki.Shared.Telemetry` (ActivitySource + Meter,
  `AddComukiTelemetry()` installer) · `Comuki.Shared.Filtering` (DSL parser
  → IQueryable).
- **8 модулей** в `platform/src/modules/`:
  - **Identity** — RBAC (`RoleMatrix`/`RoleKeys` в коде, `ck_` API keys с
    HMAC pepper, OIDC linker с per-provider схемами + `OidcAccountLinker`).
  - **Projects** — CRUD + per-project settings с live-reload, бюджеты и
    concurrency caps (`ProjectSettingsCacheRefresher`).
  - **Chat** — Voluta-graph integration в Host, checkpoints +
    `chat_sessions` / `chat_messages` storage, slash-commands.
  - **Memory** — long-term facts с pgvector (raw-SQL managed embeddings),
    learning-candidate queue, chat checkpoints в `memory` schema.
  - **Intake** — GH/GL/Yandex Tracker/Jira источники, dedupe, sync-back
    outbox, GH/GL PR-review профиль (issue #27), admission rules.
  - **Costs** — `UsageRecorder` под `IBudgetGate`, project costs view +
    `ProjectBudgetSettingsAdapter` читает лимиты из Projects.
  - **Runs + Workers** — `Comuki.Host.Translator` (AOT-pi-pump, stream-json
    parser, gRPC server) + `Comuki.Host.Grpc` worker service +
    `WorkerTokenAuthenticator` (opaque TTL).
  - **Artifacts** — `MinioRunArtifactStore`, terminal-state packager
    пишет bundle в MinIO (`{projectId}/{runId}/{brief,result,pins}.json`).
- **Host**: `/health` · `/api/v1/auth/*` (cookie + OIDC start/callback +
  bootstrap admin) · `/api/v1/projects` + settings · `/api/v1/runs` +
  `/api/v1/runs/{id}/artifacts` · `/api/v1/chat/sessions` + slash ·
  `/api/v1/intake/{admission-rules,inbox,sources,tickets,webhooks}` ·
  `/api/v1/costs/projects/{id}` · `/api/v1/controlplane/{profiles,
  chat-commands}` · `/api/v1/workers/claim|complete|fail` (gRPC-compatible
  HTTP) · `/api/v1/host/grpc` для Translator. `HostComposer` (internal, IVT
  для тестов).
- **OpenAPI emission** — `Microsoft.AspNetCore.OpenApi 10.0.9` +
  `Microsoft.Extensions.ApiDescription.Server` спавнят `GetDocument.Insider`
  при `dotnet build` (Debug only); csproj target
  `RenameOpenApiOutputToCanonicalName` переименовывает
  `Comuki.Host.json` → `openapi.json`. `OpenApiBuildTimeExtensions` стрипат
  hosted services во время инспекции (issue #29).
- **SignalR `/realtime/runs`** — `RunsHub` (JoinRun/JoinProject +
  permissions) + `RunEventsBroadcastInterceptor` пушит journal events;
  `EnableDetailedErrors` отключён в production (issue #19).

### Frontend (`dashboard/`)

- **5 доменов на реальный backend**: runs (slice 1) · identity (slice 2) ·
  projects (slice 3) · inbox (slice 4) · OIDC start (slice 5).
  Остальные домены (`tasks`, `chat`, `cost`, `compute`, `knowledge`,
  `models`, `sources`, `queue`, `verify`, `observability`, `home`, `identity`)
  ещё на mock seeds — это часть S7 (#7) follow-up.
- **API client** — `kubb v4.39.2` (`@kubb/cli`, `@kubb/core`,
  `@kubb/plugin-client`, `@kubb/plugin-oas`, `@kubb/plugin-react-query`,
  `@kubb/plugin-ts`, `@kubb/plugin-zod`). `dashboard/kubb.config.ts` →
  `@/shared/api/kubb-client` (hand-written transport: VITE_API_BASE_URL +
  `credentials:'include'` + 401/403). `output.clean: true` стирает только
  `_generated/*`, ручной код в `src/shared/api/{kubb-client,mock}/`
  сохраняется.
- **Auth** — code-based TanStack Router, `useTranslation()` для всего
  user-facing copy, browser-driven OIDC start
  (`window.location.assign`), `VITE_OIDC_PROVIDER` env, OIDC callback
  обрабатывает Host (`/api/v1/auth/oidc/{provider}/callback`) и возвращает
  `/` с кукой.
- **Tests** — vitest 4.1.x + Testing Library; mock-режим (`VITE_USE_MOCK=true`)
  не требует `VITE_API_BASE_URL`; real-mode throws на первом hook call без
  base URL.

### Хранилища

- **Postgres** — 8 schemas, по одной на DbContext: `orchestration`,
  `identity`, `projects`, `memory`, `chat`, `intake`, `costs`, `artifacts`
  (issue #26). Каждая schema имеет собственную `__ef_migrations_history`
  таблицу; `Comuki.Migrator/Program.cs` цикл `EnsureSchema` → `MigrateAsync`
  per context.
- **MinIO (S3)** — `comuki-run-bundles` бакет, ключи
  `{projectId}/{runId}/{brief,result,pins}.json`; compose `minio-init`
  job создаёт бакет + 30-day non-current-version lifecycle.
- **VictoriaMetrics / VictoriaLogs** — `deploy/` поднимает с
  `--retentionPeriod=1`; OTel → Victoria через OTLP.
- **Grafana as-code** — `deploy/grafana/dashboards/{comuki-runs,
  comuki-workers,comuki-cost}.json` + provisioning datasource в Victoria +
  provisioning dashboards.

### Observability

- `Comuki.Shared.Telemetry` — единый installer + OTel ActivitySource/Meter per
  assembly, `comuki.*` (точнее `{app}.*`) metric names, dot.case
  bounded-cardinality tags (см. `~/.agents/rules/observability/diagnostics.md`).
- **Bounded-cardinality spans** — `Claim`, `ApplyPlan`, `Brain.Invoke`,
  `Compute.Start` (тэги: profile / project / run_id).
- **Error envelope** — RFC 9457 ProblemDetails через единственный
  `ProviderExceptionHandler` (issue #17) + `AddProblemDetails()`;
  endpoints пользуются `TypedResults.Problem()` / `TypedResults.ValidationProblem()`.

### Tests

- **Backend** — xUnit v2 + Shouldly + NSubstitute + Testcontainers +
  Respawn + Bogus + coverlet, 70% line gate (см. `testing-stack-and-pyramid.md`).
- **Frontend** — vitest + Testing Library + jsdom + Playwright config (config-only,
  e2e флаг не зелёный).
- **CI** — GitHub Actions (`S14` / #16); see `.github/workflows/`.

### Agents (`agents/`)

- **bun workspace** — `comuki-agent-core` (zod, parser-зеркало),
  `comuki-worker-sdk` (locks, skills), `comuki-dev-sdk` (Claude Code fork).
- **Test status** — TS unit зелёный; e2e через TestFakePi покрывает S3.

## Гейты (Definition of Done)

1. `dotnet build comuki.slnx -c Debug` — 0/0 + `[VerifyFormatOnBuild] Format check passed`
   (жёсткий формат-гейт в графе билда).
2. Все suite'ы зелёные (`dotnet run --project <test>` — MTP, не `dotnet test`).
3. FE (когда тронут): `cd dashboard && bun run typecheck && bun run lint && bun run test`.
4. Agents TS: `cd agents && bun install && bun run typecheck && bun test`.
5. **OpenAPI emission gate** — `artifacts/openapi.json` должен появиться после
   build (Debug). kubb `predev` хук упадёт с подсказкой, если spec отсутствует
   — поэтому fail-fast ДО `output.clean` (см.
   [openapi-codegen.md](./operations/openapi-codegen.md)).

## Ключевые решения (дельта от старых docs)

| Решение | Что |
|---|---|
| Анализаторы | ТОЛЬКО IDE code-style + CA Security; MA/RCS/VSTHRD удалены |
| Формат-гейт | `platform/build/Comuki.Build.Tools` — verify hidden, эскейпы `-p:DisableFormatOnBuild` / `FormatOnBuildTreatAsWarning` |
| IDE0010/IDE0072 off | вписывают `NotImplementedException` в switch |
| IDE0058 off | иначе фиксер вставляет `_ =` (запрещены глобальным правилом) |
| IDE0022 off (expr-bodied) | методы — только block body |
| Program.cs | top-level, **без** `public partial class Program` — тесты через `internal HostComposer.Compose` + IVT |
| Entity ids | UUIDv7 (PG uuid), строки в API |
| Ключи | `ck_` prefix + HMAC(pepper env); worker token opaque+TTL |
| Postgres schemas | 8 schemas, по одной на DbContext; per-schema `__ef_migrations_history` (#26) |
| Миграции | tool-generated only; `Migrator/Program.cs` цикл `EnsureSchema` → `MigrateAsync` |
| MinIO | `comuki-run-bundles`, `s3://{bucket}/{projectId}/{runId}/...`, 30-day non-current lifecycle (#28) |
| OIDC | `auth:oidc:providers[]`, per-provider scheme + secret env var (#12) |
| OpenAPI | Debug-only emission, csproj rename → `openapi.json` (#29) |
| FE client | kubb v4.39.2 + custom transport; `VITE_API_BASE_URL` required для real-mode |
| Mock-first | `VITE_USE_MOCK=true` (default) → hand-written seeds в `src/shared/api/mock/` |
| Telemetry | `Comuki.Shared.Telemetry` installer, ActivitySource/Meter per assembly, `comuki.*` metric names |
| xUnit v3 | MTP, через `dotnet run --project`, не `dotnet test` |
| Coverage floor | 70% line (BE + FE) |
| slnx | править руками (`dotnet sln add --solution-folder` ломает пути на Win) |
| Folder cap | max 3 .cs files per folder (#25) |

## Осторожно (грабли, уже стреляли)

- `dotnet format --severity hidden` — REVIEW DIFF после фикса
  (см. STATE истории: `NotImplementedException`-arms).
- Локальная ветка `master` удалена (указывала на старый dashboard) — integration = `preparation/translator-001`, push как `HEAD:master`.
- NetArchTest prefix-match: `Comuki.Host.Translator` матчится на `Comuki.Host` — проверять реальные границы.
- Merge-конфликты волны: props (дубли `PackageVersion` → NU1506), slnx (объединять руками), `Host.csproj`.
- **`artifacts/openapi.json` gitignored** — на новом worktree / свежем clone нет
  спек. `dotnet build comuki.slnx -c Debug` регенерирует; без него
  `bun run generate-api` падает. Kubb-config стрипает ДО `output.clean`
  (issue #29, console.x incident 1334 files lost).
- **Kubb-client + kubb generate-API path** — `dotnet` должен быть в PATH
  (а не только вызван по полному пути): `ApiDescription.Server` спавнит
  `dotnet` из PATH иначе падает с exit 127. На macOS:
  `PATH=$HOME/.dotnet:$PATH dotnet build …`.
- **VITE_USE_MOCK / VITE_API_BASE_URL** — в real-mode каждый generated hook
  бросает `[kubb-client] VITE_API_BASE_URL is not set.` при пустом env;
  mock-mode читает `src/shared/api/mock/*` seeds.
- **dotnet → container DNS** — `minio:9000` доступен из Comuki.Host в compose;
  на bare-metal host — `localhost:9000`. `Artifacts:Endpoint` обязателен.
- **Migrator connection string** — `appsettings.json` без `Password=`; deployer
  обязан поставить `COMUKI_DB` или `COMUKI_MIGRATOR_DB_PASSWORD`
  ([install.md](./operations/install.md)).
- **OIDC client secret** — никогда в config. `OidcProviderOptions.ClientSecretEnv`
  указывает на env var с реальным секретом.

## Дальше

S11 (#10) v1.0 polish: security review · load test · onboarding docs sync.
S10 (#9) Knowledge: pgvector · MCP · docs worker.
S9 (#8) Cross-cutting kit + cost/budgets + optional proxy.
S7 (#7) FE dashboard pages — перевести оставшиеся домены с mock seeds.
Post-1.0 (#11): fleet · autonomy · merge-queue · domain-user · eval.
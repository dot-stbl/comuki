---
milestone: v1
status: v1-complete
last_updated: 2026-09-04
progress:
  total_slices: 24
  completed_slices: 24
  percent: 100
  v1_core_slices: "15 (S0–S14, original v1 scope)"
  additional_slices: "9 (5 FE wire-up + 2 polish + 1 admin endpoints + 1 docs)"
  issues_closed: "1,2,3,4,5,6,7,8,9,10,12,13,14,15,16,17,18,19,20,21,22,23,24,25,26,27,28,29"
  issues_open: "11,31,32,33,34,35,36,37,38,39,40,41,42,43"
  open_notes: |
    #11 — Post-1.0 backlog (verify v1.1 · fleet · autonomy · merge-queue · domain-user · eval).
    #31–#42 — FE wire-up of identity + sources admin mutations (backend shipped in
    `feature/admin-backend-fixes`, dashboard mutations stay mock-first until the
    kubb clients land — same pattern as the runs/identity/projects/inbox wire-up).
    #43 — Artifacts e2e test cleanup: drop dead `postgresSeed` container now that
    `8825387` rolled both DBs onto one. v1.1 follow-up, not blocking.
---

# Project State

## Current Position

**v1 milestone is complete.** 28 of 41 GitHub issues are closed; the 13
remaining open issues are **post-v1 scope** (S7 follow-up pages, post-1.0
backlog, one test-fixture cleanup). 24 slices shipped — 15 original v1
core slices plus 9 follow-on slices that landed as the v1 polish work.

**Slice cadence (merge commits on `master`):**

| Slice | Issue | Title | SHA |
|---|---|---|---|
| S0 | #1 | Skeleton platform/: shared · modules · engine · host | landed |
| S1 | #2 | Runs · queue · journal | landed |
| S2 | #3 | Compute: Docker provider + scale v0 | landed |
| S3 | #4 | Translator · worker image · gRPC (Slice 0 e2e) | landed |
| S4 | #12 | Identity: users · API keys · RBAC · OIDC | landed (Wave 5, `feature/audit-rules-batch-d`) |
| S5 | #5 | Chat (Voluta в Host) + Host.Brain + approve + cancel | landed (approve/cancel: `b92d070`) |
| S6 | #6 | Intake: GH · GL · Yandex Tracker · Jira + sync-back + PR-review | landed |
| S7 | #7 | FE ядро + SignalR realtime | landed (wire-up slices 1–5) |
| — | #13 | Compute k8s + quotas + observability | closed in Wave 6 |
| S9 | #8 | Cross-cutting kit + cost/budgets + optional proxy | landed (`9566546`) |
| S10 | #9 | Knowledge (opt-in): pgvector · MCP · docs worker | landed (`38cfabf`) |
| S11 | #10 | v1.0 polish: security · load · onboarding · docs sync | landed (`532e94d`) |
| S12 | #14 | agents/ TS-пакеты: agent-core · worker-sdk · dev-sdk | landed |
| S13 | #15 | control-plane дефолты: профили · каталог · chat-commands | landed |
| S14 | #16 | CI: GitHub Actions | landed |

> Note: S8 (#13) was originally scoped but its deliverable (Kubernetes compute
> provider, FreeSlots ScalePolicy, OTel business spans, Grafana as-code) was
> folded into Wave 6 alongside S9 cost work; issue #13 is closed.

**Follow-on v1 slices (post-original 15):**

| # | Title | SHA |
|---|---|---|
| wave-6 polish | #17–#25 (per-endpoint problem-details, IExceptionHandler, schema-per-DbContext, folder cap, Dto suffix, costs permissions, dev-secret removal) | landed (Aug 31 – Sep 3) |
| artifacts | #28 — MinIO run-artifact bundle (brief/result/pins) | `f8425ea` |
| openapi-emission | #29 — build-time OpenAPI + kubb alignment | `5b10e23` |
| pr-review | #27 — admit GH/GL pull requests as tickets + pr-review profile | `d467703` |
| fe-wire-runs | slice 1: kubb-client transport, runs queries, mappers | `b8a2407` |
| fe-wire-identity | slice 2: login/me/oidc, kubb client + mappers | `fcafe09` |
| fe-wire-projects | slice 3: projects queries/mutations wired | `6ff578c` |
| fe-wire-inbox | slice 4: inbox queries/mutations wired | `c760569` |
| fe-wire-oidc | slice 5: browser-driven OIDC start + callback | `9f731f6` |
| admin-endpoints | 12 host endpoints (#31–#42 BE) + tests + openspec requirements | `7dc3803` + `feature/admin-backend-fixes` (`6f644e1`) |

## Что живёт (после 2bb2afd)

### Backend (C# / .NET 10)

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
  → IQueryable; kubb-exposed filter types via OpenAPI transformer).
- **10 модулей** в `platform/src/modules/`:
  - **Identity** — RBAC (`RoleMatrix`/`RoleKeys` в коде, `ck_` API keys с
    HMAC pepper, OIDC linker с per-provider схемами + `OidcAccountLinker`,
    bootstrap admin, 7 admin endpoints #31–#37).
  - **Projects** — CRUD + per-project settings с live-reload, бюджеты и
    concurrency caps (`ProjectSettingsCacheRefresher`).
  - **Chat** — Voluta-graph integration в Host, checkpoints +
    `chat_sessions` / `chat_messages` storage, slash-commands.
  - **Memory** — long-term facts с pgvector (`SourceDocument` +
    `MemoryEmbedding` entities, raw-SQL managed embeddings),
    learning-candidate queue, chat checkpoints в `memory` schema.
  - **Intake** — GH/GL/Yandex Tracker/Jira источники, dedupe, sync-back
    outbox, GH/GL PR-review профиль (issue #27), 5 admin endpoints #38–#42
    (probe + nested rules + connect/update/test-draft/test-connection).
  - **Costs** — `UsageRecorder` под `IBudgetGate`, project costs view +
    `ProjectBudgetSettingsAdapter` читает лимиты из Projects.
  - **Runs + Workers** — `Comuki.Host.Translator` (AOT-pi-pump, stream-json
    parser, gRPC server) + `Comuki.Host.Grpc` worker service +
    `WorkerTokenAuthenticator` (opaque TTL) + `HostApproveRunAdapter` /
    `HostCancelRunAdapter` (approve/cancel: `b92d070`).
  - **Artifacts** — `MinioRunArtifactStore` + `RunArtifactPackager` polls
    terminal runs, writes `{projectId}/{runId}/{brief,result,pins}.json`
    bundle in MinIO; `ArtifactBucketInitializer` BackgroundService создаёт
    bucket idempotent на старте (`8825387`).
  - **Proxy** *(S9)* — `Comuki.Modules.Proxy` (resolver, store,
    extractors, budget, meter) + `Comuki.Host.Proxy` YARP
    OpenAI/Anthropic passthrough + virtual-key HMAC (models/budget/expiry)
    + metering → `usage_events`.
  - **Knowledge** *(S10)* — `Comuki.Modules.Knowledge` (embedder /
    chunker / ingestor / searcher) + pgvector schema + MCP JSON-RPC 2.0
    endpoint на host (`/api/v1/mcp` с tools `search_knowledge` +
    `list_runs`) + `/api/v1/knowledge/ingest` за `knowledge:write`
    permission.
- **Host endpoints** (current):
  - `/health` (liveness) · `/api/v1/health/{postgres,proxy}` (readiness с
    per-probe results, `2f01819`)
  - `/api/v1/auth/{login,logout,me,oidc/{provider}/start,oidc/{provider}/callback}`
    + bootstrap admin
  - `/api/v1/projects` + settings + budgets
  - `/api/v1/runs` + approve/cancel (`b92d070`) +
    `/api/v1/runs/{id}/artifacts`
  - `/api/v1/chat/sessions` + slash
  - `/api/v1/intake/{admission-rules,inbox,sources,tickets,webhooks}` +
    sources admin (`POST/PUT/POST-{probe,test-draft,test-connection}` →
    `/api/v1/sources/{id}/{connect,update,probe,test-draft,test-connection}`)
  - `/api/v1/costs/projects/{id}`
  - `/api/v1/controlplane/{profiles,chat-commands}`
  - `/api/v1/identity/{users,keys,grants}` admin (7 endpoints)
  - `/api/v1/knowledge/ingest`
  - `/api/v1/mcp` (JSON-RPC 2.0)
  - `/api/v1/workers/claim|complete|fail` (gRPC-compatible HTTP) +
    `/api/v1/host/grpc` для Translator.
  - `HostComposer` (internal, IVT для тестов) — central `IExceptionHandler`
    registered (`a01f4a0`, issue #17).
- **Background services**: `RunArtifactPackagerHostService`
  (Scoped-lifetime, two-phase poll, `8825387`),
  `OidcStateSweeper` (5-min interval, configurable TTL,
  `Host:OidcSweep:{Enabled,Interval,StateTtl}`, `40fca53`),
  `ArtifactBucketInitializer` (idempotent bucket create at startup).
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

- **5 доменов на реальный backend** (slices 1–5): runs · identity
  (session: login/me/oidc) · projects · inbox · OIDC start. FE генерирует
  kubb client из `artifacts/openapi.json`; per-domain mappers из wire в
  domain.
- **Mock-first домены (post-v1 follow-up)**: identity admin mutations +
  sources admin pages (`#31–#42`), tasks, models, observability, verify,
  cost, compute, knowledge, queue, approvals, settings, home.
  Mutations бросают loud error в real-mode, read path пустой —
  misconfigured `VITE_USE_MOCK=false` лендит на empty-state, не на
  phantom success.
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
- **Tests** — `bun run test` → **134 файла, 1525 тестов pass** (`2026-09-04`).
  Mock-режим (`VITE_USE_MOCK=true`) не требует `VITE_API_BASE_URL`;
  real-mode throws на первом hook call без base URL.

### Хранилища

- **Postgres** — 9 schemas, по одной на DbContext: `orchestration`,
  `identity`, `projects`, `memory`, `chat`, `intake`, `costs`, `artifacts`,
  `knowledge` (issue #26 + #9). Каждая schema имеет собственную
  `__ef_migrations_history` таблицу; `Comuki.Migrator/Program.cs` цикл
  `EnsureSchema` → `MigrateAsync` per context.
- **MinIO (S3)** — `comuki-run-bundles` бакет, ключи
  `{projectId}/{runId}/{brief,result,pins}.json`; compose `minio-init`
  job создаёт бакет + 30-day non-current-version lifecycle; bucket
  auto-init в host (`8825387`) → idempotent.
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
  Includes integration for runs/intake/identity/oidc/costs/proxy/chat/errors/
  realtime/artifacts/realtime/migrations/stores + arch tests
  (`Comuki.Architecture.Tests`) + load (`tests/load/k6`).
- **Frontend** — vitest 4.1.x + Testing Library + jsdom; 134 test files,
  1525 tests pass.
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
   На `2026-09-04`: typecheck ok, lint ok, 1525/1525 tests pass.
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
| Postgres schemas | 9 schemas, по одной на DbContext; per-schema `__ef_migrations_history` (#26 + #9) |
| Миграции | tool-generated only; `Migrator/Program.cs` цикл `EnsureSchema` → `MigrateAsync` |
| MinIO | `comuki-run-bundles`, `s3://{bucket}/{projectId}/{runId}/...`, 30-day non-current lifecycle (#28); bucket auto-init на старте хоста (#28 follow-up) |
| OIDC | `auth:oidc:providers[]`, per-provider scheme + secret env var (#12); `OidcStateSweeper` чистит expired states каждые 5 минут (`Host:OidcSweep:*`) |
| OpenAPI | Debug-only emission, csproj rename → `openapi.json` (#29) |
| FE client | kubb v4.39.2 + custom transport; `VITE_API_BASE_URL` required для real-mode |
| Mock-first | `VITE_USE_MOCK=true` (default) → hand-written seeds в `src/shared/api/mock/` |
| Telemetry | `Comuki.Shared.Telemetry` installer, ActivitySource/Meter per assembly, `comuki.*` metric names |
| xUnit v3 | MTP, через `dotnet run --project`, не `dotnet test` |
| Coverage floor | 70% line (BE + FE) |
| slnx | править руками (`dotnet sln add --solution-folder` ломает пути на Win) |
| Folder cap | max 3 .cs files per folder (#25) |
| Proxy | YARP passthrough on Host:Proxy, virtual-key HMAC, optional (`Proxy:Enabled=false` → off, `T9.6`) |
| Knowledge | pgvector в schema `knowledge`, MCP JSON-RPC 2.0 на host (`/api/v1/mcp`), `knowledge:write` permission для ingest (#9) |

## Осторожно (грабли, уже стреляли)

- `dotnet format --severity hidden` — REVIEW DIFF после фикса
  (см. STATE истории: `NotImplementedException`-arms).
- Локальная ветка `master` удалена (указывала на старый dashboard) — integration = `preparation/translator-001`, push как `HEAD:master`.
- NetArchTest prefix-match: `Comuki.Host.Translator` матчится на `Comuki.Host` — проверять реальные границы.
- Merge-конфликты волны: props (дубли `PackageVersion` → NU1506), slnx (объединять руками), `Host.csproj`.
- **`artifacts/openapi.json` gitignored** — на новом worktree / свежем clone нет
  спека. `dotnet build comuki.slnx -c Debug` регенерирует; без него
  `bun run generate-api` падает. Kubb-config стрипает ДО `output.clean`
  (issue #29, console.x incident 1334 files lost).
- **Kubb-client + kubb generate-API path** — `dotnet` должен быть в PATH
  (а не только вызван по полному пути): `ApiDescription.Server` спавнит
  `dotnet` из PATH иначе падает с exit 127. На macOS:
  `PATH=$HOME/.dotnet:$PATH dotnet build …`.
- **VITE_USE_MOCK / VITE_API_BASE_URL** — в real-mode каждый generated hook
  бросает `[kubb-client] VITE_API_BASE_URL is not set.` при пустом env;
  mock-mode читает `src/shared/api/mock/*` seeds. Identity-admin mutations
  и sources-admin (mock-first, #31–#42) — real-mode read тоже throws.
- **dotnet → container DNS** — `minio:9000` доступен из Comuki.Host в compose;
  на bare-metal host — `localhost:9000`. `Artifacts:Endpoint` обязателен.
  `Artifacts:AutoCreateBucket=true` создаёт bucket на старте
  (idempotent).
- **Migrator connection string** — `appsettings.json` без `Password=`; deployer
  обязан поставить `COMUKI_DB` или `COMUKI_MIGRATOR_DB_PASSWORD`
  ([install.md](./operations/install.md)).
- **OIDC client secret** — никогда в config. `OidcProviderOptions.ClientSecretEnv`
  указывает на env var с реальным секретом.
- **OIDC discovery parsing** — Keycloak 26+ шлёт bool-поля, MS
  `OpenIdConnectConfiguration` ожидает strings — host
  hand-parses discovery (`37c8bb1`).
- **Artifacts e2e isolation** — `Pooling=false` + fresh scope per phase +
  per candidate (`8825387`). 2-container fixture dead weight after that
  fix (`#43` follow-up).
- **`RunArtifactPackagerHostService` lifetime** — Singleton → Scoped
  (`8825387`): per-cycle scope даёт свежий packager, иначе shared state
  гоняет races между phases.
- **Test suite under xUnit v3** — `dotnet test` (VSTest) не видит
  discoveries, MTP через `dotnet run --project <test>` обязателен.

## Дальше

Open issues = post-v1 scope; v1 ships.

- #11 — Post-1.0 backlog (verify v1.1 · fleet · autonomy · merge-queue ·
  domain-user · eval · Redis cache при multi-replica Host).
- #31–#42 — FE wire-up of identity admin + sources admin mutations
  (dashboard pages есть, mutations mock-first).
- #43 — Artifacts e2e test cleanup (drop dead `postgresSeed` container).
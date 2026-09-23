---
milestone: v1 (shipped) → v2 (mission-cowork epic drafted, 0 built)
status: v2-epic-drafted
last_updated: 2026-09-23
openspec_changes_in_flight:
  - harden-pi-worker-sandbox (15/27 tasks; issue #121 + follow-up #125; actively landing)
  - enrich-chat-parts (22/24 tasks; near done — Testcontainers suite + spec sync remain)
  - agent-runtime-capabilities (planning complete, awaiting /opsx-apply — unchanged since 2026-09-15)
  - add-mission-cowork (issue #70; v2 umbrella epic, proposal/design/tasks complete, 0/131 built;
    18 child stubs #87-#105 are empty .openspec.yaml only — real spec content still lives in
    add-mission-cowork itself, decomposition is future work)
progress:
  v1_core_slices: "15 (S0–S14, original v1 scope)"
  additional_slices: "9 (5 FE wire-up + 2 polish + 1 admin endpoints + 1 docs)"
  issues_total: 50
  issues_closed: 50
  issues_open: 0
  cli_rebuild_epic: "shipped — issue #71 (15 sub-issues #72-#85), closed 2026-09-19 to 2026-09-21"
  mission_cowork_epic: "drafted only — issue #70, 19 phases / 131 tasks, 0 built"
  master_tip: 86d15e10
  openapi_emission: artifacts/openapi.json
  be_tests: "2075 across 37 unit-category projects (2073 pass, 2 known-fail — see Tests)"
  fe_tests: "dashboard 2032 pass; agents/ 155 pass; cli 1433 pass / 4 fail (Windows-only path bug)"
  rule_bootstrap: |
    Agent onboarding ritual enforced by three machine-checkable artefacts
    (see `.agents/RULES-BOOTSTRAP.md`):
      - [VerifyRuleAwareness] MSBuild target — prints rule corpus at build start.
      - [SelfAuditReport] MSBuild target — writes audit-data/last-commit-audit.md.
      - dashboard/scripts/rule-audit.ts — FE mirror, wired into predev/prebuild.
    All three are WARNING ONLY (MSBuild <Message> is not promoted by
    TreatWarningsAsErrors). Skip is a load-gate skip, not a courtesy slip.
    Disable: -p:DisableRuleAwareness=true / -p:DisableSelfAuditReport=true
---

# Project State

## Текущая позиция (2026-09-23)

**v1 шипнут** (детали ниже, master `fa659fd`, 2026-09-08). После него
прошли четыре волны работы:

1. **CLI rebuild epic (issue #71, закрыт).** 15 под-issues (#72-#85):
   harness engine + reducer, OpenTUI как дефолтный `--tui` (`b75eb179`,
   2026-09-21), risk-tiered approvals, swarm canvas, a11y linear
   renderer, machine-режим (`--json`/`--ndjson`), generated-contracts
   pipeline (kubb HTTP types + realtime codegen), reconnectable
   sessions, single-file packaging + телеметрия. Закрыт 2026-09-19 →
   2026-09-21. **`comuki-cli` физически переехал `agents/comuki-cli` →
   `cli/` 2026-09-17 (`a2bddc27`)** — отдельный активно растущий пакет
   (458 файлов / ~64.6K LOC), больше не под `agents/`. Следующий
   инкремент — issue #105 `rewrite-cli-for-shared-contracts`
   (strangler-миграция на `HarnessEngine` + сгенерированные контракты,
   внешние ADR-0002/0003); в openspec только один plan-документ
   ("step 1"), а сам step 1 уже реализован (`4687613c`/`f29a2415`,
   2026-09-21) — код обогнал план.
2. **`harden-pi-worker-sandbox` (issue #121 + follow-up #125), в работе
   сегодня.** Floor для isolation class `strong`: default-deny egress
   (Docker fenced network + K8s NetworkPolicy, `eb87e5b6`), минтинг
   virtual key на claim + стемпинг в `pi` (`bd61e47f`), worker CPU/mem
   limits (issue #124, закрыт). 15/27 задач `tasks.md`. Осталось:
   git-clone-based workspace prep (без `docker.sock`), journal
   conditions + artifact drain, operator exec opt-in, image-pin-by-digest,
   и `agents/comuki-worker-sdk/src/pi-extensions/` (задачи 6.1-6.3) —
   сегодня локи внутри воркера **не действуют вообще** (только dev-sdk
   hooks на стороне разработчика).
3. **v2-эпик `add-mission-cowork` (issue #70), расписан — код не
   начат.** 19 фаз / 131 задача в
   `openspec/changes/add-mission-cowork/tasks.md` (Missions, standalone
   Tasks, Capability Broker, Context Fabric, тёплые worker pools,
   dashboard/CLI паритет). **BREAKING**: Run перестаёт быть durable-
   целью. Из него объявлено 18 дочерних openspec-стабов (issues
   #87-#105) — каждый пока только `.openspec.yaml`, без proposal/tasks;
   реальный spec-контент живёт в самом `add-mission-cowork`,
   декомпозиция — будущая работа. `openspec validate --all`: 27 pass /
   23 fail (стабы + пара changes без `specs/`-дельты).
4. **Audit fixes U1-U7 landed** (2026-09-16→17): `canon.mjs`
   directory-expansion fix, `TimeProvider` в
   `CreateApiKeyRequestValidator`, status enums → smart-types +
   `OrchestrationDomainException`, `MergeQueue`/`MergeBatch` split в
   commands/handlers, `ApiRoutes`-константы в контроллерах, filtering
   doc-tag cleanup, `[hybrid]` → `[.stbl]` commit-prefix correction в
   `.agents/` докам.

Master tip: `86d15e10` (2026-09-23). 28 открытых GitHub issues на
момент проверки — не дублируем список здесь, `gh issue list -R
dot-stbl/comuki` (#87-#105 = openspec-стабы выше; #121/#125 = sandbox;
#100-104 = поздние mission-cowork фазы; #51/#53/#65/#66 = старый мелкий
backlog). Тестовые числа — раздел "Tests" ниже.

## Tests (2026-09-23, master `86d15e10`)

Real counts from a full read-only run (audit report, same date). Numbers
below supersede any older count elsewhere in this file.

- **Backend** — xUnit v3 / MTP, **2075 tests, 2073 pass, 2 fail**, across
  all 37 unit-category projects (36 `tests/unit/*` + `Comuki.Architecture.Tests`,
  38/38 green). The 2 failures are both `Comuki.Host.Brain.Unit`
  `DeploymentProfileCatalogShould` — real drift between `deploy/hybrid/infra-dev.yaml`
  (missing `COMUKI_BRAIN_CONTROLPLANEPROFILESPATH`) and
  `deploy/helm/templates/brain.yaml` (sets it correctly), plus a second
  Dockerfile/test contract mismatch on the `COPY` scope for control-plane
  profiles — not a stale test, a live deployment-manifest bug worth
  fixing. All 22 `tests/integration/*` (Testcontainers) build clean but
  were not run in that pass (no Docker in the audit shell); they do run
  in GitHub Actions CI.
- **Frontend `dashboard/`** — vitest, **176 files, 2032 tests pass**.
- **`agents/`** (3 TS SDKs) — bun test, **155/155 pass** (11 files).
- **`cli/`** — bun test, **1433 pass, 4 fail** (131 files). All 4
  failures are one root cause: `cli/scripts/export-bundle.test.ts`
  hard-codes `/tmp/test-tar.tar.gz` and shells out to `tar`; on Windows
  Git-Bash `tar` reads `C:\...` as `host:path`. Portability bug in the
  test (should use `os.tmpdir()`, already imported), not a logic bug —
  likely green on Linux CI.
- **CI** — two independent pipelines exist: GitHub Actions
  (`.github/workflows/ci.yml`) runs a 2-3 project subset of unit tests
  plus the **full** integration suite; the GitLab deploy pipeline
  (`deploy/hybrid/ci.yml`, gitignored on GitHub) that actually promotes
  images to `dev` runs 3 hand-picked unit suites and a migration-
  idempotency check — **no integration or E2E stage** before
  `promote:dev`. See `.agents/docs/audits/testing-audit-report.md`
  (2026-09-09) for the fuller gap analysis; most of its P1 findings
  (Testcontainers-per-class cost, zero `Respawn` usage, duplicated fake
  helpers) were still open as of 2026-09-23.

## v1 — историческая точка (шипнут 2026-09-08)

**v1 milestone is complete and shipping.** All 50 GitHub issues closed
(0 open). 24 slices shipped — 15 original v1 core slices plus 9 follow-on
slices landed during v1 polish. Master tip `fa659fd` (2026-09-08).

**Slice cadence (merge commits on `master`):**

| Slice | Issue | Title | SHA |
|---|---|---|---|
| S0 | #1 | Skeleton platform/: shared · modules · engine · host | landed |
| S1 | #2 | Runs · queue · journal | landed |
| S2 | #3 | Compute: Docker provider + scale v0 | landed |
| S3 | #4 | Translator · worker image · gRPC (Slice 0 e2e) | landed |
| S4 | #12 | Identity: users · API keys · RBAC · OIDC | landed |
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

**Post-1.0 backlog slice (#11) — closed 2026-09-07 with 13 sub-slices landed:**

| Sub-slice | SHA | Description |
|---|---|---|
| Merge-queue entity | `6072dd9` | MergeQueue aggregate + IMergeQueueStore + AddMergeQueueTable |
| Status-machine golden-replay tester (historically mislabeled "Eval-harness") | `7989779` | `EvalRunner` + 7 golden `Golden/0N-*.json` fixtures + JSON parser + Markdown writer — a deterministic replay of `Create`/`Transition` ops against the pure Run/WorkItem domain, asserting `transitionLog`/`finalStatus`. **Not** an agent/model-quality eval — no LLM, no rubric, no golden *tasks for the brain or pi worker*. See `tests/unit/Comuki.Engine.Orchestration.Unit.Eval/`. |
| Autonomy ratchet (slice 1) | `6f2ddb8` + `3f769f5` | RunTrustClass enum + TrustClassRatchetSweeper + AddRunTrustClass migration |
| Domain-user intake (slice 1) | `1ac0550` | DomainTypeAdmission EF + gate service + AddDomainTypeAdmissions |
| C#→TS codegen (Option A) | `77561c9` → `0aeae3e` | RealtimeContractAttribute + RealtimeContractEmitter + contracts in Shared.Contracts |

**Verified absent from the current tree (2026-09-23) despite being listed
"shipped" in earlier revisions of this file — do not cite as done:**
- **Redis cache** (`Comuki.Shared.Redis` + `DistributedProjectSettingsCache`)
  — no `Comuki.Shared.Redis` project exists anywhere in `platform/`;
  `ProjectSettingsCacheRefresherComukiWorker`'s own XML doc still calls
  Redis "planned... when it lands," i.e. future tense, not shipped. The
  live cache is DB-backed with an in-memory fallback snapshot + TTL.
- **Fleet runners** (`IRunnerRegistry` / `EfRunnerRegistry`) — zero
  matches anywhere in `platform/src`.
- **Generic-command verifier / `Comuki.Modules.Verify`** — the module
  was built (`ec3ce24`, `493704c`) but later dropped as an unbuildable
  skeleton (`chore(slnx): drop unbuildable Verify module skeleton`); it
  does not exist in `platform/src/modules/` or `comuki.slnx` today.
  `GenericCommandRun` has zero matches anywhere in the tree.

**Deferred to v2 (4 issues closed as deferred, not in v1.1):**
- #47 Generic-command runner-container (Process.Start isolation)
- #48 Fleet runner host-agent for bare-metal
- #49 Autonomy ratchet continuation (confidence + escalation)
- #50 Merge-queue multi-feature batch + dependency ordering

## Что живёт (master `fa659fd`, 2026-09-08)

### Backend (C# / .NET 10)

- **Каркас**: `platform/src/{shared,modules,engine,host}` + `platform/build`
  (format gate + `dotnet format --severity hidden`).
- **Engine**: `Comuki.Engine.Orchestration` (runs / queue / claim-lease
  `SKIP LOCKED` / journal / reaper / TrustClass ratchet) ·
  `Comuki.Engine.Compute` (Docker + Kubernetes providers,
  `KubernetesComputeProvider` использует `batch/v1 Job` с
  `backoffLimit=0` / `ttlSecondsAfterFinished`, ScaleSupervisor cycle).
- **Shared**: `Comuki.Shared.Kernel` (ids, exceptions, subject scoping,
  secrets abstraction) · `Comuki.Shared.Contracts` (gRPC, brain, queue,
  journal, plans, memory, control-plane, realtime) ·
  `Comuki.Shared.Telemetry` (ActivitySource + Meter,
  `AddComukiTelemetry()` installer) · `Comuki.Shared.Filtering` (DSL parser
  → IQueryable; kubb-exposed filter types via OpenAPI transformer) ·
  `Comuki.Shared.Bootstrap` (host composition: CLI, config, correlation,
  logging, versioning, worker registry) · `Comuki.Shared.Migrations`
  (cross-module DbContext list for the Migrator). **No `Comuki.Shared.Redis`
  project exists** — the settings cache is DB-backed with an in-process
  fallback snapshot; Redis is future work, not shipped (see the "Verified
  absent" note above).
- **10 модулей** в `platform/src/modules/` (1:1 с `comuki.slnx`):
  - **Identity** — RBAC (`RoleMatrix`/`RoleKeys` в коде, `ck_` API keys с
    HMAC pepper, OIDC linker с per-provider схемами + `OidcAccountLinker`,
    bootstrap admin, 7 admin endpoints #31–#37.
  - **Projects** — CRUD + per-project settings с live-reload, бюджеты и
    concurrency caps (`ProjectSettingsCacheRefresherComukiWorker` через
    DB-backed cache + in-process fallback snapshot; Redis не введён).
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
    extractors, budget, meter); YARP OpenAI/Anthropic passthrough runs
    **in-process inside `Comuki.Host`** (`Comuki.Host/Proxy/*Endpoints.cs`)
    — there is no separate `Comuki.Host.Proxy` project — + virtual-key
    HMAC (models/budget/expiry) + metering → `usage_events`.
  - **Knowledge** *(S10)* — `Comuki.Modules.Knowledge` (embedder /
    chunker / ingestor / searcher) + pgvector schema + MCP JSON-RPC 2.0
    endpoint на host (`/api/v1/mcp` с tools `search_knowledge` +
    `list_runs`) + `/api/v1/knowledge/ingest` за `knowledge:write`
    permission.
  - **Scheduler** — `Comuki.Modules.Scheduler` (cron + sentry observability
    via `scheduler.scheduled_jobs` table, `ScheduledJobDispatcherWorker`
    polls via `FOR UPDATE SKIP LOCKED`, fires ephemeral workers; S15 issue
    #44; см. [operations/scheduler.md](../docs/operations/scheduler.md)).
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
  `ArtifactBucketInitializer` (idempotent bucket create at startup),
  `TrustClassRatchetSweeper` (autonomy ratchet sweeper).
- **OpenAPI emission** — `Microsoft.AspNetCore.OpenApi 10.0.9` +
  `Microsoft.Extensions.ApiDescription.Server` спавнят `GetDocument.Insider`
  при `dotnet build` (Debug only); csproj target
  `RenameOpenApiOutputToCanonicalName` переименовывает
  `Comuki.Host.json` → `openapi.json`. `OpenApiBuildTimeExtensions` стрипат
  hosted services во время инспекции (issue #29).
- **SignalR `/realtime/runs`** — `RunsHub` (JoinRun/JoinProject +
  permissions) + `RunEventsBroadcastInterceptor` пушит journal events;
  `EnableDetailedErrors` отключён в production (issue #19).
- **C#→TS realtime contracts** — `RealtimeContractAttribute` + source-gen
  `RealtimeContractEmitter` живёт на BE в `Comuki.Shared.Contracts.Realtime`
  (отдельный unit-проект; FE-сторона codegen ещё не подключена — slice
  отложен до v2).

### Frontend (`dashboard/`)

- **Реальный backend, оба branch'а реализованы (2026-09-23, сильно шире
  исходных 5 доменов):** runs · approvals (runs + learning candidates) ·
  queue (workers, drain/stop) · sources (CRUD, probe, rotate-secret,
  rules) · projects (+ scheduled-jobs) · identity (users/grants/keys) ·
  auth (`/auth/me`, OIDC) · inbox/tasks (claim, tickets) · chat
  (sessions, messages, slash) · knowledge (documents, search) · models
  (proxy keys list/revoke) · artifacts (visual list — но
  `FetchAsync` пока всегда возвращает `[]`, backend placeholder). FE
  генерирует kubb client из `artifacts/openapi.json`; per-domain
  mappers из wire в domain.
- **Смешанные/частичные (read реальный, write мок или наоборот):**
  compute (registry read реальный; take-work/retire throw — эндпоинтов
  нет в spec) · cost (real-mode дергает `/projects/{id}/costs`, но
  **выбрасывает результат** и возвращает seed — платформенный rollup
  не отдаёт backend, "issue Q3/v1.1") · settings (`GET` реальный; PUT
  в spec нет вообще, все мутации throw) · home (список ранов реальный;
  outcomes throw, эндпоинта нет) · models (enable/disable toggle
  принципиально не может работать — ключи config-seeded и immutable,
  UI прячет его в real-mode).
- **Только мок, backend-аналога нет вовсе:** observability, verify
  (целый gate-домен с UI-сообщением "gate not connected"). Ни у одного
  нет пути в OpenAPI spec.
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
- **Tests** — `bun run test` → **176 файлов, 2032 теста pass** (`2026-09-23`,
  ~150K LOC в `src/`, 19 доменов). Mock-режим (`VITE_USE_MOCK=true`, до
  сих пор default) не требует `VITE_API_BASE_URL`; real-mode throws на
  первом hook call без base URL. `predev` гоняет полный `dotnet build`
  даже в mock-режиме (нужен spec для kubb); `prebuild` — только
  `audit:fe` (`dashboard/scripts/rule-audit.ts`, warning-only).

### Хранилища

- **Postgres** — 10 schemas, по одной на DbContext: `orchestration`,
  `scheduler`, `identity`, `projects`, `memory`, `chat`, `intake`,
  `costs`, `artifacts`, `knowledge` (issue #26 + #9 + #44 Scheduler).
  Каждая schema имеет собственную `__ef_migrations_history` таблицу;
  `Comuki.Migrator/Program.cs` цикл `EnsureSchema` → `MigrateAsync`
  per context.
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

- **Backend** — xUnit v3 + MTP (не VSTest), 1567 tests pass
  (1354 unit + 213 integration). Pre-existing flakes fixed in
  merge(fixes) batch 2026-09-08: StatusMachine PromoteTrustedIsNoOp
  (test race on `Run.UpdatedAt`), Scheduler ExecuteDeleteAsync
  (InMemory compat via Find+Remove), Runs EscalationTimeoutSweeper
  (test seed cross-pollution), Scheduler CreateAsync exception type
  mismatch (`FormatException` wrapped in `InvalidCronExpressionException`),
  OIDC Keycloak integration tests (skipped on WSL2 — Docker Postgres
  unreachable). Remaining: `Comuki.Modules.Verify.Unit` ships 0 tests
  (build green, no coverage). Includes integration for runs/intake/
  identity/oidc/costs/proxy/chat/errors/realtime/artifacts/migrations/
  stores + arch tests (`Comuki.Architecture.Tests`) + load
  (`tests/load/k6`).
- **Frontend** — vitest 4.1.x + Testing Library + jsdom; 136 test files,
  1560 tests pass.
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
   На `2026-09-23`: typecheck ok, lint ok, 2032/2032 tests pass (176 файлов).
4. Agents TS: `cd agents && bun install && bun run typecheck && bun test`.
   На `2026-09-23`: 155/155 pass.
5. CLI (когда тронут): `cd cli && bun run typecheck && bun run lint && bun run test`
   + `test:contracts` (drift-гейт против сгенерированных OpenAPI/realtime
   контрактов, требует `dotnet` в PATH). На `2026-09-23`: 1433/1437 pass —
   4 known-fail — см. раздел "Tests" выше.
6. **OpenAPI emission gate** — `artifacts/openapi.json` должен появиться после
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
| Postgres schemas | 10 schemas, по одной на DbContext; per-schema `__ef_migrations_history` (#26 + #9 + #44) |
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
| Proxy | YARP passthrough in-process inside `Comuki.Host` (no standalone `Comuki.Host.Proxy`), virtual-key HMAC, optional (config section `Proxy`, `Proxy:Enabled=false` → off, `T9.6`) |
| Knowledge | pgvector в schema `knowledge`, MCP JSON-RPC 2.0 на host (`/api/v1/mcp`), `knowledge:write` permission для ingest (#9) |
| TrustClass | enum (Supervised/Trusted/Autonomous) + `TrustClassRatchetSweeper` (passive timeout); future: confidence scoring (#49) |
| Realtime contracts | C#→TS source-gen: `RealtimeContractAttribute` in `Comuki.Shared.Contracts/Realtime/` (Option A); FE-side codegen landed later via `tools/Comuki.Codegen.Realtime` → `cli/src/contracts/_generated/realtime.ts` (dashboard's realtime layer is still hand-written) |

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
  fix (`#43` follow-up, closed).
- **`RunArtifactPackagerHostService` lifetime** — Singleton → Scoped
  (`8825387`): per-cycle scope даёт свежий packager, иначе shared state
  гоняет races между phases.
- **Test suite under xUnit v3** — `dotnet test` (VSTest) не видит
  discoveries, MTP через `dotnet run --project <test>` обязателен.
- **Worktree cleanup (Windows)** — `git worktree remove` падает на
  `Filename too long`. Workaround: `cmd /c "mklink /J C:\wt .agents\worktree"`
  → удаление через `rmdir /S /Q C:\wt\<name>` + `git worktree prune`.
- **PendingModelChangesWarning** — `dotnet ef migrations add` после смены
  RunConfiguration (TrustClass, `3f769f5`); без миграции — все integration
  тесты с `OrchestrationDbContext` падают с warning-as-error.

## Дальше (v2 backlog)

### В работе сейчас

- **`harden-pi-worker-sandbox`** (issue #121 + #125) — 15/27. Осталось:
  git-clone-based workspace prep (без `docker.sock`), journal
  conditions + artifact drain, operator exec opt-in, image-pin-by-digest,
  `agents/comuki-worker-sdk/src/pi-extensions/` (задачи 6.1-6.3).
- **`enrich-chat-parts`** — 22/24. Осталось: Testcontainers integration
  suite + синхронизация дельты в `openspec/specs/chat/spec.md` + архивация.

### Расписано, код не начат

- **`add-mission-cowork`** (issue #70) — 19 фаз / 131 задача,
  `openspec/changes/add-mission-cowork/tasks.md` — единственный source
  of truth для порядка фаз, не дублируем его здесь. Из него объявлены
  18 дочерних change-стабов (issues #87-#105), сейчас пустые
  `.openspec.yaml`; порядок и зависимости между ними — там же в
  `add-mission-cowork/design.md`/`architecture.md`.
- **`rewrite-cli-for-shared-contracts`** (issue #105) — один
  plan-документ ("step 1: HarnessEngine + 2 reducer events"), без
  proposal/tasks triad; сам step 1 уже реализован
  (`4687613c`/`f29a2415`, 2026-09-21).
- **`execution-spine-orchestration`** (#87) и
  **`hard-rename-intake-to-integrations`** (#88) — пустые стабы, но
  оба помечены как ранние/foundational фазы `add-mission-cowork`.

### Закрыто / deferred

- #47 Generic-command runner-container (Process.Start isolation) — closed
- #48 Fleet runner host-agent for bare-metal — closed
- #49 Autonomy ratchet continuation (confidence scoring, daily decay) — closed
- #50 Merge-queue multi-feature batch + dependency ordering — closed

Все 4 deferred.

### v2 — agent runtime capabilities (drafted 2026-09-15)

OpenSpec change
[`agent-runtime-capabilities`](../../openspec/changes/agent-runtime-capabilities/)
drafted; awaiting `/opsx-apply`. Four implementation phases:

| Phase | Capability | Goal |
|---|---|---|
| A | `memory` | Per-project facts at brain call via trusted digest |
| B | `discovery` | MCP `discovery.scan` + `/discover` slash + finding → memory |
| C | `secrets` | `Secret` entity, envelope encryption, RBAC, audit, `DbSecretProvider` |
| D | `compute` + `worker-runtime` | `ComputeStartRequest.SecretRefs` → Docker/K8s env |

KMS / SaaS envelope encryption, auto-rotation, bulk import, external
providers beyond `vault` / `consul` are deferred to follow-ups.

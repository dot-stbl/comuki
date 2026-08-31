# Comuki — распил на задачи v2 (технический)

> v2: каждая таска = файлы · паттерны · имена. Основано на scope-draft +
> project-structure + fe-requirements. Оценка S/M/L.
>
> **Id/ключи:** entity = UUIDv7 (PG uuid, API string) · API key `ck_`+HMAC ·
> worker token opaque+TTL · trace-id = run-id.
>
> Issue map: #1 S0 · #2 S1 · #3 S2 · #4 S3 · #12 S4 · #5 S5 · #6 S6 · #7 S7 ·
> #13 S8 · #8 S9 · #9 S10 · #10 S11 · #11 backlog.

---

## S0 — Скелет platform/ (#1, v0.2)

**T0.1 shared-проекты** (M)
- `Comuki.Shared.Kernel`: `Result`-типы нет (правила: throw+nullable) — только ids (`RunId`, `ProjectId` VO над Guid), `ErrorCodes`-константы
- `Comuki.Shared.Contracts`: порты `IComputeProvider`, `IModelGateway`, `IWorkItemQueue` (пустые файлы-маркеры, наполнение в слайсах)
- `Comuki.Shared.Persistence`: `ComukiDbContextBase` (snake_case convention helper), `DatabaseInformation`-паттерн
- `Comuki.Shared.Configuration`: YAML-binder (YamlDotNet) + env override provider (single-underscore)
- `Comuki.Shared.Telemetry`: `AddComukiTelemetry()` installer (OTLP)
- DoD: build 0/0, installer-цепочка в Host

**T0.2 Engine.Orchestration skeleton** (M)
- перенос `Database.Runs` → `Comuki.Engine.Orchestration.Infrastructure` (Contexts, Migrations)
- `OrchestrationInstaller` (Application) — регистрация handlers
- ns `Comuki.Engine.Orchestration.{Domain,Application,Infrastructure}`
- DoD: Migrator применяет пустую схему

**T0.3 Comuki.Host** (S) — Program.cs, `AddComukiHost()` цепочка, `/health` minimal endpoint, ProblemDetails + один `IExceptionHandler`

**T0.4 Comuki.Migrator** (S) — console host, applies pending migrations + seed

**T0.5 Arch tests** (M) — NetArchTest: host→engine→shared; modules ↛ modules; Domain без EF/ASP.NET

**T0.6 Build-консолидация** (S) — Directory.Build.props пути, csproj-и в slnx по физическим папкам

---

## S1 — Runs · очередь · journal (#2, v0.2)

**T1.1 Домены** (M)
- `Domain/Runs`: `Run` (aggregate), `RunStatus` enum, переходы `RunTransitions` (table-driven)
- `Domain/WorkItems`: `WorkItem`, `WorkItemStatus` (без stalled), `WorkItemDependency`
- `Domain/Journal`: `RunEvent` (type + jsonb payload VO), `EventType` enum
- Unit: матрица переходов (кто может: system/human/worker)

**T1.2 EF + миграции** (M)
- `Infrastructure/Persistence/OrchestrationDbContext`: `runs`, `work_items` (`leased_by uuid?`, `lease_until timestamptz?`, `heartbeat_at timestamptz?`), `run_events` (`payload jsonb`, `occurred_at`), индексы `(run_id, occurred_at)`, partial `WHERE status='queued'`
- snake_case + `HasMaxLength` на всех строках; `id uuid` PK
- DoD: миграция вверх/вниз, Migrator green

**T1.3 Claim/lease** (M)
- Port: `IWorkItemQueue.ClaimAsync(profileKey?, workerLabels)` в Contracts
- `Infrastructure/WorkItemQueueEf`: raw SQL `UPDATE … WHERE id = (SELECT … FOR UPDATE SKIP LOCKED LIMIT 1) RETURNING *` — транзакция, tenant/project фильтр, matч labels (digest/ref)
- `Application/ClaimWorkItemHandler` + FV-валидатор `ClaimWorkItemValidator`
- DoD: two-claimer race test (Testcontainers, 2 параллельных Claim → разные items); протухший lease → re-claim

**T1.4 Journal** (S)
- `AppendRunEvent` (та же транзакция, что статус-переход), `IReadRunTimeline` (стр., по run)
- DoD: integration — claim+event атомарны

**T1.5 State machine** (M) — `RunStatusMachine`/`WorkItemStatusMachine`: явные Allowed-переходы; событие на каждый переход в journal

**T1.6 Reaper** (S) — `LeaseReaperWorker : BackgroundService` (интервал из config), `MarkExpiredAsync` → requeue | failed (политика), TimeProvider inject

---

## S2 — Compute Docker (#3, v0.2)

**T2.1 Порт** (S)
- `Contracts/Compute/IComputeProvider` + `ComputeStartRequest` (image, env, labels, token) + `WorkerHandle`
- `ComputeLabels` константы (`comuki.project/profile/image/ref`)

**T2.2 Docker provider** (M)
- `Comuki.Engine.Compute/Providers/DockerComputeProvider` (Docker.DotNet): create/start/stop/remove, `network=compose`, AutoRemove=false
- Unit с моком клиента + integration против локального docker (smoke)

**T2.3 WorkerTokenIssuer** (S)
- `Security/WorkerTokenIssuer`: `Issue(workItemId, ttl)` → opaque 256-bit; хранение HMAC + expiry (таблица `worker_tokens` или cache); `Validate` на gRPC middleware; revoke по Stop/lease-expire
- Unit: expiry + revoke

**T2.4 Scale v0** (M)
- `ScaleSupervisorWorker`: backlog → desire; правила: create-per-task, `min_idle/max_concurrent` из project settings (таблица `project_settings` live)
- DoD: 3 items в очереди → 3 Start (test с fake provider)

**T2.5 Idle TTL** (S) — sweep idle по `last_claimed_at`, Stop с reason=IdleTtl

---

## S3 — Translator · image · gRPC (#4, v0.2)

**T3.0 pi sanity (риск №1)** (S)
- Запуск pi headless в контейнере `oven/bun`, stream-json, тривиальный prompt — **gate перед T3.3**; при фейле — чек Claude Code headless, эскалация

**T3.1 proto** (M)
- `Grpc/worker.proto`: `Connect(Hello)→Welcome`; client-stream `ReportEvent(EventMsg)`; server-stream `Command(Cmd)`; `EventMsg{kind, payload google.protobuf.Any|string}`; версии — package `comuki.worker.v1`
- Shared contracts csproj: Grpc.Tools generate; TS-типы — вручную (полей мало), codegen позже

**T3.2 Host gRPC server** (M)
- `WorkerGrpcService`: auth middleware (worker token), привязка connection→work item lease, fan-out в journal + SignalR (позже S7), приём Command из Orchestration (`IWorkerCommandPipe`)
- Integration: fake client стримит, Stop доходит

**T3.3 Translator** (L)
- `Comuki.Host.Translator`: `ClaimClient` (HTTP к Host), `ProfilesGitFetch` (LibGit2Sharp/sparse checkout по ref), `PiRunner : IPiRunner` (Process.Start, stdout line-by-line)
- `PiEventMapper` (internal static): строка → `EventMsg` (switch по type, skip malformed + warn) — **ручной, не Mapperly**
- Мапинг команд: gRPC Cmd → SIGTERM/InjectContext файл
- Тест: `TestFakePi` (пишет в stdout сценарий)

**T3.4 worker.Dockerfile** (M) — multi-stage: build Translator → `oven/bun` + pi + comuki-worker-sdk + бинарь; labels digest

**T3.5 E2E** (M) — compose: Host+PG → insert ticket → контейнер → события в журнале → `RunEvent(result)` → lease released. CI-джоба с docker.

---

## S4 — Identity (#12, v0.3)

**T4.1 Модуль + схема** (M)
- `Modules.Identity.{Domain,Application,Infrastructure}`
- Таблицы: `users` (id uuid, email, password_hash?, disabled), `api_keys` (id, user_id, prefix(8), hmac, last_used), `role_assignments` (subject_type, subject_id, role, scope_level, scope_id), `oidc_links` (user_id, provider, sub)
- Domain: `Role` (enum-ключи в коде), `RoleMatrix` (static: Role→IReadOnlySet<PermissionKey>), инварианты выдачи (нельзя выше seniority)

**T4.2 Local login** (M)
- `AuthController` (login/logout), `PasswordHasher<User>` из BCL, cookie scheme (sliding, SecurityStamp-аналог: `tokens_version` в users → validator)
- FV: `LoginValidator`

**T4.3 API keys** (M)
- `ApiKeyAuthenticationHandler` (parse `ck_`, prefix lookup + HMAC compare constant-time), issue-API: показать plaintext 1 раз, revoke
- Permissions: `identity:read/write`

**T4.4 Permissions каркас** (M)
- `Permissions` static class (константы `run:stop` и т.д. — baseline ~24 ключа)
- `IPermissionCatalog` + регистрация в installer; `RequiresPermissionAttribute` + `AsyncResourceFilter` → 403 ProblemDetails `code=permission.denied`
- **Startup validator**: скан контроллеров, каждый ключ ∈ каталог, иначе fail fast
- Scope-фильтр: `ISubjectScope` → EF global query filter по project → out-of-scope = 404

**T4.5 Assignments API** (M)
- CRUD grant/revoke; проверка «не эскалировать»; список моих; scope-пикер
- Маперы: `IAccountMapper` (**Mapperly**, entity→`RoleAssignmentView`)

**T4.6 OIDC** (L)
- `AddOpenIdConnect` по конфигу (список провайдеров в settings, secret в env), `OidcAccountLinker` (sub→user, автосоздание по email с флагом)
- e2e: keycloak в compose (dev), кнопка SSO

**T4.7 Роли seed** (S) — 6 ролей в `RoleMatrix`, seed `platform-admin` из env-инвайта

---

## S5 — Chat + Brain (#5, v0.4)

**T5.0 MEAI spike** (S) — IChatClient → Anthropic-compatible URL (z.ai/MiniMax через hapy) и OpenAI-compat; выбрать провайдер-пакеты, зафиксировать в ADR-note

**T5.1 modules/Chat** (M)
- `ChatSession` (id, project, user, title), `ChatMessage`, stores EF
- Memory: `user_memory` / `project_memory` (key-value facts, upsert человеком/графом, no auto-skills)

**T5.2 Voluta в Host** (M)
- `ChatGraph` (StateGraph): nodes `route → clarify → act → confirm`; interrupt на approve
- Tools (typed, каждый проверяет permission): `list_runs`, `get_run`, `create_ticket`, `stop_run`, `inject`, `switch_project`
- Composer/stream в SignalR-канале chat

**T5.3 Host.Brain** (L)
- `BrainGrpcService` (server), `BrainSession` = agent-loop: IChatClient + tools
- Инструменты мозга: `search_knowledge?` (S10), `list_profiles` (каталог из control-plane git), `list_active_runs`, `read_explorer_report`, `emit_plan`
- Rate/timeout guard, retries с backoff

**T5.4 Plan schema** (M)
- `Plan` JSON Schema (nodes[{profile, brief, dependsOn}], mode A/B/C), C# DTO + `PlanValidator` (FV: ацикличность, профиль существует, brief непустой) — **валидация до исполнения, всегда**

**T5.5 Plan apply** (M) — `ApplyPlanHandler`: plan → run + work_items (+deps), статус waiting-approve → approved; идемпотент по `plan_hash`

**T5.6 Approve flow** (M) — `plan:approve`; live-setting `approve_required` (per project); события в attention

**T5.7 Slash** (M) — каталог built-in (`/init /run /status /stop /plan /project /help`) + `custom_commands` из git клиента (парс `commands/*.md` фронматтер name+description) → autocomplete

**T5.8 /init wizard** (L) — шаги: repo (git url+cred ref) → compute (docker|k8s+ns) → models (base URLs+ключи env) → knowledge on/off → seed profiles → отчёт. Каждый шаг = tool + карточка подтверждения

---

## S6 — Intake (#6, v0.5)

**T6.1 IncomingTicket** (M)
- `Modules.Intake.Domain`: `IncomingTicket`, `SourceConnection` (type, settings jsonb, секрет ref), `AdmissionFilter`
- Native: `POST /tickets` + FV `CreateTicketValidator` + `tickets:write`-permission

**T6.2 Hooks + идемпотентность** (M)
- `HooksController /api/hooks/{source}`: signature verify per-provider (GH HMAC, GL token, YT/Jira зависит), `intake_deliveries(source, delivery_id unique)` insert-first
- `IntakeDedupHandler`; replay-тест

**T6.3–T6.6 Провайдеры** (4×M)
- Refit-клиенты `IGitHubApi`, `IGitLabApi`, `IYandexTrackerApi`, `IJiraApi` (через `AddComukiRefitClient` + resilience)
- Маперы **static**: `GitHubIssueMapper`, `GitLabIssueMapper`, `YtIssueMapper`, `JiraIssueMapper` → `IncomingTicket` (+unit на payload-фикстурах)
- Watch-фильтр v1: labels/projects/projects list (без DSL)

**T6.7 Sync back** (M) — `RunStatusBridge`: статус→transition issue + комментарий (run url); очередь исходящих (outbox-таблица)

**T6.8 Unique active run** (S) — partial unique index + конфликт → 409/событие

---

## S7 — FE ядро + realtime (#7, v0.6)

**T7.1 SignalR** (M)
- `RunsHub`: Join/LeaveRun, JoinProject; `[RequiresPermission]` на методы; `RunEventsBroadcaster` (после append → group `run:{id}`; attention-события → `project:{id}:attention`)
- Contract:typed `HubProtocol` DTO (совпадает с journal read-моделью)

**T7.2 Shell** (L) — routes (code-based TanStack Router), login/local+SSO, project switcher, RBAC: `usePermissions` (из `/me/permissions`) → hide/disable; Kubb-хуки с `openapi-v1.json`

**T7.3 Runs screens** (L) — list (server-фильтры через Query params v1), detail: Overview/Plan/Timeline (SignalR live)/Workers/Artifacts-link; StatusBadge/RunIdChip из DS

**T7.4 Home attention** (M) — cards (approve-waiting/escalated/failed-verify…), действия approve/stop

**T7.5 Inbox+Sources** (L) — inbox list+claim; sources: форма подключения (type, PAT env-ref, base url), watch-фильтры, test-connection, status

**T7.6 Approve UI** (M) — plan card (nodes+edges), approve/reject+reason, request-changes→chat deeplink

**T7.7 Chat UI** (L) — тред+streaming, slash autocomplete (built-in+custom из API), plan-cards, /init как wizard-флоу, permission-gate на tools

FE-тесты: vitest+MSW на каждый экран; handlers по контрактам Kubb-типов.

---

## S8 — k8s + observability (#13, v0.7)

**T8.1 K8s provider** (L)
- `KubernetesComputeProvider` (k8s client): `batch/v1 Job` (backoffLimit=0, ttlSecondsAfterFinished, labels digest+ref), service account, DeleteNamespacedJob на Stop; List через label-selector
- e2e: kind

**T8.2 Quotas/capacity** (M) — `ProjectQuotas` (max concurrent/cpu/mem) + `GetCapacity` (allocatable) в scale-решение; unit на политиках

**T8.3 Observability** (M)
- Business spans: `Claim`, `ApplyPlan`, `Brain.Invoke`, `Compute.Start` (ActivitySource на assembly, теги bounded: profile, project, run_id)
- `deploy/grafana/dashboards/`: comuki-runs/workers/cost + provisioning

---

## S9 — Kit + cost (#8, v0.8)

**T9.1 Filtering** (L)
- Порт в `Comuki.Shared.Filtering`: `FilterQuery`, `FilterLexer/Parser`, `FilterableFieldRegistry` (атрибут `FilterableBy`), `EfFilterTranslator`, `ApplyFilter/ApplySort`
- OpenAPI transformer: filter-параметр + схема полей → Kubb gen на FE
- Догма: только `FilterableBy`-поля; unknown → 400

**T9.2 FV pipeline** (S) — `ValidationBehavior<TRequest>` в dispatch-цепочке (наш CQRS-lite); сбор ValidationProblem; unit

**T9.3 Маперы** (M)
- Mapperly: `IRunsMapper`, `IAccountMapper`, `ITicketsMapper`, `IChatMapper` (RequiredMappingStrategy.Target)
- Static: `PiEventMapper`, `*IssueMapper` (уже), `UsageEventMapper`
- Тест: roundtrip на фикстурах; банить AutoMapper (arch test)

**T9.4 Cache** (M) — `CachedPermissionReader` (IMemoryCache, ключ subject, TTL 30s + invalidate on grant/revoke), `FilterableFieldRegistry` — статик-кеш; bench-смоук

**T9.5 Cost/budgets** (L)
- `usage_events` (tokens, cost, model) от proxy/brain/worker-отчётов → `RunCostAggregator`; budgets (soft/hard) в project settings; hard → Stop+флаг; `cost:read` UI
- DoD: бюджет 1$ → run остановился, событие в attention

**T9.6 Optional proxy** (L)
- `Comuki.Host.Proxy` (YARP): OpenAI+Anthropic passthrough, virtual key (подпись HMAC, claims: models/budget/expiry), `VirtualKeyResolver`, metering → usage_events; можно выключить (env)

---

## S10 — Knowledge (#9, v0.9)

**T10.1 Модуль** (L) — `Modules.Knowledge`: pgvector (`knowledge_chunks` embedding, metadata feature_key, superseded_at), `EmbeddingGateway` (IChatClient embeddings / внешний), retrieval (cosine top-k), MCP-сервер (tools: search_docs, get_feature)
**T10.2 Docs worker** (M) — событие run succeeded → work item profile=docs → бриф (diff+план) → чанк+upsert по feature_key (старое superseded)
**T10.3 Интеграции** (M) — brain tool `search_knowledge`; chat `/init` seed; dev-sdk MCP-подключение докой

---

## S11 — v1.0 polish (#10)

- T11.1 Onboarding-док: fresh checkout → compose → /init → первый run ≤30 мин (прогон руками, фикстура-скрипт)
- T11.2 PRODUCT.md sync (гибкий план, роли, opt-in фичи)
- T11.3 OpenSpec: спеки под scope (runs/queue/intake/identity/chat) + archive старых
- T11.4 Security: hook signatures все провайдеры, pepper/секреты env, token TTL policy, ключи ротация, audit-log назначений
- T11.5 Load: сценарий 50–200 runs (k6 или xUnit load harness) → отчёт + узкие места

---

## Backlog (#11)
verify v1.1 (generic-command: yaml из git, runner container, exit codes) · fleet · autonomy ratchet · merge-queue · domain-user · eval harness · Redis cache · C#→TS codegen контрактов

---

## Сквозные договорённости (упоминать в PR)

- Валидация: FV-валидатор на каждый command/query; DTO запросов — records в Application/Models
- Маперы: Mapperly только entity→HTTP; внешние JSON — static; AutoMapper запрещён
- EF: Specification-паттерн для списков; claim — raw SQL в Infra
- Id: UUIDv7; API-строки; slugs отдельно
- Ошибки: ProblemDetails RFC9457 + stable code (`run.not_found`, `permission.denied`)
- Тесты: unit (Shouldly) → integration (Testcontainers+Refit+WAF) → FE vitest+MSW; Playwright нет

---

## S12 — agents/ TS-пакеты (#новый, v0.2)

**T12.1 workspace** (S) — bun workspaces в `agents/`, tsconfig strict, lint/test scripts
**T12.2 comuki-agent-core** (M)
- `events/` типы (bриф/отчёт/события — руками, зеркалим C# DTO; codegen позже — backlog)
- `protocol/` brief/report форматы + zod-схемы
- `rules/` reader декларативных правил (текст)
- (MCP-клиент добавится в S10)
**T12.3 comuki-worker-sdk** (M)
- pi-extensions «замки»: запрет правки тест-файлов, install, push в main (перехват tools)
- skills loader (из profiles ref)
**T12.4 comuki-dev-sdk** (L, может v0.6) — Claude Code hooks с теми же замками; MCP-подключение к Knowledge; пакет-обёртка

## S13 — control-plane дефолты (#новый, v0.2)

**T13.1 Профили** (M): `control-plane/profiles/`: `explore-readonly`, `implement`, `docs-writer` (system prompt + allowedTools + skills list); формат = markdown+фронматтер
**T13.2 Каталог API** (S): `list_profiles` источник для brain (T5.4) — чтение дефолтов + overlay клиента
**T13.3 chat-commands pack** (S): built-in `commands/*.md` (name+description) для autocomplete (T5.7)
**T13.4 Skills seed** (S): 1–2 обкатанных рецепта как образец формата

## S14 — CI GitHub Actions (#новый, v0.2)

**T14.1 workflows** (M): `build-be` (dotnet build+arch), `build-fe` (bun typecheck/lint/test), `test-be` (xUnit v3 MTP), docker worker image build
**T14.2 cleanup** (S): удалить `.gitlab-ci.yml` (remote перенесён на GitHub), кэши bun/nuget

---

## Расширения слайсов (v2.1 — закрытие дыр)

- **S2 (#3)** + **T2.6 ProjectSettings live API** (M): CRUD квот/min-max/флагов (approve_required, debug, features) + reload без рестарта (store+cache, invalidate); FV-валидаторы; `settings:write`
- **S4 (#12)** + **T4.8 Projects API** (M): CRUD проектов (name, slug, git-профили ref, connections); `project:admin`; список для switcher/`/me/projects`
- **S5 (#5)** + **T5.9 PR comments read** (M): git-provider (Refit переиспользуем из Intake), chat tool `summarize_pr(run)` — read-only
- **S5 (#5)** + **T5.10 Explorer flow** (M): brain может выдать work item профиля `explore-readonly` → отчёт крепится к run → `read_explorer_report` wired
- **S7 (#7)** + **T7.8 Settings/Identity/Cost/Projects/Workers экраны** (L): live-settings формы (T2.6), Identity (users/keys/assignments UI), Cost (T9.5), Projects list/create, Workers/Queue таблица — всё по FE-requirements §4,§11–§14

# Comuki — scope draft (v1.0 + фундамент)

> Слепок обсуждений 2026-08-30 (+ продолжение). Черновик — не заменяет
> `comuki-decisions.md` / `ROADMAP.md`, пока не утверждён явно.
>
> **Процесс:** сейчас фиксируем решения *словами*; техничка (source-gen,
> конкретные библиотеки, схемы) — отдельным проходом.
>
> **FE-требования:** `.agents/docs/product/comuki-fe-requirements.md` (черновик собран).

---

## Определение продукта

**Comuki** ≈ **opencode-as-a-server** с делегированием работы в контейнеры.

**v1.0** = на Comuki можно **с нуля собрать любой другой продукт**.

После v1.0 продуктовых классов пока нет — только `1.x` патчи.
`0.x` — capability-релизы фундамента. Semver внутри минорки.

**Сдвиг относительно старых docs:** OpenAPI-шов, фиксированный каталог
стадий (контракт→бек∥фронт), «verify всегда платформенный» — это не догмы
платформы, а **опции/playbooks проекта**. Платформа даёт примитивы и гибкость.

---

## Ядро из коробки (без project config)

**Must:** Run · containers · events · chat.

Всё остальное — включаемые фичи / плагины / настройки проекта:
knowledge, verify, proxy (optional thin), admission filters, профили, playbooks.

---

## Branding / репо

- GitHub: `dot-stbl/comuki` (origin), MIT © 2026 `.stbl`
- OpenSpec инициализирован (`openspec/`)
- Старый GitLab → remote `gitlab`
- README + `assets/banner.png`

---

## 1. Intake / Task sources

### Провайдеры (все в v1.0)

Native (UI/API) · GitHub Issues · GitLab Issues · Yandex Tracker · Jira · Chat.

Внутри один контракт: `IncomingTicket`. Один task = один **run** (уникальный id).

### Admission (оба обязательны)

| Режим | |
|-------|--|
| Watch + filter | как AppSet: матч → в работу |
| Catalog + «взять в работу» | inbox в UI |

### Sync

Двусторонний статус в трекер (+ ссылка на run).

### Intake — техника

- Код: `modules/Intake`; webhooks на **`Comuki.Host /api/hooks/{source}`**
- Клиенты трекеров: Refit (где есть HTTP API)
- Идемпотентность **оба замка**:
  1. `intake_deliveries (source, delivery_id)` — одно письмо не обработать дважды
  2. unique active run на `(project, source, external_id)` — один issue = один живой run

---

## 2. Мозг / план

### Роль

Мозг **не** клонирует репо и **не** читает файлы. Agent-loop по платформенным
источникам: knowledge (если вкл.), факты изучателя, каталог профилей, активные
runs, правила/digest.

Код смотрит **изучатор-воркер** (read-only), если мозг или человек решили что он нужен.

### План

Артефакт = **список запусков профилей + зависимости** (граф work items).
Стадии не зашиты в платформу.

Режимы (выбирает **мозг**, человек может форсировать в chat):

| | |
|--|--|
| A | один воркер «просто закрой» |
| B | сначала brief/planner-воркер → потом полный граф |
| C | сразу полный граф, brief'ы пишет мозг |

### Plan-approve

**По умолчанию нужен Approve**; можно выключить per-project.

### Вызов мозга

Agent-loop + tools (не разовый «одна schema без tools»). Детали tool surface /
source-gen — отдельный техпроход. Направление toolset: knowledge · explorer
facts · catalog · runs · emit plan/brief/…

---

## 3. Профили воркеров («агенты»)

Воркер = **один Comuki image** (pi + Translator + SDK) + **профиль**:
system prompt / skills / tools из git.

- **Дефолты** — в репо Comuki
- **Кастом клиента** — в **git клиента** (не наш репо); нам нужен доступ + parse
- UI не source of truth для промптов; опционально редактор → commit в git клиента

При старте контейнера: **fetch pin'нутого git-ref** профиля.

Мозг выбирает профиль из доступных и **дописывает brief** (и может уточнять
prompt в рамках политики — TBD в техпроходке).

---

## 4. Runtime

### Compute

`IComputeProvider`: **Docker (dev/compose) + Kubernetes (prod)** must в v1.0;
containerd — later. Код: `Comuki.Engine.Compute`.

Пул + scale — **ядро умеет**, проект крутит ручки (min/max idle; `0` ≈ create-per-task).

**v1.0 scale:** quota-aware + capacity API провайдера (квоты проекта + allocatable).  
**Future:** собственный fleet/runners на голые хосты.

Слои: Comuki желает реплики / соблюдает квоты · железо нод — cluster/provider.

**Версии воркера (labels):** image digest + profiles git-ref.  
На claim — fetch актуального ref. Смена **image/sdk** → только новый `Start`;  
idle с другим digest не матчится на item.

**K8s Start:** `batch/v1 Job` (`backoffLimit=0`, TTL after finished).  
**Docker Start:** `Containers.Create/Start` в shared compose network.  
Worker token + orch gRPC URL + profile ref — в env при Start.

### Очередь работы

Мозг/оркестратор кладёт **work items** в очередь → свободный воркер **сам claim'ает**
(по профилю) + lease + heartbeat.

Плюс **адресный push** mid-run: Stop / InjectContext в уже работающего.

### Orchestration — техника (техпроход)

| Тема | Решение |
|------|---------|
| Код | `Comuki.Engine.Orchestration` — handlers + ports (CQRS-lite как console.x) |
| Journal | таблица `run_events` + **jsonb**; тяжёлое (raw pi, большие логи) → **MinIO** + uri в payload |
| Lease | колонки на **`work_items`**: `leased_by`, `lease_until`, `heartbeat_at` (Phase 4 код был placeholder) |
| Claim | `FOR UPDATE SKIP LOCKED` + UPDATE lease |
| Статусы run | `queued` · `waiting` · `running` · `succeeded` · `failed` · `cancelled` · `escalated` |
| Статусы work item | `blocked` · `queued` · `running` · `succeeded` · `failed` · `cancelled` — **без stalled** (stall → событие + failed или requeue) |
| Structure | см. обновлённый `comuki-project-structure.md` (`shared/modules/engine/host`) |

### Translator (один процесс в образе)

Мост + claim-клиент + profile git-fetch:
- pi child, stream → typed events
- claim / heartbeat
- fetch profile ref
- gRPC: events out · Stop/Inject in
- **не** решает retry/DAG/бюджет/эскалацию

### gRPC (техпроход)

- **Host = server**, Translator = **client** (контейнер открывает канал наружу)
- Сообщения: Hello/Welcome · Heartbeat · Lifecycle · Activity · Result ↔ Command(Stop|Inject)
- Auth: при `Compute.Start` генерится **worker token** → env → gRPC metadata; revoke с lease/Stop
- Proto/contracts рядом с engine (shared contracts package)

### События

Минимум: **lifecycle + activity + result** (сырьё, не самооценка green).  
Полный stream-json pi — **опционально** (debug-флаг на run, короткий TTL).

Куда: **журнал run** (append-only) + fan-out в chat/UI.  
Infra logs — отдельно (Grafana/Victoria), не в том же UI что run timeline.

---

## 5. Diagnostics / tests / config

### Логи

- Журнал run must: план · события воркеров · решения мозга · cost
- Сервисные логи ≠ run timeline (разные поверхности; ссылка из UI в Grafana)
- Grafana dashboards-as-code: **только в Comuki repo** (`deploy/grafana/…`)

### Тесты платформы

- Unit .NET · Integration (WebApplicationFactory + Testcontainers + **Refit**)
- Contract: OpenAPI → Kubb; дрейф ловит компилятор FE
- FE: vitest + MSW против контракта
- HTTP smoke на .NET по критичным routes
- **Без Playwright** (слишком тяжело на этом этапе)
- Architecture tests (NetArchTest) — сохраняем

### Конфиг

- Платформа: **YAML + env overrides**; секреты только env
- Проект клиента: **разделить**
  - git клиента → профили, admission, playbooks
  - UI live reload → квоты · флаги · approve on/off · debug · budgets
  - env → секреты, URL провайдеров

---

## 6. Proxy / модели (OSS)

- **Не** зависеть от hapy (это hybrid-продукт)
- Wire: **OpenAI-compatible + Anthropic-compatible**
- Воркеры: pi providers / inject URL+key
- Мозг/chat: MEAI `IChatClient` / Voluta → тот же класс endpoints
- **Тонкий optional proxy** в Comuki: virtual key · budget · cost-per-run · отзыв с lease;
  upstream = compatible URL. Dev может выключить

---

## 7. Knowledge / MCP

- **Включаемая фича**, не must ядра
- Пишет: отдельный **docs-профиль** воркера по событию «run done» (upsert)
- MCP-шлюз (если вкл.): мозг + воркеры + локальный Claude Code

---

## 8. Chat harness

- Voluta graph; tools = Orchestration API + мозг
- Полный **агент-пульт** Comuki: проекты, статусы, runs, onboarding…
- **/init** = полный onboarding wizard (репо, compute, models, knowledge seed…)
- Built-in slash + **custom commands из git клиента**
- Mid-run: Inject / Stop / Escalate; read PR comments
- Memory v1 must: **user-scope + project-scope + knowledge hooks**
- **Memory дизайн (прогриллен 2026-09-01, openspec/changes/add-chat-memory):**
  сессии = Voluta-чекпоинты + chat_messages в PG (30д архив); факты =
  memory_facts + pgvector (scope·kind standing/ephemeral·topic_key·superseded);
  чтение = digest-инъекция + memory.search tool; запись = только memory.write
  tool (topic_key канонизация, старое → superseded); забывание = /forget +
  ephemeral TTL 14д; контекст мозга = **MemoryDigest.Build() один сборщик**
  (зовут чат И orchestration, поданное — в журнале) + memory.search у мозга;
  learning-кандидаты = **отдельная learning_candidates** (апрув-очередь → PR в git)
- `/project` + контекст сессии (детали мультипроекта — уточнять при FE)

### Chat / Brain — техника (техпроход)

| Кусок | Где |
|-------|-----|
| Voluta (chat graph, slash, HITL) | **внутри `Comuki.Host`** рядом с REST/SignalR |
| Brain agent-loop | **отдельный `Comuki.Host.Brain`** |
| Host → Brain | **gRPC** (как Translator; proto contracts) |
| Dashboard → Host | OpenAPI + SignalR (Refit/Kubb на клиентах) |

Модули: `modules/Chat`, `modules/Brain`; hosts только composition.

**SignalR:** после append в `run_events` — push в groups `run:{id}` и `project:{id}:attention`.  
Join только с permission (`run:read` / `chat:use`). Тяжёлые payload — uri на MinIO, не в hub.

---

## 9. Харнесы (рантаймы)

| Роль | |
|------|--|
| Воркер | pi + comuki-worker-sdk |
| Разраб | Claude Code + comuki-dev-sdk |
| Chat / мозг-диалог | Voluta + .NET AI |
| Hermes Agent | не рантайм; идеи памяти/команд — да |

### Project learning loop

Сигналы (частые PR comments, verify fails, reject) → кандидат правило/скилл/check
→ человек апрувит → git. Нивелирует галлюцинации **кодом**, не раздутым контекстом.
Детали порогов — отдельно.

---

## 10. AuthN / AuthZ (RBAC)

Ориентир: каркас console.x (SDK ADR-0018), упрощённый под OSS Comuki.

### Identity

- **Свой user-store** (Postgres) + **опциональный OIDC** (Keycloak / Authentik / Entra / …)
- OIDC = кто ты; после логина — запись/линк user у нас
- Роли **не** из IdP: assignments только в Comuki (опц. one-shot map group→role при первом логине — TBD)
- Schemes: cookie (dashboard/chat) + **API-key / PAT** (CI, MCP, автоматизации) как first-class subject
- Права **не** в JWT/cookie — пересчёт на запрос (+ кеш), как в console.x

### Роли — только в коде (нельзя создавать свои)

В БД только **выдачи**: subject × role × scope (`platform` | `project`).

Стартовый набор:
`platform-admin` · `operator` · `project-admin` · `member` · `viewer` · `approver`

### Permissions

- Ключи в коде: `resource:action`
- Матрица Role → permissions в коде
- Check: `[RequiresPermission("…")]` → 403; out-of-scope → 404
- Две оси: **permission** (действие) × **scope** (platform/project) — без дерева TradingDesk

### Не копируем из console.x

Кастомные роли в БД · ~70 ключей сразу · agency-дерево · устаревший `[Authorize(Roles)]`.

### Identity — техника

- Модуль `modules/Identity` (Domain/Application/Infrastructure)
- Таблицы: `users` · `api_keys` · `role_assignments` · `oidc_links` (+ config OIDC providers)
- **Не** используем ASP.NET Identity framework / IdentityDbContext
- Local: свои users + `PasswordHasher`
- Schemes рядом: cookie · API-key bearer · **`AddOpenIdConnect`** (Keycloak/Authentik/Entra/…)
- После SSO — наша cookie + наши assignments (IdP ≠ RBAC)
- UI/settings: подключение OIDC connector (authority, client id/secret в env)
- Check: `[RequiresPermission]` + scope project/platform; права из assignments (+ кеш)
- Permissions: константы + каталог в installer + startup validate (как console.x); source-gen later
- Baseline keys: `run:read|create|stop|inject` · `plan:read|approve` · `queue:read` · `intake:read|claim` · `source:read|write` · `chat:use` · `settings:read|write` · `knowledge:read|admin` · `verify:read` · `cost:read` · `project:read|admin` · `identity:read|write` · `platform:admin`

---

## 11. Verify (включаемая фича)

- Не ядро: можно выключить **без ломки архитектуры**
- Целевой минимум: **generic-command** из git клиента; сырьё = exit code + логи
- **Срок: v1.1** (не блокер ядра v1.0) — слот в design оставляем
- Вердикт по сырью, не по мнению LLM

---

## 11b. Сквозной tech kit (техпроход)

| Тема | Решение |
|------|---------|
| Validation | FluentValidation + **pipeline behavior** на commands/queries |
| Filtering | Порт идей `Hybrid.Sdk.Shared.Filtering` → **`Comuki.Shared.Filtering`** (OSS, без зависимости на Hybrid) |
| Mapping | **Mapperly** для API DTO; **static mappers** на pi/webhook/внешних границах |
| OTel | Auto ASP.NET+HttpClient+EF+gRPC + **ручные spans** на Claim/ApplyPlan/Brain.Invoke/Compute.Start; борды в `deploy/grafana` |
| EF | Repository + **Specifications** в Infrastructure; ports из Application |
| Cache | Permissions assignments + catalog; **FilterableField metadata**; `IMemoryCache` сейчас, Redis при multi-instance later |
| Verify | v1.1 |

---

## 12. Ещё не закрыто / следующие фичи

- Autonomy ratchet · merge-queue · domain-user intake
- Нарезка `v0.2…` capability
- Filter DSL admission
- Полная матрица роль×permission (да/нет)
- Техпроходы: proto details, MEAI providers, SignalR groups
- Синхронизировать PRODUCT.md с новым scope
- Autonomy ratchet · merge-queue · domain-user (продукт)

---

## Краткая схема

```
Intake (trackers | native | chat)
    → Run
    → Brain (agent-loop, no git files) → Plan (nodes + edges)
    → Approve? (default on)
    → Queue work items
    → Workers (same image + profile from client git-ref)
         Translator: claim · fetch profile · pi · events
    → Run journal + chat/UI fan-out
    → optional: docs worker → knowledge
    → optional: verify plugin
```

---

*Draft · обновлено после блоков: intake, brain, runtime, diag/tests/config, proxy, knowledge, chat.*

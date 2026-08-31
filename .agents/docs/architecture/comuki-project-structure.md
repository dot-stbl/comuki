# Comuki — Project Structure

Структура репозитория платформы Comuki. Полиглот-монорепо: верхний уровень
**по стеку**; внутри `platform/` — каркас в духе **console.x.sdk**:
`shared` · `modules` · `engine` · `host`.

> Comuki **не пишет свой код сам** — пишет *другие* проекты. В этом репо:
> платформа, агентские SDK, operational UI, дефолтный control-plane контент.
>
> Продуктовые решения (scope) — в `comuki-v1-scope-draft.md`.  
> FE-экраны — в `../product/comuki-fe-requirements.md`.

**Эволюция:** ранняя раскладка `application/feature/database/models` заменяется
на каркас ниже. Существующие проекты (`Api.Public`, `Orchestration`,
`Worker.Translator`, `Database.Runs`, …) мигрируют по плану в конце файла —
не big-bang в одном PR.

---

## 1. Верхний уровень (по стеку)

```
comuki/
├── README.md · LICENSE · AGENTS.md · comuki.slnx
├── openspec/              # spec-driven changes
├── .agents/               # rules, docs, phases, STATE (агентский контур)
├── platform/              # C# — shared / modules / engine / host
├── agents/                # TS — agent-core · worker-sdk (pi) · dev-sdk
├── dashboard/             # React — operational UI + chat surface
├── control-plane/         # дефолтные worker profiles/skills (git-ref)
├── deploy/                # compose, worker image, grafana dashboards
└── tests/                 # solution-level tests (или platform/tests — см. §8)
```

`comuki.slnx` — **в корне** репо (не внутри `platform/`).

---

## 2. `platform/` — C# (console.x.sdk-style)

Префикс проектов: **`Comuki.*`** (короче, чем `Comuki.Platform.*`; при миграции
допускается временное сосуществование старых имён до rename PR).

```
platform/
├── Directory.Build.props / Directory.Packages.props / .editorconfig
│     (сейчас часть в корне репо — оставить единый DB.props в корне OK)
│
├── shared/
│   ├── Comuki.Shared.Kernel           # примитивы, ids, results — 0 I/O
│   ├── Comuki.Shared.Contracts        # порты: IComputeProvider, IChatGateway…
│   ├── Comuki.Shared.Persistence      # EF base, naming, shared helpers
│   ├── Comuki.Shared.Configuration    # YAML + env binder
│   └── Comuki.Shared.Telemetry        # OTel → Victoria
│
├── modules/                           # Domain | Application | Infrastructure
│   ├── Identity/
│   │   ├── Comuki.Modules.Identity.Domain
│   │   ├── Comuki.Modules.Identity.Application
│   │   └── Comuki.Modules.Identity.Infrastructure   # EF + migrations
│   ├── Intake/                        # GH/GL/YT/Jira/Native providers + admission
│   ├── Chat/                          # sessions, slash catalog, memory ports
│   ├── Brain/                         # agent-loop tools, plan emit (no git files)
│   ├── Knowledge/                     # OPT-IN — MCP + retrieval
│   └── Verify/                        # OPT-IN — generic-command gate
│
├── engine/                            # runtime spine — не «фича продукта»
│   ├── Comuki.Engine.Orchestration    # runs, work-item queue, plan apply, journal
│   ├── Comuki.Engine.Compute          # providers Docker/k8s/containerd + pool/scale
│   └── Comuki.Engine.Routing          # OPT — virtual keys / budget (с Proxy host)
│
├── host/                              # composition roots / deployables
│   ├── Comuki.Host                    # REST + SignalR + Voluta chat + webhooks/hooks
│   ├── Comuki.Host.Brain              # отдельный процесс: brain agent-loop (gRPC server)
│   ├── Comuki.Host.Proxy              # OPT thin YARP (OpenAI+Anthropic compatible)
│   ├── Comuki.Host.Translator         # CMD образа воркера: claim+fetch+pi+gRPC client
│   └── Comuki.Migrator                # one-shot EF migrations + seed
│
└── api/                               # OPTIONAL как в console.x
    └── Comuki.Api.*                   # controllers+DTO по bounded context
        # Альтернатива v0: контроллеры живут в Host до разрастания поверхности
```

### Правила зависимостей

```
host → modules/engine → shared
modules ↛ modules     (только через Shared.Contracts / integration events)
engine ↛ modules UI   (Orchestration вызывает порты Brain/Intake через contracts)
Brain ↛ Compute       (только через Orchestration / ports)
Api.* → Application   (не в Domain)
```

Как console.x.sdk: **механизм** в modules/engine; **vocabulary** permissions/roles
в Identity.Domain (роли **в коде**, assignments в Infrastructure/БД).

### Слои модуля (канон)

| Слой | Можно | Нельзя |
|------|-------|--------|
| Domain | entities, VOs, invariants | EF, HTTP, Voluta, Docker |
| Application | handlers, ports usage, installers | EF DbContext напрямую (через ports) |
| Infrastructure | EF, migrations, provider SDKs | HTTP controllers (→ Api или Host) |

### Database

Отдельных `Database.*` проектов **нет** (уход от старой схемы).  
Миграции — в `*.Infrastructure/Migrations/` соответствующего модуля/engine.  
Применяет `Comuki.Migrator`.

Исключение на переходный период: `Comuki.Platform.Database.Runs` живёт до
переноса в `Engine.Orchestration.Infrastructure`.

### Translator host

`Comuki.Host.Translator` — **не облачный сервис**, а entrypoint образа:

- claim / heartbeat / lease  
- fetch pin'нутого git-ref профилей клиента  
- `Process.Start(pi)` · parse stream-json  
- gRPC bi-di → Orchestration (events out, Stop/Inject in)  

**Не решает** retry / budget / plan — только канал + claim.

gRPC contracts: `Comuki.Shared.Contracts` или отдельный
`Comuki.Engine.Orchestration.Contracts` (proto) — один пакет, ссылают Host.Translator
и Engine.Orchestration.

### Brief / report C# ↔ TS

Форма брифа/отчёта — DTO в contracts (пока руками). Codegen C#→TS — после
стабилизации (как в arch). Не раньше Slice-0+.

---

## 3. `agents/` — TypeScript

Без смены идеи: три пакета.

```
agents/
├── comuki-agent-core/      # events, declarative rules reader, MCP client, brief/report
├── comuki-worker-sdk/      # pi extensions (locks), skill load
└── comuki-dev-sdk/         # Claude Code hooks / subagents (GSD-origin fork)
```

Декларативное — в core; принуждение — в sdk-адаптерах.

---

## 4. `dashboard/` — React

Operational UI + chat surface. Детали экранов —
`.agents/docs/product/comuki-fe-requirements.md`.

```
dashboard/
├── package.json · bun.lock
└── src/                    # FSD/экраны — по FE-requirements
```

Типы API — Kubb из OpenAPI Host.  
Тесты: vitest + MSW; e2e Playwright **нет** (решение scope).

---

## 5. `control-plane/` — дефолтный контент (не код)

Дефолтные профили/skills платформы. Кастом клиента — в **git клиента**
(fetch на старте воркера). UI не SoT для промптов.

```
control-plane/
├── profiles/               # system prompts заготовок
├── skills/
└── chat-commands/          # optional built-in slash packs
```

---

## 6. `deploy/`

```
deploy/
├── worker.Dockerfile       # pi + worker-sdk + Host.Translator
├── docker-compose.yml      # postgres(+pgvector), minio, nexus, victoria, grafana
└── grafana/dashboards/     # as-code (только в Comuki repo)
```

Один worker image; специфика — profile git-ref + очередь.

---

## 7. Hosts (процессы) — итог

| Host | Зачем |
|------|--------|
| `Comuki.Host` | REST (dashboard, claim, `/api/hooks/*`) + SignalR + **Voluta chat** + composition |
| `Comuki.Host.Brain` | Brain agent-loop; **gRPC server**; вызывается из Host |
| `Comuki.Host.Proxy` | optional model gateway (virtual keys / budget) |
| `Comuki.Host.Translator` | container CMD; **gRPC client** → Host (Orchestration) |
| `Comuki.Migrator` | schema + seed |

Внутренние швы service↔service: **gRPC** (Translator, Brain).  
Наружу к dashboard: **OpenAPI + SignalR** (Kubb/Refit на клиентах).

---

## 8. Tests

Предпочтительно зеркало console.x:

```
tests/
├── unit/
│   ├── Comuki.Engine.Orchestration.Unit.*/
│   ├── Comuki.Modules.Identity.Unit.*/
│   └── …
├── integration/            # WAF + Testcontainers + Refit
├── architecture/           # NetArchTest: layers, module isolation
└── host/                   # optional TestHost
```

Пока часть тестов лежит в `tests/` у корня / `platform/tests` — при миграции
свести к одной схеме. Категория в имени проекта сохраняется.

Стек: xUnit · Shouldly · NSubstitute · Testcontainers · Refit · vitest/MSW на FE.
Без Playwright.

---

## 9. Чего в репо нет

- Кода продуктов-клиентов  
- Продуктовых OpenAPI клиентов  
- Кастомных ролей в БД (роли только в коде Identity)  
- Обязательного Knowledge/Verify/Proxy (opt-in features)  

---

## 10. План миграции с текущего дерева

Сейчас (legacy paths):

```
platform/src/application/api/Comuki.Platform.Api.Public
platform/src/application/internal/Comuki.Platform.Worker.Translator
platform/src/feature/Comuki.Platform.Orchestration
platform/src/database/Comuki.Platform.Database.Runs
platform/src/models/Comuki.Platform.Entity.Core
platform/src/models/Comuki.Platform.Api.Contracts
```

Целевые шаги (отдельные PR, не один):

1. Завести `shared/*` skeleton + Composition hooks  
2. `Orchestration` → `engine/Comuki.Engine.Orchestration` (+ Infra для Runs EF)  
3. `Api.Public` → `host/Comuki.Host` (или `api/` + Host)  
4. `Worker.Translator` → `host/Comuki.Host.Translator`  
5. Добавить `modules/Identity` (первым новым модулем: users, keys, assignments, OIDC)  
6. `Host.Brain` + `modules/Brain` · Voluta в `Host`  
7. Intake / Compute — по мере фич  
8. Удалить пустые legacy folders · обновить NetArchTest  

До завершения миграции **оба** layout'а могут сосуществовать в `comuki.slnx`;
arch tests ослабить на переход или писать под фактические пути.

---

## 11. Связь с правилами

- C# style / DI / testing — `.agents/rules/coding/` + user-global csharp rules  
- Отступление от старого `PROJECT-STRUCTURE` application/feature — **осознанное**,
  по образцу console.x.sdk; зафиксировано этим документом и scope-draft  
- Python запрещён для скриптов; FE — bun  

---

*Обновлено под scope 2026-08-30 · каркас shared/modules/engine/host.*

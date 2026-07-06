# Phase 1 — Bootstrap

## Цель

Полиглот-монорепо со скелетом C# solution, который **компилируется чисто** (0
warnings, 0 errors, `TreatWarningsAsErrors=true`). Бизнес-кода пока нет — только
граф проектов, на который ляжет Slice 0.

## Что входит

- **Верхний уровень**: `platform/`, `agents/`, `dashboard/`, `control-plane/`,
  `deploy/` (последние четыре — пустые, с `.gitkeep`).
- **`comuki.slnx`** в корне репо (не внутри `platform/`, чтобы не конфликтовать
  с будущими TS-workspace и React-Vite корнями).
- **`Directory.Build.props`** в корне: `net10.0`, `Nullable=enable`,
  `ImplicitUsings=enable`, `TreatWarningsAsErrors=true`,
  `EnforceCodeStyleInBuild=true`, `AnalysisLevel=latest`. Подавляет `CS1591`
  (отсутствие XML-doc) — комментарии добавляются per-area, не глобально.
- **`Directory.Packages.props`** в корне: пустой (`<ItemGroup></ItemGroup>`),
  Central Package Management готов к Phase 3.
- **5 базовых projects** (минимум, чтобы граф был связным; остальные 12 — в
  своих слайсах):

  | Project | Тип | Зависит от |
  |---|---|---|
  | `Comuki.Platform.Api.Public` | `Microsoft.NET.Sdk.Web` (entry) | Orchestration, Api.Contracts, Entity.Core |
  | `Comuki.Platform.Orchestration` | `classlib` (главная feature) | Entity.Core |
  | `Comuki.Platform.Entity.Core` | `classlib` (entities) | — |
  | `Comuki.Platform.Api.Contracts` | `classlib` (DTO) | — |
  | `Comuki.Platform.Database.Runs` | `classlib` (DbContext stub) | Entity.Core |

- **Минимальный код** в каждом проекте (компилируется, ничего не делает):
  - `Api.Public/Program.cs` — `WebApplication.CreateBuilder`, единственный маршрут
    `GET /health` → 200 `{ "status": "ok" }`. Никакого DI поверх builder'а.
  - `Orchestration/Interfaces/IOrchestrationService.cs` — пустой интерфейс-маркер
    с `<summary>`. Реальные методы появятся в Phase 3.
  - `Entity.Core/Run.cs` — `sealed record Run { Guid Id }`. Schema — в Phase 3.
  - `Api.Contracts/BriefRequest.cs` — `sealed record BriefRequest { required string TaskKey }`.
    Расширяется в Phase 3.
  - `Database.Runs/Contexts/RunsDbContext.cs` — `sealed class RunsDbContext` без
    наследования от `DbContext` (пакет EFCore не подключён). Наследование и
    `OnModelCreating` — в Phase 3.1, когда реально появится БД.

- **`.gitignore`** — `.NET`, Node, IDE, `Thumbs.db`, `soly/rule-mtimes.json`.
- **`git init`** + initial commit со скелетом.

## Что НЕ входит

- **TS-пакеты в `agents/`** — Phase 2. Решение package-manager'а и структуры
  workspace отложено до явного разговора ("потом определятся со стеком").
- **React-приложение в `dashboard/`** — Phase 2. shadcn-vs-что-то — там же.
- **`deploy/docker-compose.yml`** — Phase 2.
- **`deploy/worker.Dockerfile`** — Phase 2 (skeleton) / Phase 3 (real).
- **EF Core / Npgsql пакеты** — Phase 3.1 (Slice 0 Step 1) — подключаются ровно
  когда появляется реальный DbContext. Заранее тащить не нужно.
- **Тестовые проекты** — добавятся в слайсе, который первым де-рискует
  тестирование (Phase 3.2 — Postgres+claim).
- **Analyzer-пакеты в `Directory.Packages.props`** (Roslynator, Meziantou и т.д.)
  — `AnalysisLevel=latest` пока хватает для net10. Полный набор per CODING-RULES
  добавим в Phase 2, когда будет solid картина.
- **`.editorconfig`** — уже на диске (предоставлен пользователем). Меняем только
  если что-то в нашем коде начнёт ломать build.
- **CI** — Phase 2.
- **README.md / CLAUDE.md / AGENTS.md** — позже, когда будет что рассказать.

## Definition of Done

1. `dotnet build comuki.slnx -c Debug` → **0 warnings, 0 errors**.
2. `dotnet run --project platform/src/application/api/Comuki.Platform.Api.Public`
   поднимает Kestrel, `GET /health` возвращает `{"status":"ok"}`.
3. `git log` показывает initial commit со всем перечисленным выше.
4. Структура верхнего уровня:
   ```
   comuki.orchestrator/
   ├── comuki.slnx
   ├── Directory.Build.props
   ├── Directory.Packages.props
   ├── .gitignore
   ├── .editorconfig                  (от пользователя)
   ├── platform/src/{application,feature,models,database}/... (5 csproj)
   ├── agents/                         (.gitkeep)
   ├── dashboard/                      (.gitkeep)
   ├── control-plane/                  (.gitkeep)
   ├── deploy/                         (.gitkeep)
   └── .agents/                          (STATE.md, ROADMAP.md, phases/, rules/, docs/)
   ```

## Hard constraints

- **0 warnings, 0 errors** при `dotnet build` — non-negotiable (architecture.md
  §01: warnings-as-errors — load-bearing wall).
- **Код следует CODING-RULES** (file-scoped namespaces, var, sealed, primary
  constructors где уместно, structured logging, никаких magic strings). То же
  касается `Comuki.Platform.*` нейминга и порядка параметров (Pyramid Rule).
- **Никаких пакетов в Phase 1**, кроме того, что идёт с SDK. Web-SDK даёт
  `Microsoft.AspNetCore.App` framework reference — этого достаточно для
  `WebApplication` + `MapGet`.
- **Никакой бизнес-логики** — placeholder-типы с явным комментарием "filled in
  during Slice N". Чтобы следующий читатель не считал, что это и есть схема.

## Verification (как проверять DoD)

```bash
# Сборка
dotnet build comuki.slnx -c Debug --nologo
# Ожидание: "Build succeeded. 0 Warning(s) 0 Error(s)"

# Sanity-check /health
dotnet run --project platform/src/application/api/Comuki.Platform.Api.Public &
APP_PID=$!
sleep 3
curl -s http://localhost:5000/health    # → {"status":"ok"}
kill $APP_PID

# Git
git log --oneline                       # → 1 commit "chore(bootstrap): …"
git status                              # → clean
```

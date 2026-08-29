---
description: .NET solution project organization — repository layout, layer responsibilities, project naming, references, tests structure
always: true
---

# Project Structure Rules

Правила организации репозитория, проектов и слоёв для C# / .NET solution.
Размещение файлов, нейминг, зависимости между слоями.

Код-стайл — в `CODING-RULES.md`. Работа с библиотеками — в `FRAMEWORK-RULES.md`.

---

## Table of Contents

1. [Repository root](#1-repository-root)
2. [Solution file](#2-solution-file)
3. [Layers overview](#3-layers-overview)
4. [Layer responsibilities](#4-layer-responsibilities)
5. [Decision tree: куда класть новый проект](#5-decision-tree-куда-класть-новый-проект)
6. [Project naming](#6-project-naming)
7. [Creating a new project](#7-creating-a-new-project)
8. [Internal project structure](#8-internal-project-structure)
9. [Layer dependencies](#9-layer-dependencies)
10. [Testing structure](#10-testing-structure)
11. [Anti-patterns](#11-anti-patterns)

---

## 1. Repository root

Корень — **только** управляющие файлы и каталоги верхнего уровня. Никаких
исходников и lock-файлов вложенных стэков.

| Файл | Назначение | Регистр |
|------|-----------|---------|
| `Directory.Build.props` / `.targets` / `.Packages.props` | Общие MSBuild свойства | **PascalCase** |
| `.editorconfig`, `.gitattributes`, `.gitignore` | Код-стайл, Git | как есть |
| `<solution>.slnx` | Solution-файл | lowercase |
| `README.md`, `CLAUDE.md`, `AGENTS.md` | Документация | UPPERCASE |

**Critical**: MSBuild на Linux (CI) — case-sensitive. `Directory.Build.props`
**обязан** быть в PascalCase, иначе `dotnet build` на Linux не подхватит
общие свойства.

**Чего не должно быть**: lock-файлы npm/bun/yarn в корне (рядом с
`package.json`); `node_modules/`, `bin/`, `obj/` (в `.gitignore`); исходники;
дубли `*.sln` + `*.slnx`.

---

## 2. Solution file

Один формат — `.slnx` (XML из .NET SDK 9 / Rider 2024.3+). Преимущества:
читаемый diff, корректный merge, поддержка `dotnet sln` CLI.

**Запрет**: держать одновременно `<name>.sln` и `<name>.slnx` — IDE выберут
разные файлы, появится расхождение «у меня работает».

Миграция: `dotnet sln <name>.sln migrate` → `git rm <name>.sln`.

---

## 3. Layers overview

Архитектура — модульная по capability, не DDD. Сверху вниз:

```
application/    ← entry points: API, workers, aggregator, collector
bots/           ← внешние интерфейсы (Telegram bot, etc.)
feature/
  patterns/     ← торговые паттерны с доменной логикой
  specified/    ← узкие реализации общих концептов
  <other>       ← level-0 capability blocks
database/       ← persistence: DbContext, миграции
client/         ← C# HTTP-клиенты к нашему API
models/         ← entities + API DTO
shared/         ← cross-cutting (DI, logging, extensions)
generation/     ← Roslyn source generators, analyzers
frontend/       ← веб-фронт (изолирован от .NET)
tests/          ← тесты, плоская структура
```

Имена в примерах — синтетика (`Acme.Shop`, `Contoso.Crm`,
`Northwind.Logistics`, `Fabrikam.Trading`). В одном solution — **один**
префикс.

---

## 4. Layer responsibilities

### `shared/`, `feature/`, `feature/patterns/`, `feature/specified/`

- **`shared/`** — cross-cutting инфраструктура (DI bootstrap, logging,
  extensions, MSBuild props). Без этого слой не запустится, но без
  бизнес-смысла. ❌ Не класть: бизнес-логику, клиентов внешних систем,
  persistence.
- **`feature/`** — независимые блоки, **ровно один** capability. Выносить
  в проект когда: переиспользуется в 2+ application-проектах **или** > 10
  файлов и логически изолирован. Примеры: `Background`, `Messaging`,
  `Exchange.Client`.
- **`feature/patterns/`** — торговые паттерны с доменной логикой (один
  проект = один паттерн). Примеры: `Pattern.Core`, `Pattern.Arbitrage`,
  `Pattern.Carry`, `Pattern.MarketMaking`.
- **`feature/specified/`** — узкие реализации общих концептов из
  `Pattern.Core` или `shared/`. Не паттерны и не capability.

| Признак | Куда |
|---------|------|
| Новый capability | `feature/<Name>/` |
| Специализация существующего концепта | `feature/specified/<Name>/` |
| Торговый паттерн | `feature/patterns/<Name>/` |

### `database/` — persistence

**Принцип**: один `DbContext` = один проект.

```
database/
├── Contoso.Crm.Database.Core/              # общая инфраструктура (если есть)
├── Contoso.Crm.Database.Customers/         # один DbContext = один проект
│   ├── InventoryDbContext.cs
│   ├── Migrations/                         # миграции только этого контекста
│   └── Extensions/ServiceCollectionExtensions.cs
├── Contoso.Crm.Database.Identity/
└── Contoso.Crm.Database.Sales/
```

`Database.Core` нужен при **любом** из условий: Specification pattern, generic
repository, общие EF extensions, base entity типы. Если ничего из этого нет
— не создавай.

Команды:

```bash
dotnet ef migrations add <Name> \
    -p src/database/Contoso.Crm.Database.Customers \
    -s src/application/api/Acme.Shop.Api.Public
```

❌ Не должно быть: HostedService/BackgroundService, бизнес-логика, "бывшие"
папки. EF-правила (queries, writes, SqlKata) — в `FRAMEWORK-RULES.md`.

### `client/` и `models/`

- **`client/`** — C# HTTP-клиенты к нашему API. Один проект = один API.
  Генерация — Refit / Kiota / NSwag (**один** на solution). Содержит:
  типизированный интерфейс (`IPublicApiClient`), DI extension
  (`AddPublicApiClient`), DTO из `models/...Api.Contracts` (НЕ дублирует).
  ❌ Не должно быть: бизнес-логики, прямого `HttpClient.SendAsync`,
  дублирования контрактов.
- **`models/`** — `Entity.Core` (entities, EF-атрибуты) и `Api.Contracts`
  (DTO для JSON, без EF-атрибутов).

```
models/
├── Acme.Shop.Entity.Core/        # доменные entities + общие интерфейсы
└── Acme.Shop.Api.Contracts/      # HTTP API DTO
```

### `application/` — entry points

```
application/
├── api/                  # публичные HTTP endpoints
│   └── Acme.Shop.Api.Public/
└── internal/             # workers/aggregators/collectors
    ├── Northwind.Logistics.Aggregator/
    └── Northwind.Logistics.Collector/
```

Только `Program.cs` + `appsettings.json` + тонкий composition root. ❌ Не
должно быть: бизнес-логики, repositories, services.

### `bots/`, `generation/`, `frontend/`, `tests/`

- **`bots/`** — те же entry points, но event-driven / polling вместо HTTP.
- **`generation/`** — Roslyn source generators, analyzers, code-fixers.
  Изолированы: `netstandard2.0`, не зависят от runtime-проектов.
- **`frontend/`** — изолирован от .NET. Свой `package.json`, lock-файл
  рядом (не в корне). Единое имя `frontend/`, не `client-side/`, не `web/`.
  Если фронтов > 1 — подпапки `web/`, `mobile/`, `desktop/`.
- **`tests/`** — см. раздел 10.

---

## 5. Decision tree: куда класть новый проект

Идём сверху вниз, останавливаемся на первом подходящем пункте.

```
1. Запускаемое приложение (Main, Web host)?
   ├── Публичный HTTP API     → application/api/
   ├── Внутренний сервис       → application/internal/
   └── Bot                      → bots/

2. Roslyn analyzer / source generator?  → generation/

3. Работа с БД (DbContext, миграции, repositories)?
   ├── Новый DbContext                 → database/<Company>.<App>.Database.<Name>/
   ├── Generic infra (specs, base repos)→ database/Contoso.Crm.Database.Core/
   └── Только для одного контекста      → внутрь database/<...>.Database.<Name>/

4. HTTP-клиент к нашему API?
   → client/<Company>.<App>.Client.<ApiName>/

5. Типы, пересекающие границы проектов?
   ├── Доменные entities       → models/<Company>.<App>.Entity.Core/
   └── HTTP контракты           → models/<Company>.<App>.Api.Contracts/

6. Cross-cutting инфраструктура (DI, logging, extensions)?
   → shared/

7. Торговый паттерн с доменной логикой?
   → feature/patterns/Fabrikam.Trading.Pattern.<Name>/

8. Специализация / реализация общего концепта?
   → feature/specified/<...>.Specified.<Name>/

9. Level-0 capability (независимый блок)?
   → feature/<Company>.<App>.<Capability>/

10. Frontend?
    → src/frontend/
```

Не подошёл ни один — **остановись и обсуди с командой**. Новая папка
верхнего уровня — архитектурное решение.

---

## 6. Project naming

### Базовый шаблон

```
<Company>.<App>.<Layer>[.<Specifier>][.<Extra>]
```

| Слой | Префикс | Пример |
|------|---------|--------|
| `shared/` | `<Company>.<App>.<Capability>` | `Acme.Shop.Composition` |
| `feature/` | `<Company>.<App>.<Capability>` | `Acme.Shop.Background` |
| `feature/patterns/` | `Fabrikam.Trading.Pattern.<Name>` | `Pattern.Arbitrage` |
| `feature/specified/` | `<...>.Specified.<Name>` | `Specified.Bulk` |
| `database/` | `<...>.Database.<DbContext>` / `Database.Core` | `Database.Customers` |
| `client/` | `<Company>.<App>.Client.<ApiName>` | `Client.Public` |
| `models/` | `<Company>.<App>.Entity.<Scope>` / `Api.Contracts` | `Entity.Core` |
| `application/` | `<Company>.<App>.<EntryPoint>.<Kind>` | `Api.Public` |
| `bots/` | `<Company>.<App>.Bots.<Channel>` | `Bots.Telegram` |
| `generation/` | `<Company>.<App>.Code.<Tech>` | `Code.Roslyn` |
| `tests/` | `<SourceProject>.<Kind>[.<Feature>]` | `Pattern.Arbitrage.Unit.Spread` |

### Глубина имён

```
<Company>.<App>.<Domain>.<SubDomain>      → ок (4 сегмента)
<Company>.<App>.<Domain>.<Kind>           → ок
<Company>.<App>.<Layer>.<Name>.<Variant>  → ок

Глубже 4 — перебор. Поднимай на уровень вверх.
```

### Хороший vs плохой нейминг

| Хорошо | Плохо | Почему |
|--------|-------|--------|
| `Acme.Shop.Composition` | `Acme.Shop.Dependency` | «Dependency» — абстракция, ни о чём |
| `Acme.Shop.Mapping` | `Acme.Shop.Design` | «Design» слишком общее |
| `Acme.Shop.Api.Contracts` | `Acme.Shop.Entity.Api` | «Entity» в .NET = EF; для DTO — «Contracts» |
| `Fabrikam.Trading.Pattern.Arbitrage` | `Fabrikam.Trading.Arbitrage` | префикс `Pattern.` показывает слой |
| `Fabrikam.Trading.Specialized.Bulk` | `Fabrikam.Trading.Specified.Bulk` | «specified» — past participle, странно |

### Запрещённые имена

Имена-помойки: `Utils`, `Helpers`, `Common`, `Misc`, `Tools`, `Shared`,
`Core` (без квалификации), `Implement`, `Context`, `Manager`, `Service`.

Не можешь дать конкретное имя → либо проект не нужен, либо границы
непонятны.

---

## 7. Creating a new project

Все операции с solution — через `dotnet` CLI. IDE — только для редактирования
кода. Это даёт воспроизводимость, документируемость, работающий CI.

### Workflow (6 шагов в порядке)

```
1. Определить место                     (decision tree, раздел 5)
2. Создать физическую папку             (mkdir)
3. Создать csproj                       (dotnet new <template>)
4. Добавить в comuki.slnx               (edit вручную — см. ниже)
5. Добавить ProjectReference            (dotnet add reference)
6. Прогнать верификацию csproj ⇄ slnx   (см. ниже)
```

**Критично**: шаги 2 и 4 в этом порядке. Solution folder в `.slnx` **не
создаёт** физическую папку — она должна быть на диске **до** редактирования
`.slnx`.

**Шаблоны `dotnet new`**: `classlib` для большинства; `webapi` для
`application/api/`; `worker` для `application/internal/` и `bots/`;
`console` для `bots/` и benchmarks; `xunit` для тестов; `classlib` с
`netstandard2.0` для `generation/`. Test framework фиксируется **один** на
solution.

### Правка `comuki.slnx` вручную — зафиксированный workaround

`.NET 10 SDK` на Windows ломает `dotnet sln add --solution-folder` (схлопывает
пути, ломает иерархию папок в `.slnx`). Поэтому `.slnx` редактируется
напрямую — это **не исключение**, это установившийся workflow для всех
проектов в comuki (см. decisions log phase 1 и phase 3).

**Правила правки:**

- **Solution folder = physical path** — точное соответствие физическому
  пути (`platform/src/feature/patterns`, не "Trading Patterns" и не
  "patterns"). Атрибут `<Folder Name="...">` = physical path от корня slnx.
- **Атрибут `Path` относителен корня репо** (не корня slnx). `comuki.slnx`
  лежит в корне, поэтому `Path="platform/src/.../X.csproj"`.
- **Один `<Project>` на проект, без дублирования.**
- **Порядок внутри `<Folder>`** — обычно логический (api → internal → database
  → feature → models → tests), но не строгий.

**Шаблон правки** (на примере добавления `Comuki.Platform.Worker.Translator`):

```xml
<Folder Name="/platform/src/application/internal/">           <!-- новая папка, если не было -->
  <Project Path="platform/src/application/internal/Comuki.Platform.Worker.Translator/Comuki.Platform.Worker.Translator.csproj" />
</Folder>
```

### Верификация: csproj ⇄ slnx

Перед **каждым** коммитом (или в CI) — diff между csproj на диске и путями в
`.slnx`. Пустой вывод = OK. Любое расхождение = забыли проект (или наоборот,
висячая ссылка):

```bash
diff <(find platform/src tests -name '*.csproj' | sort) \
     <(grep -oE 'Path="[^"]+\.csproj"' comuki.slnx | sed 's/Path="//;s/"$//' | sort)
```

> **Заметка:** `grep -P` (PCRE) не работает в Git Bash на Windows (locale
> `CP1252`/similar). Используй `grep -E` (POSIX ERE) — для нашего простого
> паттерна `Path="..."` хватает.

Если расходится — **не коммитить**, починить `.slnx` (или удалить осиротевший
`.csproj`).

### Минимальный csproj

Общие свойства (`TargetFramework`, `Nullable`, `ImplicitUsings`, `LangVersion`,
`TreatWarningsAsErrors`) — в `Directory.Build.props`. csproj проекта содержит
только `ProjectReference` и `PackageReference`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup />
  <ItemGroup>
    <ProjectReference Include="..\..\..\shared\Acme.Shop.Logging\Acme.Shop.Logging.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="9.0.0" />
  </ItemGroup>
</Project>
```

❌ Не дублировать: `TargetFramework`, `Nullable`, `ImplicitUsings`,
`LangVersion` (общее), версии пакетов (централизованно в
`Directory.Packages.props`).

Внутри solution — **только** `ProjectReference`. `PackageReference` — только
для внешних NuGet.

### CI проверка целостности

```bash
diff <(find src tests -name '*.csproj' | sort) \
     <(dotnet sln anlytra.slnx list | grep '\.csproj$' | sort)
```

Расхождение → fail в CI. Защита от забытого `dotnet sln add`.

Удаление проекта: `dotnet sln remove` + `rm -rf`. Переименование — **удаление
+ создание заново**, не переименование csproj.

---

## 8. Internal project structure

Базовый шаблон + специализации:

```
<Project>/                              # базовый
├── <Project>.csproj
├── Interfaces/                         # все интерфейсы (CODING-RULES §13)
├── Models/                             # проект-specific
├── Extensions/
└── <ConcreteClasses>.cs
```

```
<Feature>/                             # feature-проект
├── <Feature>.csproj
├── Interfaces/, Models/
├── Services/                           # бизнес-сервисы
├── Handlers/                           # message/event handlers
└── Extensions/ServiceCollectionExtensions.cs
```

```
Fabrikam.Trading.Pattern.<Name>/       # pattern-проект
├── *.csproj, Interfaces/, Models/, Extensions/
├── Strategies/                         # entry/exit/risk
├── Processors/                         # обработка данных
└── Queries/                            # query specs
```

```
<App>/                                 # application-проект
├── <App>.csproj
├── Program.cs
├── appsettings.json, appsettings.Development.json
├── Configuration/                      # Options pattern
└── Endpoints/ или Controllers/
```

❌ Папки `Helpers/`, `Utils/`, `Common/`, `Misc/`, `Tools/` внутри проекта
запрещены. То же правило, что и для имён проектов.

---

## 9. Layer dependencies

### Правило: ссылки только вниз

```
application/  ─┐
bots/         ─┤
               ├──→  feature/   ─→  models/   ─→  shared/
generation/   ─┘                ─→  database/ ─┘

feature/patterns/    ─→  feature/  (можно)
feature/specified/   ─→  feature/  (можно)
feature/             ─→  models/, shared/  (можно)

feature/patterns/    ─×  application/  (нельзя)
shared/              ─×  feature/      (нельзя — поднимется обратно)
models/              ─×  database/     (нельзя — модели не знают про EF)
```

### Конкретные разрешённые ссылки

| Слой | Может ссылаться на |
|------|-------------------|
| `application/` | `feature/`, `database/`, `client/`, `models/`, `shared/`, `bots/` |
| `bots/` | `feature/`, `client/`, `models/`, `shared/` |
| `feature/patterns/`, `feature/specified/` | `feature/`, `models/`, `shared/` |
| `feature/<other>` | `models/`, `shared/` (**НЕ** другие feature) |
| `database/<...>.Database.<Name>` | `Database.Core`, `models/`, `shared/` |
| `database/Database.Core` | `models/`, `shared/` |
| `client/` | `models/`, `shared/` |
| `models/` | `shared/` (минимально, лучше — ничего) |
| `shared/`, `generation/` | ничего (только nuget) |
| `tests/` | любое из `src/` |

### Между проектами одного слоя

- `feature/<other>` **НЕ** ссылаются друг на друга. Общее → `shared/`.
- `feature/patterns/*` → `Pattern.Core` (базовые абстракции); между
  конкретными паттернами — нет.
- `database/<...>.Database.<Name>` **НЕ** ссылаются друг на друга. Cross-db
  связи — на application-уровне.

Проверяется автоматически в `tests/architecture/` через NetArchTest
(`CODING-RULES.md §15`).

---

## 10. Testing structure

### Папка `tests/` — плоская

Без `unit/`, `integration/`, `architecture/` подпапок — категория в имени
проекта.

```
tests/
├── Fabrikam.Trading.Pattern.Arbitrage.Unit.SpreadCalculation/
├── Fabrikam.Trading.Pattern.Arbitrage.Unit.RiskManagement/
├── Fabrikam.Trading.Pattern.Arbitrage.Integration.Execution/
├── Acme.Shop.Api.Public.Integration.Health/
└── Acme.Shop.Architecture.Tests/   # один на весь solution
```

`tests/` — множественное число (.NET convention). НЕ `test/`.

### Нейминг

```
<SourceProject>.<TestKind>[.<Feature>]
```

- `SourceProject` — обязательно.
- `TestKind` — `Unit` | `Integration` | `Benchmarks`, обязательно.
- `Feature` — опционально, если у src-проекта **ровно один** тест-проект
  данного типа. Если появляется второй — оба обязаны иметь Feature.

Разделять тест-проекты когда: > 30 файлов и логически делится; разные
dependencies (Testcontainers vs нет); разные команды.

### TestKind — что когда

| TestKind | Когда |
|----------|-------|
| `Unit` | Моки, in-memory, < 100ms каждый |
| `Integration` | Реальные зависимости: БД через Testcontainers, HTTP через `WebApplicationFactory` |
| `Benchmarks` | BenchmarkDotNet |

`Architecture.Tests` — один на весь solution. Правила слоёв, нейминга,
размещения интерфейсов и моделей.

❌ Не должно быть: тестов внутри src, папки `test/` (ед. число), подпапок
`unit/`/`integration/` в `tests/`, одного тест-проекта на несколько src
(исключение — `Architecture.Tests`).

---

## 11. Anti-patterns

### Технический долг в имени папки

```
❌ database/.../Repositories/        # "(бывшие, вынесены)"
❌ feature/.../Services_Old/
❌ shared/.../Deprecated/
```

Либо удалить сразу, либо issue с дедлайном. Не хранить «на всякий случай».

### Циклы зависимостей через DI

```csharp
// ❌ feature/A регистрирует реализацию из feature/B → cycle
services.AddSingleton<ISomething, SomethingFromFeatureB>();
```

Решение: общая абстракция в `shared/` или `models/`, реализации
регистрируются на application-уровне.

### Бизнес-логика в `application/`

Application — **только** composition + bootstrap. Расчёты, торговые решения
— в `feature/patterns/`.

### Persistence в feature

`feature/` работает через **интерфейсы** репозиториев / query services,
реализации — в `database/`. Позволяет тестировать паттерны без БД.

### Утечка EF-атрибутов в DTO

API контракты (`Acme.Shop.Api.Contracts`) не знают про EF Core. Никаких
`[Table]`, `[Column]`, `[ForeignKey]`.

### Папки с именами-помойками

`Helpers/`, `Utils/`, `Common/`, `Misc/`, `Tools/`, `Stuff/`. См. раздел 6.

### Один большой проект вместо нескольких

```
❌ Fabrikam.Trading.Exchange.Client/
   ├── Binance/        ← если > 30 файлов или специфичные nuget
   ├── OKX/
   ├── Bybit/
   └── ...
```

Критерий выноса (хотя бы один): > 30 файлов с собственной структурой;
специфичные nuget-пакеты; независимый цикл релиза; можно отключить/заменить
без влияния на остальные.
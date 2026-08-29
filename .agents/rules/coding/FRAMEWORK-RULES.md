---
description: EF Core, ASP.NET Core, MS.Ext.Logging, Gridify — usage patterns, no magic strings, ExecuteUpdate for writes, structured logging
always: true
---

# Framework & Library Usage Rules

Правила использования библиотек и фреймворков в C# / .NET проектах:
EF Core, ASP.NET Core, Microsoft.Extensions.Logging, Gridify.

Чистый C# код-стайл — в **`CODING-RULES.md`** рядом.

---

## Table of Contents

1. [EF Core](#1-ef-core)
2. [Entity models](#2-entity-models)
3. [API design (ASP.NET Core)](#3-api-design-aspnet-core)
4. [Logging (Microsoft.Extensions.Logging)](#4-logging-microsoftextensionslogging)

---

## 1. EF Core

### No magic strings

Названия таблиц/схем — через константы:

```csharp
// ✅ Correct
[Table(DatabaseInformation.Tables.Balance, Schema = DatabaseInformation.Schemes.Exchange)]

// ❌ Wrong
[Table("balance", Schema = "exchange")]
```

Константы лежат в `<Project>.Entity.Core`:
- Таблицы — `DatabaseInformation.Tables`.
- Схемы — `DatabaseInformation.Schemes`.
- Прочее — `*Constants.cs`.

### Queries

**LINQ** для простых случаев. **SqlKata** через `FromSqlKata` — для CTE,
view, cross-schema join'ов.

```csharp
// ✅ Простой запрос — LINQ
return await dbContext.Set<FuturesInstrument>()
    .Where(instrument => instrument.ExchangeName == exchangeName)
    .AsNoTracking()
    .ToListAsync(cancellationToken);

// ✅ Сложный запрос — SqlKata
var sqlQuery = new Query("exchange.v_carry_pairs")
    .When(
        gridifyQuery.MinFundingDeltaPercent.HasValue,
        builder => builder.Where("profit_spread", ">=", gridifyQuery.MinFundingDeltaPercent.Value / 100m));

return await dbContext.CarryPairViewsSet
    .FromSqlKata(sqlQuery)
    .AsNoTracking()
    .GridifyAsync(gridifyQuery, cancellationToken);
```

**Никогда не интерполируй параметры в `WhereRaw`** — используй
`Where("col", ">=", value)` или передавай параметры через биндинги SqlKata.

### Writes

Только EF Core методы и Batch:

```csharp
// ✅ ExecuteUpdate / ExecuteDelete
await dbContext.Set<Instrument>()
    .Where(instrument => instrument.ExchangeName == exchangeName)
    .ExecuteUpdateAsync(
        setters => setters.SetProperty(instrument => instrument.UpdatedAt, DateTime.UtcNow),
        cancellationToken);

// ✅ Bulk
await dbContext.BulkInsertAsync(entities, cancellationToken);
```

Raw SQL для write — запрещён.

### Transactions

В high-load всегда думай о scope:

```csharp
// Read snapshot
await using var transaction = await dbContext.Database.BeginTransactionAsync(
    IsolationLevel.Snapshot, cancellationToken);

try
{
    var instruments = await dbContext.Set<Instrument>()
        .FromSqlKata(sqlQuery)
        .AsNoTracking()
        .ToListAsync(cancellationToken);

    await transaction.CommitAsync(cancellationToken);
    return instruments;
}
catch
{
    await transaction.RollbackAsync(cancellationToken);
    throw;
}
```

### DbContext template

```csharp
[ConnectionString($"{SectionConstants.DatabaseSection}:Inventory")]
public sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> contextOptions)
    : DbContext(contextOptions)
{
    public DbSet<OrderBook> OrderBooksSet { get; init; } = null!;
    public DbSet<FuturesInstrument> FuturesInstrumentsSet { get; init; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        OrderBook.OnModelEntity(modelBuilder);
        FuturesInstrument.OnModelEntity(modelBuilder);

        base.OnModelCreating(modelBuilder);
    }
}
```

### Migrations

**Никогда вручную с нуля.** Только:

```bash
dotnet ef migrations add <Name> \
    -c <DbContextName> \
    -o ./Migrations/<ContextName> \
    -s ../../application/api/MyProject.Api.Public/MyProject.Api.Public.csproj
```

Ручное редактирование сгенерированного `Up()` / `Down()` для сложного SQL
(views, triggers, partitions) — разрешено.

---


### DbContext placement

```
src/database/<Project>/
├── Contexts/
│   ├── InventoryDbContext.cs
│   ├── ManagerDbContext.cs
│   └── UserDbContext.cs
└── Migrations/
    ├── Inventory/
    │   ├── 20260528_Initial.cs
    │   └── InventoryDbContextModelSnapshot.cs
    ├── Manager/
    └── User/
```

Каждый DbContext — в своём файле в `Contexts/`. Миграции каждого
контекста — в собственной подпапке `Migrations/<ContextName>/`.

## 2. Entity models

### Multi-interface inheritance

Свойства сущности **наследуются** через интерфейсы, а не объявляются
ad-hoc в каждой модели.

| Interface              | Property              |
|------------------------|-----------------------|
| `IExchangeObject`      | `ExchangeName`        |
| `IUserObject`          | `UserId`              |
| `IUpdatedEntity`       | `UpdatedAt`           |
| `IEntryComputed`       | `Id` (computed key)   |
| `ISettlementSymbol`    | `AssetName` etc.      |
| `IInstrumentSeparation`| `InstrumentType`      |

```csharp
[Table(DatabaseInformation.Tables.Balance, Schema = DatabaseInformation.Schemes.Exchange)]
public sealed class BalanceInfo : IEntryComputed, IExchangeObject,
    IUpdatedEntity, IUserObject, ISettlementSymbol, IInstrumentSeparation
{
    [Key]
    public string Id { get; init; } = string.Empty;
    public InstrumentType InstrumentType { get; init; }
    public decimal? Value { get; set; }
    public string ExchangeName { get; init; } = string.Empty;
    public string AssetName { get; init; } = string.Empty;
    public string UserId { get; init; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
}
```

### Когда выносить поле в новый интерфейс

Два условия — **оба** должны быть истинны:

1. Поле встречается в **3+** моделях с одинаковой семантикой
   (не просто одинаковое имя — одинаковый смысл).
2. Существует или явно планируется потребитель, работающий с этими моделями
   **полиморфно** через интерфейс (generic-метод, сервис, принимающий
   `IExchangeObject`).

Два совпадения — совпадение. Три совпадения без потребителя — всё ещё
не повод плодить маркер-интерфейсы.

```csharp
// ✅ Правильный кейс — есть generic-метод
public Task<T> UpdateTimestampAsync<T>(T entity, CancellationToken cancellationToken)
    where T : class, IUpdatedEntity
{
    entity.UpdatedAt = DateTimeOffset.UtcNow;
    return SaveAsync(entity, cancellationToken);
}

// ❌ Wrong — интерфейс без потребителя, чисто маркерный
public interface INamed { public string Name { get; init; } }
```

### `OnModelEntity` pattern

```csharp
[Table(DatabaseInformation.Tables.OrderBook, Schema = DatabaseInformation.Schemes.Exchange)]
[Index(nameof(ExchangeName), nameof(Symbol))]
public sealed class OrderBook : IExchangeObject, IUpdatedEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; init; }

    public string ExchangeName { get; init; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; init; }
    public string Symbol { get; init; } = string.Empty;

    /// <inheritdoc cref="DbContext.OnModelCreating" />
    public static ModelBuilder OnModelEntity(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OrderBook>(entity =>
        {
            entity.Property(orderBook => orderBook.UpdatedAt)
                .HasConversion(
                    updatedAt => updatedAt.ToUniversalTime(),
                    updatedAt => updatedAt);

            entity.HasIndex(orderBook => new { orderBook.ExchangeName, orderBook.Symbol });
        });

        return modelBuilder;
    }
}
```

### View configuration

```csharp
modelBuilder.Entity<CarryPairView>(entity =>
{
    entity.ToView("v_carry_pairs", "exchange");
    entity.HasNoKey();
});
```

### Indexes

`[Index]` атрибутом для простых случаев, builder в `OnModelEntity` —
для сложных (filtered, partial, computed).

---

## 3. API design (ASP.NET Core)

### Controller skeleton

```csharp
[ApiController]
[Route($"{ApiRouteConstants.DefaultRoute}/record/futures")]
public sealed class FuturesRecordController(IFuturesQueryService futuresQueryService)
    : ControllerBase
{
    /// <summary>
    /// Returns paginated futures instruments.
    /// </summary>
    [HttpGet("instrument/page")]
    [EndpointName("futures-instrument-page")]
    [EndpointSummary("Returns paginated futures instruments")]
    [EndpointDescription("Supports filtering and sorting through the query parameters")]
    [Tags(["futures", "instruments"])]
    [ProducesResponseType<GridifyResult<FuturesInstrument>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<GridifyResult<FuturesInstrument>>> GetInstrumentPageAsync(
        [FromQuery] GridifyQuery gridifyQuery,
        CancellationToken cancellationToken = default)
        => Ok(await futuresQueryService.QueryAsync(gridifyQuery, cancellationToken));
}
```

### Атрибуты OpenAPI

| Metadata      | Attribute              | Пример                                      |
|---------------|------------------------|---------------------------------------------|
| operationId   | `[EndpointName]`       | `[EndpointName("futures-instrument-page")]` |
| summary       | `[EndpointSummary]`    | `[EndpointSummary("Returns paginated...")]` |
| description   | `[EndpointDescription]`| `[EndpointDescription("Supports...")]`      |
| tags          | `[Tags]`               | `[Tags(["futures"])]`                       |
| response type | `[ProducesResponseType<T>]` | См. ниже                               |

### `[ProducesResponseType<T>]` — 2xx mandatory per-endpoint, 4xx/5xx global

`ProducesResponseType` — нативный атрибут `Microsoft.AspNetCore.Mvc`.
Запрещён только `[SwaggerOperation]` из `Swashbuckle.AspNetCore.Annotations`.

**Правило (comuki):**

- **2xx success** (200, 201, 202, 204): `[ProducesResponseType<T>]` **обязателен**
  per-endpoint — тип варьируется, без атрибута OpenAPI generation не покажет
  shape ответа.
- **4xx/5xx errors** (400, 404, 409, 422, 500, 503): **глобально** через
  `builder.Services.AddProblemDetails()` в `Program.cs` — **не нужно**
  ставить атрибут на каждый action. `[ApiController]` + `AddProblemDetails()`
  автоматически отдают `application/problem+json` для всех ошибок.
- **Override per-endpoint** — только когда error имеет **специфическую форму**
  (например, `409 Conflict` с machine-readable `code` для бизнес-логики,
  или `503 Service Unavailable` с retry-after). Тогда явный
  `[ProducesResponseType<ConflictResponse>(StatusCodes.Status409Conflict)]`.

```csharp
// ✅ Correct — 200 обязателен, 4xx/5xx глобально
[HttpGet("{userId:guid}")]
[EndpointName("users-get-by-id")]
[ProducesResponseType<UserModel>(StatusCodes.Status200OK)]
public async Task<ActionResult<UserModel>> GetUserAsync(
    Guid userId,
    CancellationToken cancellationToken = default)
    => await userService.GetAsync(userId, cancellationToken) is { } user
        ? Ok(user)
        : NotFound();   // → ProblemDetails автоматически через AddProblemDetails()
```

```csharp
// ❌ Wrong — ProblemDetails на каждом action, copy-paste noise
[HttpGet("{userId:guid}")]
[ProducesResponseType<UserModel>(StatusCodes.Status200OK)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
public async Task<ActionResult<UserModel>> GetUserAsync(Guid userId, ...)
```

### Endpoint signatures

- Возврат — `ActionResult<T>` (или `ActionResult` для void-ответов).
- `CancellationToken` последним: `CancellationToken cancellationToken = default`.
- `await` всегда, `.Result` запрещён.
- Если эндпоинт — однострочный проброс — он остаётся `async` + `Ok(await ...)`,
  потому что иначе сигнатура `Task<ActionResult<T>>` не сходится.

### Core models → Record controllers

| Контроллер                  | Назначение                              |
|-----------------------------|-----------------------------------------|
| `FuturesRecordController`   | Futures: Instrument, Orderbook, Position|
| `SpotRecordController`      | Spot                                    |

**Не создавай** `InstrumentsController` / `OrderbookController`.

### Gridify

Gridify фильтрует/сортирует **только по полям возвращаемой модели**. Если
нужно фильтровать по чему-то вне модели — сначала custom-фильтрация в
QueryService, затем Gridify поверх.

### Input models

**Query params** — `[FromQuery(Name = "camelCase")]`:

```csharp
[FromQuery(Name = "pageSize")]
public int PageSize { get; set; } = 25;
```

**Body** — `[JsonPropertyName("PascalCase")]`:

```csharp
[JsonPropertyName("Host")]
public string Host { get; init; } = string.Empty;
```

---

## 3.X. Controller patterns — comuki conventions

Правила ниже — **только для comuki Platform.Api** (`Comuki.Platform.Api.Public`).
Новые контроллеры должны им следовать. Расширение на другие API-проекты
comuki — после обсуждения.

### URL structure & `ApiRoutes` constants

Все URL — `api/{version}/{resource}/...`. Версия — **одна константа** в общем
классе, чтобы при выпуске `v2` поменять в одном месте.

```csharp
namespace Comuki.Platform.Api.Public;

/// <summary>
/// Single source of truth for API URL composition.
/// Version change → update <see cref="ApiVersion"/> and verify generated OpenAPI spec.
/// </summary>
public static class ApiRoutes
{
    public const string ApiVersion = "v1";
    public const string Base = $"api/{ApiVersion}";   // "api/v1"
}
```

```csharp
[ApiController]
[Route($"{ApiRoutes.Base}/[controller]")]   // /api/v1/{controller}
public sealed class TasksController(ITaskService taskService) : ControllerBase
{
    [HttpGet]                                  // GET /api/v1/tasks
    [EndpointName("tasks-list")]
    public Task<ActionResult<IReadOnlyList<TaskResponse>>> ListAsync(...) { ... }

    [HttpGet("{taskId:guid}")]                 // GET /api/v1/tasks/{taskId}
    [EndpointName("tasks-get-by-id")]
    public Task<ActionResult<TaskResponse>> GetAsync(Guid taskId, ...) { ... }
}
```

**Когда менять шаблон вручную** (override `[controller]` в `[Http*]`):

- **Sub-action**: `[HttpPost("{runId:guid}/cancel")]` → `POST /api/v1/runs/{runId}/cancel`
- **Кастомный сегмент**: `[HttpGet("by-slug/{slug:regex(^[a-z0-9-]+$)}")]`
- **Cross-resource**: `[HttpGet("search")]` вместо отдельного `SearchController`
  с одним action

**Route constraints** — обязательны для типизированных ID:

| Сегмент | Что значит | Когда |
|---------|-----------|-------|
| `{taskId:guid}` | Только Guid | ID любой entity |
| `{slug:length(2,50)}` | 2–50 символов | Slug |
| `{slug:regex(^[a-z0-9-]+$)}` | Kebab-case | Slug с правилом |
| `{page:int:min(1)}` | int ≥ 1 | Пагинация |
| `{id:int:range(1,1000)}` | int в диапазоне | Любой enum-like индекс |

### Resource naming — plural

| Singular (неправильно) | Plural (правильно) |
|------------------------|---------------------|
| `/api/v1/task` | `/api/v1/tasks` |
| `/api/v1/run` | `/api/v1/runs` |
| `/api/v1/executionPlan` | `/api/v1/execution-plans` (kebab-case) |

Multi-word ресурсы — **kebab-case** в URL (`/api/v1/execution-plans`,
`/api/v1/worker-clusters`). C#-имя контроллера — **PascalCase plural**
(`ExecutionPlansController`, `WorkerClustersController`).

**Исключение** для singular-сегментов: action-методы (`/cancel`, `/retry`),
health probes (`/health`), `by-...` поиски (`/by-slug/...`).

### Controller structure — hybrid (resource + lifecycle)

**Resource controller** — `{Resource}Controller`, стандартный CRUD:

- Один клиент (дашборд)
- List / get-by-id / create / update / delete
- Имя = ресурс в plural

**Lifecycle controller** — `{Domain}{Action}Controller` или `{Actor}{Action}Controller`,
не CRUD:

- State machine (claim → heartbeat → release)
- Другой клиент (worker SDK vs dashboard)
- Cross-resource, не-RESTful, длинные операции

| Контроллер | Маршруты | Тип |
|------------|----------|-----|
| `RunsController` | `GET /api/v1/runs`, `POST /api/v1/runs`, `GET /api/v1/runs/{id}` | Resource CRUD |
| `StagesController` | `GET /api/v1/runs/{runId}/stages`, `GET /api/v1/stages/{id}` | Resource (nested) |
| `TasksController` | `GET /api/v1/stages/{stageId}/tasks`, `GET /api/v1/tasks/{id}` | Resource (nested) |
| `RulesController` | `GET /api/v1/rules`, `GET /api/v1/rules/{id}` | Resource CRUD (read-only через API) |
| `WorkersController` | `GET /api/v1/workers`, `GET /api/v1/workers/{id}` | Resource CRUD |
| `WorkerClaimController` | `POST /api/v1/workers/claim`, `POST /api/v1/workers/{id}/heartbeat`, `POST /api/v1/workers/{id}/release` | **Lifecycle** (worker SDK) |
| `HealthController` | `GET /api/v1/health` | Probe |

**Сигнал "выноси в отдельный контроллер"** (любой из):

- Другой **клиент** (worker vs dashboard) → почти наверняка разный auth/rate-limit
- **State machine** (claim/heartbeat/release) — не CRUD
- **Cross-resource** (поиск по runs+stages+tasks)
- **Не-RESTful** семантика (длинные операции, batch, async jobs)

`WorkersController` (CRUD) и `WorkerClaimController` (lifecycle) **сосуществуют** —
оба про workers, но разные use-case. Это и есть hybrid.

### Endpoint attributes

**Обязательные** (per-endpoint):

- `[HttpGet]` / `[HttpPost]` / `[HttpPut]` / `[HttpDelete]` / `[HttpPatch]`
  с явным шаблоном, если путь не очевиден из convention
- `[ProducesResponseType<T2xx>]` для success — тип варьируется, без атрибута
  OpenAPI generation не покажет shape

**Рекомендуемые** (per-endpoint):

- `[EndpointName("kebab-case-name")]` — operationId для OpenAPI/Kubb.
  Без него будет `TasksController_ListAsync` — работает, но некрасиво
  в сгенерированном TS-клиенте
- `[EndpointSummary("...")]` — короткое summary для OpenAPI UI
- `[Tags(["domain", "subdomain"])]` — группировка в OpenAPI UI

**Глобально** (в `Program.cs`):

- `builder.Services.AddProblemDetails();` — все 4xx/5xx автоматически в
  `application/problem+json`
- **Не нужно** ставить `[ProducesResponseType<ProblemDetails>(400)]` и т.п.
  на каждый action

**Override per-endpoint** (только при необходимости):

- Когда error имеет **специфическую форму** (например, `409 Conflict` с
  machine-readable `code` для бизнес-логики, `503 Service Unavailable` с
  `Retry-After` header)

**Auth** — намеренно **не покрыто** этим правилом. Когда появится —
отдельное правило `security/auth.md` с per-endpoint обсуждением
(роли, политики, rate-limit per client). Пока auth нет.

### Examples

#### 1. List (200 OK) — простая коллекция

```csharp
[HttpGet]
[EndpointName("tasks-list")]
[EndpointSummary("Returns all tasks")]
[Tags(["tasks"])]
[ProducesResponseType<IReadOnlyList<TaskResponse>>(StatusCodes.Status200OK)]
public async Task<ActionResult<IReadOnlyList<TaskResponse>>> ListAsync(
    CancellationToken cancellationToken = default)
    => Ok(await taskService.ListAsync(cancellationToken));
```

#### 2. Get by ID (200 / 404) — типизированный параметр

```csharp
[HttpGet("{taskId:guid}")]
[EndpointName("tasks-get-by-id")]
[EndpointSummary("Returns a single task by ID")]
[Tags(["tasks"])]
[ProducesResponseType<TaskResponse>(StatusCodes.Status200OK)]
public async Task<ActionResult<TaskResponse>> GetAsync(
    Guid taskId,
    CancellationToken cancellationToken = default)
    => await taskService.GetAsync(taskId, cancellationToken) is { } task
        ? Ok(task)
        : NotFound();   // → ProblemDetails автоматически
```

`NotFound()` без аргумента → ASP.NET Core генерирует `ProblemDetails`
через `AddProblemDetails()`. Никакого атрибута на action не нужно.

#### 3. Create (201 Created) — POST с телом

```csharp
[HttpPost]
[EndpointName("tasks-create")]
[EndpointSummary("Creates a new task")]
[Tags(["tasks"])]
[ProducesResponseType<TaskResponse>(StatusCodes.Status201Created)]
public async Task<ActionResult<TaskResponse>> CreateAsync(
    [FromBody] CreateTaskRequest request,
    CancellationToken cancellationToken = default)
{
    var task = await taskService.CreateAsync(request, cancellationToken);
    return CreatedAtAction(nameof(GetAsync), new { taskId = task.Id }, task);
}
```

`CreatedAtAction` строит URL через `nameof(GetAsync)` + route values —
**не пиши URL руками**. OpenAPI generation подхватит `Location` header
автоматически.

#### 4. Nested resource (stages в run)

```csharp
[HttpGet("runs/{runId:guid}/stages")]
[EndpointName("runs-stages-list")]
[EndpointSummary("Returns stages for a given run")]
[Tags(["runs", "stages"])]
[ProducesResponseType<IReadOnlyList<StageResponse>>(StatusCodes.Status200OK)]
public async Task<ActionResult<IReadOnlyList<StageResponse>>> ListStagesAsync(
    Guid runId,
    CancellationToken cancellationToken = default)
    => Ok(await stageService.ListByRunAsync(runId, cancellationToken));
```

Nested-маршруты — `{parentId:guid}/{collection}` без сегмента "by-{parent}".

#### 5. Sub-action (cancel run)

```csharp
[HttpPost("{runId:guid}/cancel")]
[EndpointName("runs-cancel")]
[EndpointSummary("Cancels an in-progress run")]
[Tags(["runs"])]
[ProducesResponseType<RunResponse>(StatusCodes.Status200OK)]
public async Task<ActionResult<RunResponse>> CancelAsync(
    Guid runId,
    CancellationToken cancellationToken = default)
    => Ok(await runService.CancelAsync(runId, cancellationToken));
```

Sub-action — `POST` (не `PUT`/`DELETE`), потому что меняется **state**,
а не сам ресурс. `DELETE` — только для удаления записи.

#### 6. Lifecycle endpoint (worker claim) с специфичным 503

```csharp
[HttpPost("workers/claim")]
[EndpointName("workers-claim")]
[EndpointSummary("Worker claims the next available task")]
[Tags(["workers"])]
[ProducesResponseType<ClaimResponse>(StatusCodes.Status200OK)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
public async Task<ActionResult<ClaimResponse>> ClaimAsync(
    [FromBody] ClaimRequest request,
    CancellationToken cancellationToken = default)
{
    var claim = await workerClaimService.ClaimAsync(request, cancellationToken);
    return claim is null
        ? Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "No tasks available",
            detail: "Worker lease queue is empty; retry after backoff.")
        : Ok(claim);
}
```

Здесь `Problem(...)` строит `ProblemDetails` явно, и `[ProducesResponseType]`
override нужен потому что **этот 503 — специфичный** (не generic NotFound,
а доменный "queue empty" с retry-after). Для всех остальных 4xx/5xx в
этом контроллере — глобально.

#### 7. Search (cross-resource) — не-RESTful endpoint

```csharp
[HttpGet("search")]
[EndpointName("search-runs-stages-tasks")]
[EndpointSummary("Full-text search across runs, stages, and tasks")]
[Tags(["search"])]
[ProducesResponseType<SearchResponse>(StatusCodes.Status200OK)]
public async Task<ActionResult<SearchResponse>> SearchAsync(
    [FromQuery] string query,
    CancellationToken cancellationToken = default)
    => Ok(await searchService.SearchAsync(query, cancellationToken));
```

Search — cross-resource, не CRUD на конкретной сущности → **отдельный
endpoint** в подходящем контроллере (не отдельный `SearchController` с
одним action). `[Tags(["search"])]` выделяет в OpenAPI UI отдельной группой.

### Anti-patterns

```csharp
// ❌ Magic strings в URL — теряем единое место для версии
[Route("api/v1/tasks")]
public sealed class TasksController : ControllerBase

// ❌ Конкатенация строк в контроллере (нужен helper или CreatedAtAction)
return Created($"/api/v1/tasks/{task.Id}", task);

// ❌ `[controller]` не работает с другим префиксом — пиши явно
[HttpGet("v1/special")]   // → /api/v1/tasks/v1/special, баг

// ❌ 4xx/5xx на каждом action — copy-paste noise
[ProducesResponseType<ProblemDetails>(400)]
[ProducesResponseType<ProblemDetails>(404)]
[ProducesResponseType<ProblemDetails>(409)]
[ProducesResponseType<ProblemDetails>(500)]
public async Task<...> GetAsync(...)

// ❌ `ProblemDetails` в success response type — не та семантика
[ProducesResponseType<ProblemDetails>(StatusCodes.Status200OK)]

// ❌ `void`/ничего-не-возвращает action — ASP.NET Core всё равно ждёт ActionResult
public async Task DeleteAsync(Guid id) { ... }   // компилируется, но OpenAPI не покажет 204
```

---

## 4. Logging (Microsoft.Extensions.Logging)

### Только structured logging

```csharp
// ✅ Correct
logger.LogInformation(
    "Starting execution {ExecutionId} for task {TaskName}",
    execution.Id, execution.TaskName);

logger.LogWarning(
    "Execution {ExecutionId} exceeded timeout of {TimeoutMs}ms",
    execution.Id, timeout.TotalMilliseconds);

logger.LogError(
    exception,
    "Execution {ExecutionId} failed",
    execution.Id);

// ❌ Wrong — string interpolation, ломает structured logging
logger.LogInformation($"Starting execution {execution.Id}");

// ❌ Wrong — concat
logger.LogInformation("Starting execution " + execution.Id);
```

Запрещено `.editorconfig`-ом через **CA2254** (severity = error).

### Log levels

| Level         | Когда                                                                |
|---------------|----------------------------------------------------------------------|
| `Trace`       | Очень детальная трассировка, обычно выключена в проде.               |
| `Debug`       | Диагностика во время разработки.                                     |
| `Information` | Штатные события: запуск, выполнение задачи, регистрация.             |
| `Warning`     | Неожиданное, но обработанное (retry, fallback, rate limit).          |
| `Error`       | Сбой операции, приложение продолжает работу.                         |
| `Critical`    | Сбой, угрожающий работе приложения (потеря БД, OOM).                 |

```csharp
logger.LogDebug("Query took {ElapsedMs}ms", elapsedMs);
logger.LogInformation("Task {TaskKey} executed successfully", taskKey);
logger.LogWarning("Retry attempt {AttemptNumber} for task {TaskKey}", attempt, taskKey);
logger.LogError(exception, "Failed to execute task {TaskKey}", taskKey);
logger.LogCritical("Database connection lost");
```

### Placeholders — PascalCase

```csharp
// ✅ PascalCase placeholders — стандарт Serilog/MEL
logger.LogInformation("User {UserId} signed in from {IpAddress}", userId, ipAddress);

// ❌ camelCase / snake_case
logger.LogInformation("User {userId} signed in", userId);
```

---

## Quick reference

| Что                                           | Где проверять                       |
|-----------------------------------------------|-------------------------------------|
| Structured logging (no interpolation)         | `.editorconfig` — CA2254            |
| PascalCase placeholders в логах               | `.editorconfig` — CA1727            |
| `ForwardCancellationToken` в async chain      | `.editorconfig` — MA0040            |
| EF Core: только `ExecuteUpdate`/`Bulk` для writes | Код-ревью                       |
| SqlKata `WhereRaw` без интерполяции параметров | Код-ревью                          |
| `[Table]` с константами, не строками          | Код-ревью                           |
| `OnModelEntity` static method на каждой entity| Код-ревью                           |
| `[ProducesResponseType<T>]` для 2xx success, **глобальный `AddProblemDetails()` для 4xx/5xx** | Код-ревью                           |
| `ApiRoutes.ApiVersion` — single source of truth для URL-версии | Код-ревью                           |
| Resource plural + kebab-case в URL, `[controller]` в `[Route]` | Код-ревью                           |
| Hybrid controller structure: resource CRUD + lifecycle отдельно | Код-ревью                           |
| `[Http*]` с явным шаблоном, `[EndpointName]` для OpenAPI operationId | Код-ревью                           |
| Controller наследует `ControllerBase`         | Код-ревью                           |
| Core models → Record controllers, не отдельные| Код-ревью                           |

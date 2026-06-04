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

### `[ProducesResponseType<T>]` — required для non-200

`ProducesResponseType` — это **нативный** ASP.NET Core атрибут
(`Microsoft.AspNetCore.Mvc`), не Swashbuckle. Запрещён только
`[SwaggerOperation]` из `Swashbuckle.AspNetCore.Annotations`.

Правило:

- **200 OK**: тип выводится из сигнатуры `ActionResult<T>` — атрибут опционален.
- **Все non-200 ответы** (400/404/409/422/500): **обязателен**
  `[ProducesResponseType<T>(StatusCodes.Status4xx...)]` с конкретным типом
  проблемы (обычно `ValidationProblemDetails` или `ProblemDetails`).

```csharp
[ProducesResponseType<UserModel>(StatusCodes.Status200OK)]
[ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
public async Task<ActionResult<UserModel>> GetUserAsync(Guid userId, CancellationToken cancellationToken = default)
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
| `[ProducesResponseType<T>]` для non-200       | Код-ревью                           |
| Controller наследует `ControllerBase`         | Код-ревью                           |
| Core models → Record controllers, не отдельные| Код-ревью                           |

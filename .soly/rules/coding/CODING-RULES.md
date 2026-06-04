---
description: C# / .NET code style — naming, primary constructors, sealed, records, async/await, var, file-scoped namespaces, braces, pattern matching
always: true
---

# C# / .NET Coding Rules

Единые правила C# код-стайла для всех .NET проектов.
EN термины — RU пояснения.

Что можно автоматизировать — лежит в `.editorconfig` рядом с этим файлом
(rule severity = `error` или `warning`). В этом документе — то, что
`.editorconfig` не ловит: паттерны, обоснования, организационные правила.

Правила использования библиотек (EF Core, ASP.NET Core, MS.Ext.Logging)
вынесены в отдельный файл — **`FRAMEWORK-RULES.md`**.

---

## Table of Contents

1. [Naming](#1-naming)
2. [Class structure & sealed](#2-class-structure--sealed)
3. [Primary constructors](#3-primary-constructors)
4. [Record vs class](#4-record-vs-class)
5. [Pattern matching & minimal code](#5-pattern-matching--minimal-code)
6. [Async / await](#6-async--await)
7. [`var` vs explicit type](#7-var-vs-explicit-type)
8. [File-scoped namespaces](#8-file-scoped-namespaces)
9. [Braces](#9-braces)
10. [XML documentation](#10-xml-documentation)
11. [Comments](#11-comments)
12. [Collections](#12-collections)
13. [Interface organization](#13-interface-organization)
14. [Project structure](#14-project-structure)
15. [Required tooling](#15-required-tooling)

---

## 1. Naming

### Общие правила

- **Interfaces**: `I` префикс — `ISchedulerManager`, `IConnectionManager`.
- **Без сокращений**: `user`, `configuration`, `request` — а не `usr`, `cfg`, `req`.
- **Methods**: verb phrases — `CreateUserAsync`, `GetFilteredAsync`.
- **Access modifiers**: всегда явно (включая `public` на членах интерфейса).
- **Lambda parameters**: осмысленные имена, **никогда** одной буквы.

### Postfixes

Используй описательные постфиксы, отражающие назначение:

```csharp
// ✅ Correct
public sealed record UserModel { ... }
public sealed record TaskRequest { ... }
public sealed record TaskResponse { ... }
public sealed record HealthCheckResult { ... }
public sealed class TaskScheduler { ... }
public sealed class UserAuthenticator { ... }

// ❌ Wrong — generic постфикс, не несёт смысла
public sealed record UserDto { ... }
public sealed class TaskService { ... }
public sealed class DataHelper { ... }
public sealed class ValidationUtil { ... }
```

**Result vs Response**:

- `*Response` — DTO ответа HTTP-эндпоинта (наружу через API).
- `*Result` — результат внутренней операции (метод сервиса, валидатор).

### Async methods

1. **Async suffix**: все методы возвращающие `Task` / `ValueTask` / `Task<T>` /
   `ValueTask<T>` оканчиваются на `Async`. Независимо от того, есть ли внутри
   `await` — даже простой проброс Task получает суффикс.
2. **`CancellationToken` последним параметром** со значением по умолчанию:
   `CancellationToken cancellationToken = default`.

```csharp
public Task<int> GetCountAsync(CancellationToken cancellationToken = default)
    => repository.CountAsync(cancellationToken);

public async Task<User> GetUserByIdAsync(
    Guid userId,
    CancellationToken cancellationToken = default)
{
    ...
}
```

### `Task` vs `ValueTask`

- **`Task<T>`** — операция всегда асинхронная (IO, БД, HTTP).
- **`ValueTask<T>`** — операция **может** быть синхронной (кеш-хит, материализованное
  значение). Типичный пример — `GetOrLoadAsync`, где 99% случаев — кеш-хит.

```csharp
// ✅ ValueTask — кеш может вернуть синхронно
public async ValueTask<User> GetUserAsync(Guid userId, CancellationToken cancellationToken = default)
{
    if (cache.TryGet(userId, out var cached))
    {
        return cached;
    }

    return await loader.LoadAsync(userId, cancellationToken);
}
```

### Parameter naming

Описательные имена везде. Сокращения запрещены:

| Bad             | Good                          |
|-----------------|-------------------------------|
| `ct`            | `cancellationToken`           |
| `sp`            | `serviceProvider`             |
| `id`            | `userId`, `taskId`, `orderId` |
| `req`           | `request`                     |
| `resp`         | `response`                    |
| `msg`           | `message`                     |
| `err`           | `error`                       |

**Исключение**: переменная цикла с очевидным контекстом и коротким скоупом —
`foreach (var item in items)` допустимо.

### Lambda parameters

```csharp
// ✅ Correct
users.Where(user => user.IsActive)
     .Select(activeUser => activeUser.Id)
     .ToList();

// ❌ Wrong
users.Where(u => u.IsActive)
     .Select(x => x.Id)
     .ToList();
```

Одно-буквенные lambda-параметры **запрещены без исключений**.

### Private fields

**NO underscore prefix**. Предпочитай auto-properties или primary constructor.
Поле без подчёркивания — только если нужна кастомная логика геттера/сеттера
или поле нельзя выразить через primary constructor.

```csharp
// ✅ Best — primary constructor
public sealed class UserService(ILogger<UserService> logger)
{
    public Task DoAsync() => logger.LogInformationAsync(...);
}

// ✅ Auto-property когда нужно
public sealed class UserService
{
    public ILogger<UserService> Logger { get; }
}

// ✅ Private field без подчёркивания — если нужна custom-логика
public sealed class Counter
{
    private int currentValue;

    public int Increment() => Interlocked.Increment(ref currentValue);
}

// ❌ Wrong
private readonly string _someValue;
```

---

## 2. Class structure & sealed

### `sealed` рекомендуется

`sealed` рекомендуется по умолчанию для всех **конкретных** классов.
Не обязательно, но при ревью спросят: «Зачем эта база незапечатана?
Есть наследник?»

Не помечать `sealed`:
- `abstract` базовые классы (по определению).
- Классы, для которых **есть** наследник или спроектирована точка расширения.

```csharp
// ✅ Default — sealed
public sealed class UserService(IUserRepository userRepository)
{
    ...
}

// ✅ Base — abstract, не sealed
public abstract class WorkerBase : IHostedService
{
    public abstract Task RunAsync(CancellationToken cancellationToken);
}

// ✅ Открыт для наследования сознательно — есть наследники
public class DomainEvent { ... }
public sealed class OrderCreated : DomainEvent { ... }
```

CA1852 включён в `.editorconfig` как warning, чтобы напоминать.

---

## 3. Primary constructors

### Всегда primary constructor

Для DI-классов, сервисов, контроллеров, репозиториев, worker-ов — **всегда**
primary constructor.

Обычный конструктор — только если нужна валидация параметров, side-effects
в конструкторе, или интерфейсная иерархия с разными ctor'ами.

### Pyramid Rule (short → long)

Параметры в primary constructor сортируются **по длине типа: короткий → длинный**.

```csharp
// ✅ Correct — IUserRepository (короче) первым
public sealed class UserService(IUserRepository repository, ILogger<UserService> logger)
{
    ...
}

// ✅ Correct
public sealed class OrderHandler(
    IClock clock,
    IOrderRepository orderRepository,
    ILogger<OrderHandler> logger)
{
    ...
}

// ❌ Wrong — порядок нарушен
public sealed class UserService(ILogger<UserService> logger, IUserRepository repository)
```

При равной длине — алфавитный порядок.

---

## 4. Record vs class

### Default — `sealed class`

`record` используется **только** когда явно нужны:
- Value-equality (структурное сравнение по полям).
- `with`-expressions.
- Сжатый синтаксис для immutable value-объектов.

Во всех остальных случаях — `sealed class`. Это касается **в том числе**:
- EF Core entities (классы работают надёжнее в edge-cases tracking-а).
- DTO, которые сравниваются по reference.
- Результаты сервисов (если не нужен value-equality).

```csharp
// ✅ Value object — record оправдан
public sealed record Money(decimal Amount, string Currency);

// ✅ EF entity — class
[Table(DatabaseInformation.Tables.OrderBook, Schema = DatabaseInformation.Schemes.Exchange)]
public sealed class OrderBook : IExchangeObject, IUpdatedEntity
{
    [Key]
    public Guid Id { get; init; }
    public string ExchangeName { get; init; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; init; }
}

// ✅ DTO без value-equality — class
public sealed class CreateOrderRequest
{
    public required string Symbol { get; init; }
    public required decimal Quantity { get; init; }
}
```

### Required members

```csharp
public required string DeclarationId { get; init; }
```

---

## 5. Pattern matching & minimal code

### Главный принцип: минимальность через синтаксический сахар

**Не разделяй присвоение и проверку.** Если C# позволяет уложить
объявление переменной + условие в одно выражение — делай это.

Применимые инструменты:
- `is { } var` / `is not { } var` для null-checks.
- `is var x and > N` для inline-условий.
- Ternary в return.
- Inline выражение в `foreach`, `if`, `switch expression`.
- Switch expressions вместо длинных `if/else`.

### Inline assignment + check

```csharp
// ❌ Wrong — две строки: переменная, потом проверка
var user = await repository.GetByIdAsync(userId, cancellationToken);
if (user == null)
{
    return NotFound();
}

// ✅ Correct — одна строка
if (await repository.GetByIdAsync(userId, cancellationToken) is not { } user)
{
    return NotFound();
}

// ❌ Wrong
var metaJson = await db.HashGetAsync(metaKey, "*");
if (metaJson.IsNullOrEmpty)
{
    return null;
}

// ✅ Correct
if (await db.HashGetAsync(metaKey, "*") is not { } metaJson)
{
    return null;
}
```

### Ternary в return

```csharp
// ❌ Wrong — переменная только ради return
var dataJson = await db.HashGetAsync(metaKey, field);
if (dataJson.IsNullOrEmpty)
{
    return null;
}
return JsonSerializer.Deserialize<ManagedProxy>(dataJson.ToString());

// ✅ Correct
return await db.HashGetAsync(metaKey, field) is { } dataJson
    ? JsonSerializer.Deserialize<ManagedProxy>(dataJson.ToString())
    : null;
```

### Inline в `foreach`

```csharp
// ❌ Wrong
var items = await ComputeAsync(cancellationToken);
foreach (var item in items)
{
    Process(item);
}

// ✅ Correct
foreach (var item in await ComputeAsync(cancellationToken))
{
    Process(item);
}
```

### `is var` and

```csharp
// ✅ Присвоить и проверить одним выражением
if (await registry.MarkHungTasksAsync(timeout, cancellationToken) is var hungCount and > 0)
{
    logger.LogInformation("Marked {Count} tasks as Hung", hungCount);
}
```

### Switch expressions

```csharp
// ❌ Wrong
JobStatus status;
if (state == JobState.Running) status = JobStatus.Running;
else if (state == JobState.Disconnected) status = JobStatus.Disconnected;
else status = JobStatus.Waiting;

// ✅ Correct
var status = state switch
{
    JobState.Running       => JobStatus.Running,
    JobState.Disconnected  => JobStatus.Disconnected,
    _                      => JobStatus.Waiting,
};
```

### Inline single-use variables

Если переменная используется **один раз** — инлайн её. Без исключений.

```csharp
// ❌ Wrong
var fundingData = await fundingService.GetFundingDataAsync(fundingId, cancellationToken);
return Ok(fundingData);

// ✅ Correct
return Ok(await fundingService.GetFundingDataAsync(fundingId, cancellationToken));

// ❌ Wrong
var count = items.Count;
logger.LogInformation("Found {Count} items", count);
return Ok(count);

// ✅ Correct
logger.LogInformation("Found {Count} items", items.Count);
return Ok(items.Count);
```

### Когда промежуточная переменная нужна

- Значение используется **дважды и более**.
- Имя переменной добавляет смысл, которого нет в выражении.
- Выражение слишком сложное для inline (читаемость > краткость).

```csharp
// ✅ Используется в условии и в return
var activeLocks = await database.DeployLocks
    .AsNoTracking()
    .Where(deployLock => deployLock.ExpiresAt > now)
    .ToListAsync(cancellationToken);

if (activeLocks.Count == 0)
{
    return Array.Empty<DeployLock>();
}

return activeLocks;
```

### EF tracking + pattern

```csharp
// ✅ AsTracking — нужен мутируемый объект, is not { } pattern
if (await database.Executions
        .AsTracking()
        .FirstOrDefaultAsync(execution => execution.Id == id, cancellationToken)
        is not { } execution)
{
    return false;
}

execution.Status = ExecutionStatus.Completed;
await database.SaveChangesAsync(cancellationToken);
return true;
```

---

## 6. Async / await

- **Весь IO** — через `async`/`await`.
- **Запрещено**: `.Result`, `.Wait()`, `.GetAwaiter().GetResult()`.
- **Async suffix**: всегда на методах, возвращающих `Task` / `ValueTask`.
- **`CancellationToken`** последним параметром везде, где есть IO.

Прокидывай `cancellationToken` во **все** вложенные async-вызовы — никаких
`Task.Delay(...)` без токена.

---

## 7. `var` vs explicit type

**Всегда `var`** когда тип выводится из правой части. Это покрывается
`.editorconfig`, нарушение — ошибка компиляции.

```csharp
// ✅ Correct
var user = await repository.GetByIdAsync(userId, cancellationToken);
var symbols = new List<string>();
var count = items.Count;

// ❌ Wrong (избыточно)
List<string> symbols = new List<string>();
User user = await repository.GetByIdAsync(userId, cancellationToken);
```

---

## 8. File-scoped namespaces

Всегда `namespace Foo;` (file-scoped). Block-scoped запрещён.

```csharp
// ✅ Correct
namespace MyProject.Services;

public sealed class UserService { }

// ❌ Wrong
namespace MyProject.Services
{
    public sealed class UserService { }
}
```

---

## 9. Braces

### Braces обязательны везде

Фигурные скобки требуются для **всех** control flow statements:
`if`, `else`, `for`, `foreach`, `while`, `do`, `using`, `lock`, `fixed`.

```csharp
// ✅ Correct
if (user is null)
{
    return NotFound();
}

foreach (var item in items)
{
    Process(item);
}

// ❌ Wrong — однострочник без скобок
if (user is null)
    return NotFound();

foreach (var item in items)
    Process(item);

// ❌ Wrong — однострочник в одну строку
if (user is null) return NotFound();
```

Правило применяется и к `else`:

```csharp
// ✅ Correct
if (proxy.IsHealthy)
{
    await repository.MarkHealthyAsync(proxy.Id, cancellationToken);
}
else
{
    logger.LogWarning("Proxy {ProxyId} failed health check", proxy.Id);
}
```

### Не противоречит минимальности

Раздел 5 (минимальность через синтаксический сахар) и раздел 9 (braces)
работают вместе:

- **Минимальность** — не плодить лишние переменные, использовать `is { } var`,
  ternary, switch expression, inline в `foreach`. Это про **отсутствие
  промежуточных шагов**.
- **Braces** — требование к **форме** control flow.

Тот же inline-pattern с braces:

```csharp
// ✅ Минимально И со скобками
if (await repository.GetByIdAsync(userId, cancellationToken) is not { } user)
{
    return NotFound();
}

return Ok(user);
```

### Expression-bodied members — не затрагиваются

Правило про braces применяется только к **statements**. Expression-bodied
методы, свойства, операторы — остаются как есть:

```csharp
// ✅ Expression-bodied — braces не нужны
public Task<User> GetUserAsync(Guid userId, CancellationToken cancellationToken = default)
    => repository.GetByIdAsync(userId, cancellationToken);

public string FullName => $"{FirstName} {LastName}";
```

### Switch expressions — не затрагиваются

```csharp
// ✅ Switch expression — не control statement, braces не нужны
var status = state switch
{
    JobState.Running       => JobStatus.Running,
    JobState.Disconnected  => JobStatus.Disconnected,
    _                      => JobStatus.Waiting,
};
```

Правило закреплено в `.editorconfig` — `csharp_prefer_braces = true:error`.
Нарушение ломает build.

---

## 10. XML documentation

### Где `<summary>` обязателен

- **Interfaces**: все члены.
- **Base classes** (от которых наследуются): все public members.
- **Public API** конкретных классов и records.
- **Private fields and methods**: обязательно `<summary>` (не `<inheritdoc/>`).

### `<inheritdoc/>` — для наследников

```csharp
/// <summary>
/// Base user entity.
/// </summary>
public abstract class User
{
    /// <summary>
    /// Unique identifier.
    /// </summary>
    public Guid Id { get; init; }
}

public sealed class ApplicationUser : User
{
    /// <inheritdoc />
    public new Guid Id { get; init; }

    /// <summary>
    /// Internal session cache.
    /// </summary>
    private readonly ConcurrentDictionary<string, Session> sessions = new();
}
```

### Что НЕ требует summary

- `Dispose` / `DisposeAsync` — стандартный паттерн, всегда понятно.
- Locally-scoped helper methods (не private — а локальные функции внутри метода).

---

## 11. Comments

### Inline-комментарии разрешены только когда они **важны**

Запрещены комментарии, дублирующие имя метода или операцию.
Разрешены комментарии, объясняющие **«почему»**, особенно если без
комментария код выглядит «неправильным» и его захочется «починить».

```csharp
// ❌ Wrong — дублирует имя
// increment counter
counter++;

// ❌ Wrong — описывает «что», но это и так видно
// loop through items
foreach (var item in items) ...

// ✅ Correct — объясняет «почему», предотвращает регрессию
// Binance throttles at 1200 req/min — увеличение приведёт к 429
private const int MaxRequestsPerMinute = 1100;

// ✅ Correct — объясняет неочевидный workaround
// EF Core 8 теряет точность для decimal при ToList() — материализуем вручную
var rates = await query.AsAsyncEnumerable().ToListAsync(cancellationToken);

// ✅ TODO/HACK с автором или ссылкой на тикет
// TODO(ANL-1234): убрать после миграции на новый API биржи
```

### Правило ревьюера

Комментарий принимается, если без него **существует реальный риск** того,
что следующий читатель сломает код или потратит время на понимание. Иначе —
удалить, переименовать сущности.

XML-документация (`/// <summary>`) — отдельно, см. раздел 10.

---

## 12. Collections

### Принцип: возвращай минимально достаточный тип

Не возвращай тип с более широкой функциональностью, чем нужно потребителю.

| Что нужно потребителю       | Тип                            |
|-----------------------------|--------------------------------|
| Только итерация             | `IEnumerable<T>` (с осторожностью) |
| Итерация + `Count`          | `IReadOnlyCollection<T>`       |
| Итерация + `Count` + индекс | `IReadOnlyList<T>`             |
| Membership check (contains) | `HashSet<T>` / `IReadOnlySet<T>` |
| Фиксированный набор, max perf | `T[]`                        |

### Default для public API — `IReadOnlyCollection<T>`

Если нужен индексный доступ — `IReadOnlyList<T>`. Если нужен perf на
итерации и размер известен — `T[]`. `List<T>` в публичном API не возвращаем.

```csharp
// ✅ Default — Collection
public IReadOnlyCollection<string> ActiveSymbols => activeSymbols;

// ✅ Нужен индексный доступ потребителю
public IReadOnlyList<Trade> RecentTrades => recentTrades;

// ✅ Фиксированный, часто итерируется — массив
public string[] SupportedExchanges { get; } = ["Binance", "OKX", "Bybit"];
```

### `HashSet<T>` для membership

```csharp
// ✅ Fast lookups — O(1)
private readonly HashSet<string> activeSymbols = new(StringComparer.OrdinalIgnoreCase);

// ❌ Wrong — O(n) на каждый Contains
private readonly List<string> activeSymbols = new();
```

### `List<T>` / `IList<T>` — только локально для мутации

```csharp
// ✅ Локальная мутация — List
var symbols = new List<string>();
foreach (var instrument in instruments)
{
    if (instrument.IsActive)
    {
        symbols.Add(instrument.Symbol);
    }
}

return symbols.ToArray();  // наружу — массив

// ❌ Wrong — List в публичном API
public List<string> Symbols { get; }

// ❌ Wrong — IList наружу, провоцирует мутацию
public IList<Instrument> Instruments { get; }
```

### Anti-patterns

```csharp
// ❌ IEnumerable когда нужен Count или повторная итерация
//    — LINQ дёргает источник заново
public IEnumerable<string> Symbols => symbols;

// ❌ List для lookups — O(n)
public List<string> Symbols { get; }
```

### Empty collections

```csharp
// ✅ Бесплатно
return Array.Empty<Trade>();
return [];  // C# 12 collection expression

// ❌ Аллокация
return new List<Trade>();
return new Trade[0];
```

---

## 13. Interface organization

### Default — папка `Interfaces/`

Интерфейсы лежат в папке `Interfaces/` на одном уровне с реализациями.

```
Services/
├── Interfaces/
│   ├── IProxyHealthChecker.cs
│   └── IProxyRepository.cs
├── ProxyHealthChecker.cs
└── ProxyRepository.cs
```

При большом количестве — группировка по доменам:

```
Services/
├── Interfaces/
│   ├── Health/
│   │   ├── IProxyHealthChecker.cs
│   │   └── IHealthCheckResult.cs
│   └── Persistence/
│       └── IProxyRepository.cs
└── ...
```

### Исключение — one-to-one co-location

Если у интерфейса **ровно одна** реализация и они никогда не разойдутся
(типичный кейс: внутренний сервис, не предполагающий замены) — можно
положить рядом, в одной папке. Тогда у обоих файлов общий префикс имени:
`OrderProcessor.cs` + `IOrderProcessor.cs`.

### Где НЕ обязательно `Interfaces/`

- **Marker interfaces** в проектах-маркерах (`*.Markers`, `*.Abstractions`).
- **Domain abstractions** в `*.Entity.Core` / `*.Domain` — допустимо рядом
  с моделями.

---

## 14. Project structure

### Где разрешены модели

Модели разрешены в проектах с `Models` / `Entities` / `Contracts` в имени
или пути:

- `src/models/**`
- `src/entity/**`
- `src/feature/*/Models/**`
- `src/feature/*/Entities/**`
- `src/feature/*/Contracts/**`
- `src/api/*.Api.Models/**` — отдельный проект для API DTO/Request/Response

### Где НЕ разрешены

- В API-проектах (`*.Api.*`) — кроме папок `Contracts/` или вынесенного
  `*.Api.Models` проекта.
- В worker-проектах — модели идут в общий entity-проект.

**Исключение**: модель, которая используется только внутри одного проекта
и не выходит наружу — допустима локально в этом проекте, в папке `Models/`.


### Private business logic — выносим

**Класс не должен содержать приватную бизнес-логику.**

Допустимо оставить `private`:
- В **HostedService / Worker / BackgroundService** — оркестрирующие методы,
  которые вызывают инжектированные зависимости. Worker — единичный файл,
  и оркестрация — его прямая задача.
- В **Controller / Handler** — методы валидации, форматирования ответа,
  тривиальные хелперы без внешних зависимостей.
- `Dispose` / `DisposeAsync`.
- Static утилиты без DI — в `file static class`.

Выносим в отдельный класс/сервис:
- Любая private-логика, которую можно **переиспользовать**.
- Логика, требующая **изолированного тестирования**.
- Сложная логика с собственными зависимостями.

```csharp
// ✅ HostedService — оркестрирующие private OK
public sealed class ProxyValidationWorker(
    IProxyRepository proxyRepository,
    IProxyHealthChecker proxyHealthChecker,
    ILogger<ProxyValidationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await ValidateProxiesAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    /// <summary>
    /// Validates all alive proxies and updates their state.
    /// </summary>
    private async Task ValidateProxiesAsync(CancellationToken cancellationToken)
    {
        foreach (var proxy in await proxyRepository.GetAliveProxiesAsync(cancellationToken))
        {
            var checkResult = await proxyHealthChecker.CheckAsync(proxy, cancellationToken);

            if (checkResult.IsHealthy)
            {
                await proxyRepository.MarkHealthyAsync(proxy.Id, checkResult.LatencyMs, cancellationToken);
            }
            else
            {
                logger.LogWarning("Proxy {ProxyId} failed health check", proxy.Id);
            }
        }
    }
}

// ✅ Бизнес-логика — отдельный класс с интерфейсом
public interface IProxyHealthChecker
{
    public Task<HealthCheckResult> CheckAsync(ManagedProxy proxy, CancellationToken cancellationToken = default);
}

// ✅ Чистая утилита — file static
file static class ProxyUriBuilder
{
    public static string Build(ManagedProxy proxy)
        => string.IsNullOrEmpty(proxy.Username)
            ? $"http://{proxy.Host}:{proxy.Port}"
            : $"http://{proxy.Username}:{proxy.Password}@{proxy.Host}:{proxy.Port}";
}
```

---

## 15. Required tooling

### `.editorconfig`

Лежит в корне репозитория. Все правила там — `severity = error`
для критичных или `warning` для рекомендаций.

### NuGet packages (Directory.Packages.props / csproj)

```xml
<PackageReference Include="Microsoft.CodeAnalysis.NetAnalyzers" Version="*" />
<PackageReference Include="Microsoft.VisualStudio.Threading.Analyzers" Version="*" />
<PackageReference Include="Meziantou.Analyzer" Version="*" />
<PackageReference Include="Roslynator.Analyzers" Version="*" />
```

Что они дают:

| Package                                       | Что ловит                                                                |
|-----------------------------------------------|--------------------------------------------------------------------------|
| `Microsoft.CodeAnalysis.NetAnalyzers`         | CA-правила: `CA1852` (sealed), `CA2254` (logging), `CA1862` (strings).   |
| `Microsoft.VisualStudio.Threading.Analyzers`  | `VSTHRD200` (async suffix), `VSTHRD103` (`.Result`/`.Wait()`).           |
| `Meziantou.Analyzer`                          | `MA0004` (ConfigureAwait), single-letter lambda warnings, и др.          |
| `Roslynator.Analyzers`                        | Минимальность кода, упрощение pattern matching.                          |

### Architecture tests

Что `.editorconfig` не ловит — проверяется через
[NetArchTest](https://github.com/BenMorris/NetArchTest) или
[ArchUnitNET](https://github.com/TNG/ArchUnitNET) в xUnit:

- Интерфейсы лежат в `Interfaces/` (за исключениями).
- Модели лежат только в Models/Entities/Contracts проектах.
- Controllers зависят только от сервисов из allowed-сборок.
- Запрет на reference между bounded contexts.

Шаблон теста (для NetArchTest):

```csharp
[Fact]
public void Interfaces_should_live_in_Interfaces_folder()
{
    var result = Types.InAssembly(typeof(IUserService).Assembly)
        .That()
        .AreInterfaces()
        .And()
        .DoNotHaveNameMatching("^I[A-Z].*Markers$")
        .Should()
        .ResideInNamespaceMatching(@".*\.Interfaces(\..*)?$")
        .GetResult();

    Assert.True(result.IsSuccessful,
        $"Interfaces outside Interfaces/ folder: {string.Join(", ", result.FailingTypeNames ?? [])}");
}
```

---

## Quick reference: что куда

| Что хочешь проверить                          | Где                                |
|-----------------------------------------------|-------------------------------------|
| `var` обязательно                             | `.editorconfig` — IDE0007/IDE0008   |
| File-scoped namespaces                        | `.editorconfig` — IDE0161           |
| Braces всегда                                 | `.editorconfig` — IDE0011           |
| `sealed` по умолчанию                         | `.editorconfig` — CA1852            |
| `.Result` / `.Wait()` запрет                  | `.editorconfig` — VSTHRD103         |
| Async suffix                                  | `.editorconfig` — VSTHRD200         |
| Underscore prefix запрет                      | `.editorconfig` — naming rule       |
| Microsoft modifier order                      | `.editorconfig` — IDE0036           |
| Pyramid Rule (порядок параметров)             | Код-ревью                           |
| Записать переменную одной строкой с проверкой | Код-ревью + Roslynator hints        |
| Интерфейсы в `Interfaces/`                    | Architecture test                   |
| Модели только в Models-проекте                | Architecture test                   |
| Private business logic вынесена               | Код-ревью                           |
| Корректный выбор `IReadOnlyCollection`/`List` | Код-ревью                           |

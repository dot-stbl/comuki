---
description: xUnit v3 + Shouldly + NSubstitute + Testcontainers — unit/integration test stack, pyramid (integration-first), test naming, anti-patterns
always: true
---

# Testing Rules

Правила тестирования C# / .NET проектов: какой стэк использовать,
как писать unit и integration тесты, что покрывать, что нет.

Структура тест-проектов (плоская `tests/`, нейминг
`<SourceProject>.<Kind>[.<Feature>]`) — в **`PROJECT-STRUCTURE.md §10`**.
Этот файл — про **содержимое** тестов.

---

## Table of Contents

1. [Stack](#1-stack)
2. [Test pyramid: когда unit, когда integration](#2-test-pyramid-когда-unit-когда-integration)
3. [Unit tests](#3-unit-tests)
4. [Integration tests](#4-integration-tests)
5. [Shared test infrastructure](#5-shared-test-infrastructure)
6. [Assertions (Shouldly)](#6-assertions-shouldly)
7. [Mocking (NSubstitute)](#7-mocking-nsubstitute)
8. [Test data](#8-test-data)
9. [Anti-patterns](#9-anti-patterns)
10. [CI integration](#10-ci-integration)

---

## 1. Stack

Фиксированный набор библиотек для **всех** тест-проектов в solution:

| Назначение | Библиотека | Версия |
|------------|------------|--------|
| Test framework | **xUnit v3** | 1.x+ |
| Assertions | **Shouldly** | 4.x+ |
| Mocking | **NSubstitute** | 5.x+ |
| Container infrastructure | **Testcontainers** | 4.x+ |
| Web API testing | **Microsoft.AspNetCore.Mvc.Testing** | matches .NET version |
| DB cleanup between tests | **Respawn** | 6.x+ |
| Fake data generation | **Bogus** | 35.x+ |
| Coverage | **Coverlet.collector** | 6.x+ |

### Почему этот стэк

**xUnit v3** (выпущен в 2025) — переход с xUnit v2 решает основные
исторические проблемы: построен на `Microsoft.Testing.Platform`, не
`VSTest`; быстрее discovery; лучше поддержка async lifecycle. AI-инструменты
и Stack Overflow натренированы на xUnit-синтаксисе, что даёт меньше
сюрпризов чем альтернативы (TUnit, NUnit, MSTest).

**Shouldly** вместо FluentAssertions: FluentAssertions 8.0+ переехала на
коммерческую лицензию (Xceed) в 2025. Shouldly — MIT, стабильная, имеет
лучшие сообщения об ошибках (показывает имя переменной из исходного кода).

**NSubstitute** вместо Moq: Moq в 2023 встроил SponsorLink (сбор email
разработчиков), доверие подорвано. NSubstitute — простой синтаксис без
`.Setup()`-цепочек, читается как обычный код.

**Testcontainers** для integration тестов: реальная Postgres/Redis в
Docker даёт точное воспроизведение продакшна. EF Core InMemory и SQLite
имеют разную семантику с реальной БД (case sensitivity, ordering,
constraints) — найденные ими баги ложные, а пропущенные — реальные.

**Respawn** для очистки БД между тестами: быстрее `DROP DATABASE` /
пересоздания, работает на уровне TRUNCATE с сохранением структуры.

### Исключения из CODING-RULES для тестов

Тесты следуют **`CODING-RULES.md`** и **`FRAMEWORK-RULES.md`** с одним
формальным исключением:

**`IAsyncLifetime.InitializeAsync()` / `DisposeAsync()` не принимают
`CancellationToken`.** Это override интерфейса xUnit, сигнатура зафиксирована
библиотекой — добавить параметр невозможно. Правило «`CancellationToken`
последним» (FRAMEWORK §6) применяется к **нашим** методам, не к override
чужих интерфейсов.

```csharp
// ✅ Корректно — это override IAsyncLifetime
public async Task InitializeAsync()
{
    await Container.StartAsync();
}

// ✅ Наши собственные методы — с CancellationToken
public async Task ResetAsync(CancellationToken cancellationToken = default)
{
    await respawner.ResetAsync(connection, cancellationToken);
}
```

В остальном тестовый код подчиняется тем же правилам: `var`,
file-scoped namespaces, braces везде, осмысленные lambda-имена, structured
logging, `is null` вместо `== null`, и так далее.

### Csproj шаблон тест-проекта

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="xunit.v3" Version="1.*" />
    <PackageReference Include="Shouldly" Version="4.*" />
    <PackageReference Include="NSubstitute" Version="5.*" />
    <PackageReference Include="coverlet.collector" Version="6.*" />
  </ItemGroup>

  <!-- Только для integration projects: -->
  <ItemGroup Condition="'$(IsIntegrationTest)' == 'true'">
    <PackageReference Include="Testcontainers.PostgreSql" Version="4.*" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="9.*" />
    <PackageReference Include="Respawn" Version="6.*" />
  </ItemGroup>

</Project>
```

Общие пакеты (xUnit, Shouldly, NSubstitute, coverlet) выносятся в
`Directory.Build.props` для всех проектов в `tests/` через
`Condition="'$(IsTestProject)' == 'true'"`. csproj тест-проекта остаётся
минимальным.

---

## 2. Test pyramid: когда unit, когда integration

### Главный принцип

> **Integration first.** Unit — там, где integration избыточен или
> невозможен.

Большинство кода тестируется через integration: API endpoint + БД +
сервисы вместе. Это даёт максимальную уверенность за один тест.

Unit-тесты пишутся **только** когда выполняется хотя бы одно условие:

1. **Чистая логика без I/O** — калькулятор, парсер, форматтер, валидатор,
   стратегия. Integration-тест здесь не даст ничего нового — нет БД, нет
   HTTP, нет внешних сервисов.
2. **Сложная логика с большим количеством веток** — algorithm с 10+ cases.
   Integration-тест каждый case сделает медленным и сложным; unit-тест
   с `[Theory]` покрывает за секунду.
3. **Integration невозможен или дорог** — взаимодействие с реальным
   внешним API биржи, платёжным провайдером и т.д. Здесь нужны
   contract tests или unit с моками клиента.

### Дерево решений

```
Что тестируем?

├── Калькулятор / парсер / форматтер / валидатор?
│   └── UNIT (чистая функция, integration не нужен)
│
├── Стратегия / алгоритм / pattern matching с многими ветками?
│   └── UNIT с [Theory] + InlineData (или MemberData)
│
├── Reposiotry / EF query / SQL?
│   └── INTEGRATION с Testcontainers (тестировать DbContext бессмысленно)
│
├── HTTP endpoint?
│   └── INTEGRATION через WebApplicationFactory
│
├── Сервис, который ходит во внешний API биржи?
│   ├── Hot path (логика обработки ответа)        → UNIT с моком клиента
│   └── Сам клиент к API                          → CONTRACT tests против sandbox
│
├── Workflow из нескольких сервисов?
│   └── INTEGRATION end-to-end через API или message bus
│
└── HostedService / BackgroundService?
    └── INTEGRATION в составе WebApplicationFactory + ожидание side-effects
```

### Что НЕ тестируем вообще

- **DTO / Records** без логики (только `init`-properties) — нечего тестировать.
- **EF Core entities** (если только в них нет custom-методов) — конфигурация
  проверяется integration-тестом запроса.
- **Auto-mapped профили AutoMapper/Mapperly** — если простой 1:1 mapping,
  ловится при сборке. Если есть кастомные resolver-ы — тестируем их.
- **Microsoft / NuGet библиотеки** — мы их не пишем.
- **Один-в-один обёртки** над сторонним API без логики.

### Пропорция

Ориентир для типичного проекта:
- **70%** integration тесты
- **30%** unit тесты (там где это правда чистая логика)

Не самоцель — это констатация того, что получится, если следовать
правилам выше.

---

## 3. Unit tests

### Структура: AAA (Arrange / Act / Assert)

Каждый тест состоит из трёх явно разделённых блоков. Разделять пустыми
строками **обязательно** — это форма документации.

```csharp
[Fact]
public void ReturnZeroForEmptyOrders()
{
    // Arrange
    var calculator = new SpreadCalculator();
    var orders = Array.Empty<Order>();

    // Act
    var spread = calculator.Calculate(orders);

    // Assert
    spread.ShouldBe(0m);
}
```

Комментарии `// Arrange`, `// Act`, `// Assert` **не пишем** — пустые
строки уже делают структуру очевидной. Комментарии добавляются только
если в секции есть нетривиальные действия, требующие пояснения.

### Нейминг тестов

**В проекте два стиля именования**, выбираются **per-class, не per-method**.
Класс обязан быть выдержан в одном стиле до конца.

#### 1. BDD-стиль — для unit-тестов одного класса

Класс — `<ClassUnderTest>Should`, метод — `PascalCase` без подчёркиваний,
человекочитаемое описание — в `[Fact(DisplayName = "...")]` /
`[Theory(DisplayName = "...")]`.

```csharp
// ✅ Correct
public sealed class SpreadCalculatorShould
{
    [Fact(DisplayName = "Given empty orders, when Calculate is called, then returns zero")]
    public void ReturnZeroForEmptyOrders()
    {
        var calculator = new SpreadCalculator();

        var spread = calculator.Calculate(Array.Empty<Order>());

        spread.ShouldBe(0m);
    }

    [Fact(DisplayName = "Given orders with negative price, when Calculate is called, then throws")]
    public void ThrowWhenOrdersContainNegativePrice()
    {
        var calculator = new SpreadCalculator();

        Should.Throw<ArgumentException>(() => calculator.Calculate([new Order(-1m, 1)]));
    }
}
```

Почему:

- **Метод PascalCase** — консистентно с CODING-RULES §1 (no snake_case).
- **`<Subject>Should`** — класс читается как «`SpreadCalculator` should
  `ReturnZeroForEmptyOrders`». Не нужен суффикс `Tests`.
- **`DisplayName` в Given/When/Then** — runner покажет осмысленное описание
  с предусловиями. CI-логи и failure-reports становятся документацией.

#### 2. Assertion (sentence) — для архитектурных / constraint тестов

Класс — `<Concern>Tests` (например `LayerDependencyTests`),
метод — **`PascalCase_With_Underscores`** (C# стиль, каждое слово
с заглавной, разделитель `_`). Метод читается как законченное предложение
без `DisplayName`:

```csharp
// ✅ Correct
public sealed class LayerDependencyTests
{
    /// <summary>
    /// Api layer must not reach into Database directly.
    /// All persistence goes through feature/Orchestration or models/Contracts.
    /// </summary>
    [Fact]
    public void ApiPublic_Must_Not_Reference_Database() { ... }

    [Fact]
    public void Models_Must_Not_Reference_Database() { ... }

    [Fact]
    public void Feature_Must_Not_Reference_Api() { ... }
}
```

Почему:

- **Sentence-форма** читается сама по себе, без `DisplayName` —
  имя метода и есть документация архитектурного правила.
- **PascalCase_With_Underscores** — C# convention для многословных имён
  в `public` API (CODING-RULES §1 «Имена методов»). `_` разрешён как
  разделитель, в отличие от snake_case, который запрещён (`lower_with_under`).
- Каждое слово с заглавной — `Models_Must_Not_Reference_Database`,
  НЕ `Models_must_not_reference_Database`.
- `<Concern>Tests` — стандартный .NET convention для **архитектурных /
  инфраструктурных** тестов (NetArchTest, contract tests, smoke tests
  без конкретного Subject).

#### Стиль выбирается per-class, не per-method

```csharp
// ✅ Correct — единый стиль в классе
public sealed class SpreadCalculatorShould   // BDD
{
    public void ReturnZeroForEmptyOrders() { }      // BDD
    public void ThrowWhenNegative() { }              // BDD
}

public sealed class LayerDependencyTests  // assertion
{
    public void Models_Must_Not_Reference_Database() { }       // assertion
    public void ApiPublic_Must_Not_Reference_Database() { }    // assertion
}

// ❌ Wrong — микс в одном классе
public sealed class CalculatorTests
{
    public void ReturnZeroForEmptyOrders() { }                  // BDD
    public void Models_Must_Not_Reference_Database() { }       // assertion — нарушение
}
```

### Имя метода: только «что должно произойти»

Имя метода описывает **результат**, без условий. Условия идут в `DisplayName`.

```csharp
// ✅ Correct
[Fact(DisplayName = "Given valid order request, when POST /api/orders, then creates order")]
public async Task CreateOrder() { ... }

[Fact(DisplayName = "Given empty symbol, when POST /api/orders, then returns 400")]
public async Task ReturnBadRequestForEmptySymbol() { ... }

[Fact(DisplayName = "Given missing auth token, when GET /api/orders, then returns 401")]
public async Task ReturnUnauthorizedWhenTokenMissing() { ... }

// ❌ Wrong — снова snake_case, нарушение CODING-RULES
public async Task CreateOrder() { ... }

// ❌ Wrong — Test1 не говорит ничего
public void Test1() { ... }
```

### Класс — один Subject + опционально один Method

**Default**: один класс на тестируемый класс.

```csharp
// Тестируем класс SpreadCalculator
public sealed class SpreadCalculatorShould { ... }
```

**Если методов в Subject много** и тестов на каждый по 5+ — разделяем
вложенными классами **по методу**:

```csharp
public sealed class OrderProcessorShould
{
    public sealed class ProcessAsyncShould
    {
        [Fact(DisplayName = "Given new order, when ProcessAsync, then marks as placed")]
        public async Task MarkOrderAsPlaced() { ... }

        [Fact(DisplayName = "Given duplicate order, when ProcessAsync, then returns existing")]
        public async Task ReturnExistingOrderForDuplicate() { ... }
    }

    public sealed class CancelAsyncShould
    {
        [Fact(DisplayName = "Given placed order, when CancelAsync, then marks as cancelled")]
        public async Task MarkOrderAsCancelled() { ... }
    }
}
```

Альтернатива (для очень больших Subject) — отдельные классы на метод:

```csharp
public sealed class OrderProcessorProcessAsyncShould { ... }
public sealed class OrderProcessorCancelAsyncShould { ... }
```

Используй вложенные классы, если они помещаются в один файл. Отдельные
классы — если каждый растёт до 200+ строк.

### `DisplayName` — Given / When / Then

Формат: **«Given `<precondition>`, when `<action>`, then `<expected>`»**.

Каждая часть короткая (5–10 слов). Не пиши Given/When/Then капсом — это
не Gherkin, это просто структура английского предложения.

```csharp
// ✅ Структурно
[Fact(DisplayName = "Given user with admin role, when DELETE /api/orders/{id}, then returns 204")]

// ✅ Когда нет meaningful precondition — упрощаем до When/Then
[Fact(DisplayName = "When Calculate is called with empty orders, then returns zero")]

// ✅ Для unit-теста чистой функции — When/Then достаточно
[Fact(DisplayName = "When parsing 'BTCUSDT', then returns BTC and USDT")]
```

`DisplayName` обязателен для **integration тестов** и **сложных unit-тестов**.
Для простых unit с очевидным именем метода — опционален:

```csharp
// ✅ Имя метода говорит всё, DisplayName не нужен
[Fact]
public void ReturnZeroForEmptyOrders() { ... }

// ✅ Сложный сценарий — DisplayName объясняет контекст
[Fact(DisplayName = "Given concurrent requests modifying same order, when both call UpdateAsync, then last write wins")]
public async Task HandleConcurrentUpdates() { ... }
```

### `[Theory]` — DisplayName + InlineData

`DisplayName` описывает общий паттерн, конкретные значения подставит xUnit:

```csharp
[Theory(DisplayName = "Given two integers, when Add is called, then returns sum")]
[InlineData(0, 0, 0)]
[InlineData(1, 1, 2)]
[InlineData(-1, 1, 0)]
[InlineData(int.MaxValue, 1, int.MinValue)]
public void ReturnSum(int left, int right, int expected)
{
    var sum = Calculator.Add(left, right);

    sum.ShouldBe(expected);
}
```

В runner это покажется как:

```
✓ Given two integers, when Add is called, then returns sum(left: 0, right: 0, expected: 0)
✓ Given two integers, when Add is called, then returns sum(left: 1, right: 1, expected: 2)
```

### `[Fact]` vs `[Theory]`

**`[Fact]`** — один конкретный сценарий, один ожидаемый результат.

**`[Theory]` + `[InlineData]`** — один и тот же логический сценарий с
разными входными данными.

```csharp
[Theory]
[InlineData(0, 0, 0)]
[InlineData(1, 1, 2)]
[InlineData(-1, 1, 0)]
[InlineData(int.MaxValue, 1, int.MinValue)]
public void ReturnSum(int left, int right, int expected)
{
    var sum = Calculator.Add(left, right);

    sum.ShouldBe(expected);
}
```

**`[MemberData]`** — когда `InlineData` не хватает (объекты, коллекции):

```csharp
public static TheoryData<Order[], decimal> OrderSpreadCases =>
[
    { [], 0m },
    { [new Order(100m, 1)], 0m },
    { [new Order(100m, 1), new Order(105m, 1)], 5m },
];

[Theory]
[MemberData(nameof(OrderSpreadCases))]
public void ReturnCorrectSpread(Order[] orders, decimal expected)
{
    var spread = new SpreadCalculator().Calculate(orders);

    spread.ShouldBe(expected);
}
```

`TheoryData<T1, T2, ...>` — типизированная альтернатива `IEnumerable<object[]>`.
Используем её, не голый `object[]`.

### Один Arrange, один Act, один Assert (концептуально)

«Один» не значит «одна строка». Значит — **одно действие, которое
тестируем**, и **одно утверждение о результате**. Можно проверять
несколько свойств одного объекта:

```csharp
// ✅ Один act, один логический assert
[Fact]
public void CreateOrderWithCorrectFields()
{
    var order = new OrderBuilder()
        .WithSymbol("BTCUSDT")
        .WithQuantity(1.5m)
        .Build();

    order.Symbol.ShouldBe("BTCUSDT");
    order.Quantity.ShouldBe(1.5m);
    order.Status.ShouldBe(OrderStatus.New);
}

// ❌ Два разных act — нужно разбить на два теста
[Fact]
public void BuildOrders()
{
    var order1 = new OrderBuilder().WithSymbol("BTC").Build();
    order1.Symbol.ShouldBe("BTC");

    var order2 = new OrderBuilder().WithQuantity(1m).Build();
    order2.Quantity.ShouldBe(1m);
}
```

### Тестирование исключений

```csharp
// ✅ Через Shouldly
[Fact]
public void ThrowWhenOrdersIsNull()
{
    var calculator = new SpreadCalculator();

    Should.Throw<ArgumentNullException>(() => calculator.Calculate(null!));
}

// ✅ Async версия
[Fact]
public async Task ThrowWhenIdIsEmpty()
{
    var service = new UserService();

    await Should.ThrowAsync<ArgumentException>(
        async () => await service.GetUserAsync(Guid.Empty));
}

// ✅ Проверка содержимого исключения
[Fact]
public void ThrowWithDescriptiveMessageForInvalidFormat()
{
    var parser = new SymbolParser();

    var exception = Should.Throw<FormatException>(() => parser.Parse("BAD"));
    exception.Message.ShouldContain("Expected format: <BASE><QUOTE>");
}
```

---

## 4. Integration tests

### Принципы

1. **Реальная БД через Testcontainers** — не in-memory, не SQLite.
2. **Один контейнер на класс тестов** через `IClassFixture<T>`. Между
   тестами — Respawn чистит данные.
3. **API тестируем через `HttpClient`** из `WebApplicationFactory`, не
   напрямую через сервисы. Так тестируем pipeline целиком: routing,
   model binding, middleware, фильтры.
4. **Никаких production-secrets** — все настройки через
   `appsettings.Test.json` или environment variables в фикстуре.

> Этот раздел показывает «голые» примеры фикстур и тестов для понимания
> базовых механизмов. В реальном коде ты будешь использовать base classes
> и factory из проекта `<Company>.<App>.Testing` — см. **раздел 5**.

### Базовая фикстура для БД

```csharp
public sealed class PostgresContainerFixture : IAsyncLifetime
{
    public PostgreSqlContainer Container { get; } = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    public string ConnectionString => Container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await Container.StartAsync();

        // Apply migrations
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        await using var dbContext = new InventoryDbContext(options);
        await dbContext.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await Container.DisposeAsync();
}
```

### Базовая фикстура для API

```csharp
public sealed class ShopApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgresContainerFixture postgresFixture = new();
    private Respawner? respawner;

    public async Task InitializeAsync()
    {
        await postgresFixture.InitializeAsync();

        // WebApplicationFactory создаёт хост лениво при первом обращении
        // — вызываем CreateClient() чтобы он стартовал
        _ = CreateClient();

        await using var connection = new NpgsqlConnection(postgresFixture.ConnectionString);
        await connection.OpenAsync();

        respawner = await Respawner.CreateAsync(connection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = ["public"],
        });
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<InventoryDbContext>>();
            services.AddDbContext<InventoryDbContext>(options =>
                options.UseNpgsql(postgresFixture.ConnectionString));
        });
    }

    public async Task ResetDatabaseAsync()
    {
        await using var connection = new NpgsqlConnection(postgresFixture.ConnectionString);
        await connection.OpenAsync();
        await respawner!.ResetAsync(connection);
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await postgresFixture.DisposeAsync();
    }
}
```

### Тест API

```csharp
[Collection(nameof(ShopApiCollection))]
public sealed class CreateOrderEndpointShould(ShopApiFactory factory) : IAsyncLifetime
{
    public async Task InitializeAsync() => await factory.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task CreateOrderAndReturn201()
    {
        var client = factory.CreateClient();
        var request = new CreateOrderRequest
        {
            Symbol = "BTCUSDT",
            Quantity = 1.5m,
        };

        var response = await client.PostAsJsonAsync("/api/orders", request);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<OrderResponse>();
        created.ShouldNotBeNull();
        created.Symbol.ShouldBe("BTCUSDT");
        created.Quantity.ShouldBe(1.5m);
    }

    [Fact]
    public async Task ReturnBadRequestForInvalidSymbol()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/orders", new CreateOrderRequest
        {
            Symbol = "",
            Quantity = 1m,
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}

[CollectionDefinition(nameof(ShopApiCollection))]
public sealed class ShopApiCollection : ICollectionFixture<ShopApiFactory>;
```

### `IClassFixture<T>` vs `ICollectionFixture<T>`

| Когда | Что использовать |
|-------|------------------|
| Контейнер для **одного** класса тестов | `IClassFixture<T>` |
| Контейнер шарится **между классами** | `ICollectionFixture<T>` + `[Collection]` |

Для Testcontainers всегда `ICollectionFixture` — Docker-контейнер стоит
дорого подниматься, шарим между всеми API-тестами одного типа.

### Respawn vs пересоздание БД

**Respawn** — `TRUNCATE` всех таблиц с сохранением структуры. Быстро,
~100ms на 50 таблиц.

**`Database.EnsureDeletedAsync()` + `MigrateAsync()`** — пересоздание
схемы. Медленно, ~5s. Используем **только** при тестах самих миграций.

```csharp
// ✅ В каждом тесте
public async Task InitializeAsync() => await factory.ResetDatabaseAsync();

// ❌ Не делаем так — это убьёт скорость
public async Task InitializeAsync()
{
    await dbContext.Database.EnsureDeletedAsync();
    await dbContext.Database.MigrateAsync();
}
```

### Параллелизация

xUnit по умолчанию параллелит **классы тестов**, но не методы внутри
класса. Для integration тестов **отключаем параллелизацию для коллекции**,
которая шарит контейнер — иначе тесты будут стирать данные друг друга
через Respawn:

```csharp
[CollectionDefinition(nameof(ShopApiCollection), DisableParallelization = true)]
public sealed class ShopApiCollection : ICollectionFixture<ShopApiFactory>;
```

Между **разными** integration-коллекциями параллелизация остаётся —
каждая имеет свой контейнер.

### Что НЕ должно быть в integration тесте

- **Hard-coded URL продакшна** — только `factory.CreateClient()`.
- **Реальные secrets / production connection strings** — только тестовые.
- **`Thread.Sleep` для ожидания async операций** — используй polling
  через `WaitForAsync` или `IHostedService` lifecycle.
- **Зависимости между тестами** — каждый тест работает с чистой БД.
- **`[Trait("Category", "Integration")]` ручные** — категория зашита в
  имени проекта (см. `PROJECT-STRUCTURE.md §10`).

## 5. Shared test infrastructure

Чтобы тест-классы не дублировали setup для Testcontainers, WebApplicationFactory,
Respawn, DI-доступа, аутентификации и seed-данных — общая инфраструктура
вынесена в отдельный проект **`tests/<Company>.<App>.Testing/`**.

Подход — **гибрид**:

- **Base classes через наследование** для базовой инфраструктуры
  (factory, client, `GetService<T>`, reset БД).
- **Extension methods** для специфики (аутентификация, seeding, ожидания
  side-effects).

Композиция даёт минимальный boilerplate в тест-классах, но не запирает в
жёсткой иерархии «AuthedTestBase / MessageBusTestBase / ...».

### Проект-инфраструктура

```
tests/
├── Acme.Shop.Testing/                            # инфраструктура (не тесты!)
│   ├── Acme.Shop.Testing.csproj
│   ├── Fixtures/
│   │   ├── PostgresContainerFixture.cs
│   │   └── RedisContainerFixture.cs
│   ├── Factories/
│   │   ├── IIntegrationFactory.cs                # общий контракт
│   │   ├── ShopApiFactory.cs                     # для Acme.Shop.Api.Public
│   │   ├── CollectorWorkerFactory.cs             # для Northwind.Logistics.Collector
│   │   └── TelegramBotFactory.cs                 # для Acme.Shop.Bots.Telegram
│   ├── Bases/
│   │   ├── IntegrationTestBase.cs                # generic база
│   │   ├── ApiIntegrationTestBase.cs             # специализация для API
│   │   └── HostedServiceTestBase.cs              # специализация для worker-ов
│   ├── Extensions/
│   │   ├── HttpClientExtensions.cs               # AuthenticateAs, ReadJsonAsync
│   │   ├── FactoryExtensions.cs                  # SeedAsync, CountInDbAsync
│   │   └── WaitExtensions.cs                     # WaitForAsync, WaitForLogAsync
│   ├── Builders/
│   │   ├── OrderBuilder.cs
│   │   └── UserBuilder.cs
│   └── TestData/
│       ├── KnownIds.cs                           # фиксированные Guid для тестов
│       └── SeedDataFactory.cs

├── Acme.Shop.Api.Public.Integration.Orders/       # обычные тест-проекты
│   └── ссылается на Acme.Shop.Testing
└── Acme.Shop.Api.Public.Integration.Auth/
    └── ссылается на Acme.Shop.Testing
```

### Нейминг проекта-инфраструктуры

| Хорошо | Плохо | Почему |
|--------|-------|--------|
| `Acme.Shop.Testing` | `Acme.Shop.Tests.Common` | «Common» — generic постфикс (см. `PROJECT-STRUCTURE.md §6`) |
| `Acme.Shop.Testing` | `Acme.Shop.TestInfrastructure` | длинно, читается тяжелее |
| `Acme.Shop.Testing` | `Acme.Shop.TestHelpers` | «Helpers» — запрещённое имя |

Постфикс **`.Testing`** — устоявшаяся .NET-конвенция (например,
`Microsoft.AspNetCore.Mvc.Testing` именно так и называется).

### Размещение в structure

Проект **`<Company>.<App>.Testing`** лежит в `tests/` рядом с тест-проектами,
но это **не тест-проект** в строгом смысле: он не содержит тестов сам, в
нём нет `[Fact]`, его не запускают через `dotnet test`. CI его собирает,
но не прогоняет (см. раздел 10).

В csproj — `IsPackable=false`, **без** `IsTestProject=true` и
**без** `UseMicrosoftTestingPlatformRunner=true`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="xunit.v3" Version="1.*" />
    <PackageReference Include="Shouldly" Version="4.*" />
    <PackageReference Include="NSubstitute" Version="5.*" />
    <PackageReference Include="Testcontainers.PostgreSql" Version="4.*" />
    <PackageReference Include="Testcontainers.Redis" Version="4.*" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="9.*" />
    <PackageReference Include="Respawn" Version="6.*" />
    <PackageReference Include="Bogus" Version="35.*" />
  </ItemGroup>

  <ItemGroup>
    <!-- Ссылки на src-проекты, которые тестируются -->
    <ProjectReference Include="..\..\src\application\api\Acme.Shop.Api.Public\Acme.Shop.Api.Public.csproj" />
    <ProjectReference Include="..\..\src\database\Acme.Shop.Database.Customers\Acme.Shop.Database.Customers.csproj" />
  </ItemGroup>

</Project>
```

### `IIntegrationFactory` — общий контракт

Все factory реализуют один интерфейс. Это позволяет писать generic base
classes, работающие с любым типом приложения (API, worker, bot):

```csharp
public interface IIntegrationFactory : IAsyncLifetime
{
    /// <summary>
    /// DI container поднятого приложения.
    /// </summary>
    public IServiceProvider Services { get; }

    /// <summary>
    /// Сбрасывает состояние между тестами: Respawn для БД, очистка кешей,
    /// сброс state hosted services.
    /// </summary>
    public Task ResetAsync();
}
```

API-factory дополнительно реализует `IApiFactory`:

```csharp
public interface IApiFactory : IIntegrationFactory
{
    /// <summary>
    /// HTTP-клиент к поднятому API.
    /// </summary>
    public HttpClient CreateClient();
}
```

Worker/bot factory имеют свои интерфейсы по необходимости:

```csharp
public interface IHostedServiceFactory : IIntegrationFactory
{
    /// <summary>
    /// Запускает hosted services и ждёт их готовности.
    /// </summary>
    public Task StartHostedServicesAsync();
}
```

### Конкретная factory для API

```csharp
public sealed class ShopApiFactory : WebApplicationFactory<Program>, IApiFactory
{
    private readonly PostgresContainerFixture postgres = new();
    private Respawner? respawner;

    public new IServiceProvider Services => base.Services;

    public async Task InitializeAsync()
    {
        await postgres.InitializeAsync();

        // Заставляем хост запуститься
        _ = CreateClient();

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();

        respawner = await Respawner.CreateAsync(connection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = ["public"],
        });
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<CustomersDbContext>>();
            services.AddDbContext<CustomersDbContext>(options =>
                options.UseNpgsql(postgres.ConnectionString));
        });
    }

    public async Task ResetAsync()
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await respawner!.ResetAsync(connection);
    }

    public new HttpClient CreateClient() => base.CreateClient();

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await postgres.DisposeAsync();
    }
}
```

### Generic base classes

#### `IntegrationTestBase<TFactory>` — базовый, для любого приложения

```csharp
[Collection(IntegrationTestCollection.Name)]
public abstract class IntegrationTestBase<TFactory>(TFactory factory) : IAsyncLifetime
    where TFactory : class, IIntegrationFactory
{
    protected TFactory Factory { get; } = factory;

    /// <summary>
    /// Получает сервис из DI поднятого приложения.
    /// Каждый вызов возвращает новый scope.
    /// </summary>
    protected T GetService<T>() where T : notnull
    {
        using var scope = Factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<T>();
    }

    /// <summary>
    /// Выполняет действие внутри DI scope, гарантируя корректное освобождение.
    /// </summary>
    protected async Task UsingScopeAsync(Func<IServiceProvider, Task> action)
    {
        using var scope = Factory.Services.CreateScope();
        await action(scope.ServiceProvider);
    }

    public virtual Task InitializeAsync() => Factory.ResetAsync();
    public virtual Task DisposeAsync() => Task.CompletedTask;
}
```

#### `ApiIntegrationTestBase<TFactory>` — для API-тестов

```csharp
public abstract class ApiIntegrationTestBase<TFactory>(TFactory factory)
    : IntegrationTestBase<TFactory>(factory)
    where TFactory : class, IApiFactory
{
    /// <summary>
    /// HTTP-клиент к тестируемому API. Создаётся один раз на тест.
    /// </summary>
    protected HttpClient Client { get; } = factory.CreateClient();
}
```

Использование — минимум boilerplate:

```csharp
public sealed class CreateOrderEndpointShould(ShopApiFactory factory)
    : ApiIntegrationTestBase<ShopApiFactory>(factory)
{
    [Fact]
    public async Task CreateNewOrder()
    {
        var response = await Client.PostAsJsonAsync("/api/orders", new CreateOrderRequest
        {
            Symbol = "BTCUSDT",
            Quantity = 1.5m,
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task PersistOrderToDatabase()
    {
        await Client.PostAsJsonAsync("/api/orders", new CreateOrderRequest { ... });

        var orderRepository = GetService<IOrderRepository>();
        var orders = await orderRepository.GetAllAsync();
        orders.ShouldHaveSingleItem();
    }
}
```

В тесте нет:
- ручного `IClassFixture<T>` (зашит в base через `[Collection]`)
- ручного `CreateClient()` (зашит в `ApiIntegrationTestBase`)
- ручного `ResetAsync` (зашит в `InitializeAsync`)

#### `HostedServiceTestBase<TFactory>` — для воркеров

```csharp
public abstract class HostedServiceTestBase<TFactory>(TFactory factory)
    : IntegrationTestBase<TFactory>(factory)
    where TFactory : class, IHostedServiceFactory
{
    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await Factory.StartHostedServicesAsync();
    }
}
```

Использование:

```csharp
public sealed class OrderCollectorShould(CollectorWorkerFactory factory)
    : HostedServiceTestBase<CollectorWorkerFactory>(factory)
{
    [Fact]
    public async Task ProcessPendingOrdersWithinFiveSeconds()
    {
        // Seed pending order
        await Factory.SeedPendingOrderAsync(symbol: "BTCUSDT");

        // Wait for processing
        await Factory.WaitForAsync(
            condition: async () => (await GetService<IOrderRepository>().GetProcessedCountAsync()) > 0,
            timeout: TimeSpan.FromSeconds(5));

        var orderRepository = GetService<IOrderRepository>();
        var processedCount = await orderRepository.GetProcessedCountAsync();
        processedCount.ShouldBe(1);
    }
}
```

### Extension methods для специфики

Composable дополнения — лежат в `Extensions/` проекта `.Testing`.

#### Аутентификация

```csharp
public static class HttpClientAuthExtensions
{
    /// <summary>
    /// Добавляет JWT в заголовок Authorization.
    /// </summary>
    public static HttpClient AuthenticateAs(this HttpClient client, string userId, params string[] roles)
    {
        var token = TestTokenFactory.Create(userId, roles);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>
    /// Снимает аутентификацию (для тестов на 401).
    /// </summary>
    public static HttpClient WithoutAuth(this HttpClient client)
    {
        client.DefaultRequestHeaders.Authorization = null;
        return client;
    }
}
```

Использование:

```csharp
[Fact]
public async Task RequireAdminRoleToDelete()
{
    Client.AuthenticateAs("user-123", roles: ["User"]);

    var response = await Client.DeleteAsync("/api/orders/abc");

    response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
}
```

#### Seeding

```csharp
public static class FactorySeedingExtensions
{
    /// <summary>
    /// Добавляет entity в БД через DI scope.
    /// </summary>
    public static async Task<TEntity> SeedAsync<TEntity>(
        this IIntegrationFactory factory,
        TEntity entity)
        where TEntity : class
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CustomersDbContext>();
        await dbContext.Set<TEntity>().AddAsync(entity);
        await dbContext.SaveChangesAsync();
        return entity;
    }

    public static Task<User> SeedUserAsync(this IIntegrationFactory factory, string email = "test@example.com")
        => factory.SeedAsync(new UserBuilder().WithEmail(email).Build());
}
```

Использование:

```csharp
[Fact]
public async Task ReturnSeededUser()
{
    var user = await Factory.SeedUserAsync(email: "alice@example.com");

    var response = await Client.GetAsync($"/api/users/{user.Id}");

    response.EnsureSuccessStatusCode();
    var returned = await response.Content.ReadFromJsonAsync<UserResponse>();
    returned!.Email.ShouldBe("alice@example.com");
}
```

#### Ожидания async side-effects

```csharp
public static class WaitExtensions
{
    /// <summary>
    /// Опрашивает condition до тех пор, пока он не вернёт true,
    /// или истечёт timeout.
    /// </summary>
    public static async Task WaitForAsync(
        this IIntegrationFactory factory,
        Func<Task<bool>> condition,
        TimeSpan timeout,
        TimeSpan? pollInterval = null,
        [CallerArgumentExpression(nameof(condition))] string? description = null)
    {
        var interval = pollInterval ?? TimeSpan.FromMilliseconds(50);
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(interval);
        }

        throw new TimeoutException(
            $"Condition '{description}' not met within {timeout.TotalSeconds:F1}s");
    }
}
```

`[CallerArgumentExpression]` автоматически берёт текст condition в
сообщение об ошибке — `Condition '() => count > 0' not met within 5.0s`.

### Когда наследоваться, когда писать extension

| Что | Куда |
|-----|------|
| Базовый setup (factory, client, GetService) | `*TestBase` через наследование |
| Сброс БД, lifecycle | `*TestBase.InitializeAsync` |
| Аутентификация | extension на `HttpClient` |
| Seeding entity в БД | extension на `IIntegrationFactory` |
| Ожидание async условия | extension на `IIntegrationFactory` |
| Проверка содержимого БД | extension на `IIntegrationFactory` |
| Парсинг response с типизацией | extension на `HttpResponseMessage` |
| Что-то нужно один раз в одном тесте | в самом тесте, не выноси |

Критерий выноса в `.Testing`: код **дублируется** в 3+ тест-классах **и**
не содержит специфики конкретного теста. Один раз — оставь в тесте.

### Чего НЕ должно быть в `.Testing`

- **Конкретные тесты** (`[Fact]`, `[Theory]`) — это **не** тест-проект.
- **Production-логика** — только инфраструктура для тестирования.
- **Логика валидации входных данных** для setup — Builder может иметь
  defaults, но не выкидывать exception на «неправильные» данные. Тесты
  должны падать на assertion, не на setup.
- **Mutable static state** — `public static int CallCount` сломает
  параллельные тесты.

### Multiple factory в одном solution

Если в solution несколько entry-points (`api/Public`, `internal/Collector`,
`bots/Telegram`) — у каждого свой Factory в проекте `.Testing`:

```csharp
// API
public sealed class ShopApiFactory : WebApplicationFactory<Program>, IApiFactory { ... }

// Worker — другой Program.cs
public sealed class CollectorWorkerFactory : WebApplicationFactory<Program>, IHostedServiceFactory { ... }

// Bot — Console host, не WebApplicationFactory
public sealed class TelegramBotFactory : IIntegrationFactory { ... }
```

Проблема: `WebApplicationFactory<Program>` ссылается на класс `Program`,
которых в solution несколько. Решается через alias в csproj:

```xml
<ItemGroup>
  <ProjectReference Include="..\..\src\application\api\Acme.Shop.Api.Public\Acme.Shop.Api.Public.csproj">
    <Aliases>ShopApi</Aliases>
  </ProjectReference>
  <ProjectReference Include="..\..\src\application\internal\Northwind.Logistics.Collector\Northwind.Logistics.Collector.csproj">
    <Aliases>CollectorWorker</Aliases>
  </ProjectReference>
</ItemGroup>
```

И в коде:

```csharp
extern alias ShopApi;
extern alias CollectorWorker;

public sealed class ShopApiFactory : WebApplicationFactory<ShopApi::Program> { ... }
public sealed class CollectorWorkerFactory : WebApplicationFactory<CollectorWorker::Program> { ... }
```

Альтернатива (проще) — сделать `Program` partial и добавить marker-класс
в каждом entry-point:

```csharp
// Acme.Shop.Api.Public/Program.cs
public partial class Program;

// Northwind.Logistics.Collector/Program.cs
public partial class Program;
```

Каждый Factory автоматически указывает на нужный `Program` через namespace.

---

## 6. Assertions (Shouldly)

### Базовые ассерты

```csharp
// Equality
value.ShouldBe(expected);
value.ShouldNotBe(unexpected);

// Null
value.ShouldBeNull();
value.ShouldNotBeNull();

// Boolean
condition.ShouldBeTrue();
condition.ShouldBeFalse();

// Strings
text.ShouldStartWith("prefix");
text.ShouldEndWith("suffix");
text.ShouldContain("substring");
text.ShouldBeNullOrEmpty();
text.ShouldNotBeNullOrWhiteSpace();

// Numbers
amount.ShouldBeGreaterThan(0);
amount.ShouldBeInRange(0, 100);
amount.ShouldBeNegative();

// Collections
items.ShouldBeEmpty();
items.ShouldNotBeEmpty();
items.ShouldHaveSingleItem();
items.Count.ShouldBe(3);
items.ShouldContain(item);
items.ShouldAllBe(item => item.IsValid);
items.ShouldBeInOrder();

// Types
result.ShouldBeOfType<SuccessResult>();
result.ShouldBeAssignableTo<IResult>();

// Exceptions
Should.Throw<ArgumentException>(() => Action());
await Should.ThrowAsync<HttpRequestException>(async () => await Action());

// Reference equality
actual.ShouldBeSameAs(expected);
actual.ShouldNotBeSameAs(other);
```

### Цепочки и сложные проверки

Для проверки нескольких свойств — отдельными ассертами:

```csharp
// ✅ Каждое свойство — отдельная строка
order.ShouldNotBeNull();
order.Symbol.ShouldBe("BTCUSDT");
order.Quantity.ShouldBe(1.5m);
order.Status.ShouldBe(OrderStatus.New);

// ❌ Не делаем так — теряем имя при ошибке
order.ShouldSatisfyAllConditions(
    () => order.Symbol.ShouldBe("BTCUSDT"),
    () => order.Quantity.ShouldBe(1.5m)
);
```

Исключение — `ShouldSatisfyAllConditions` оправдан когда **важно увидеть
все провалившиеся ассерты сразу**, а не остановиться на первом:

```csharp
// ✅ Оправдано — хотим видеть все проблемы маппинга разом
mapped.ShouldSatisfyAllConditions(
    () => mapped.Id.ShouldBe(source.Id),
    () => mapped.Name.ShouldBe(source.Name),
    () => mapped.Email.ShouldBe(source.Email),
    () => mapped.CreatedAt.ShouldBe(source.CreatedAt));
```

### Кастомные сообщения

Shouldly показывает имя переменной из исходника — кастомные сообщения
почти никогда не нужны. Используй только если переменная названа
непонятно или нужен context:

```csharp
// ✅ Хорошее имя — сообщение не нужно
spread.ShouldBe(5m);
// Output при failure: "spread should be 5 but was 4.5"

// ✅ Контекст помогает
spread.ShouldBe(5m, "BTC/USDT spread должен быть 5 после изменения price feed");
```

### `Should()` (legacy FluentAssertions API) запрещён

Не путаем Shouldly с FluentAssertions. Используем **`.ShouldBe(...)`**,
не **`.Should().Be(...)`**:

```csharp
// ✅ Shouldly
result.ShouldBe(42);

// ❌ FluentAssertions API
result.Should().Be(42);
```

---

## 7. Mocking (NSubstitute)

### Когда мокаем

Моки — для **внешних** зависимостей, которые невозможно или дорого
поднять реально:

- **Внешние HTTP API** (биржа, платёжный провайдер) в unit-тестах сервиса
  логики обработки ответа.
- **Time providers** (`IClock`, `TimeProvider`) — детерминированное
  тестирование time-dependent логики.
- **Message bus producers** — проверяем, что событие было опубликовано.

**Не мокаем**:
- DbContext / репозитории — для них integration с Testcontainers.
- `IOptions<T>` / `IConfiguration` — создаём руками.
- Логгеры (`ILogger<T>`) — `NullLogger<T>.Instance` или собственная
  test-implementation.

### Базовый синтаксис

```csharp
[Fact]
public async Task CallExchangeWithCorrectPayload()
{
    var exchangeClient = Substitute.For<IExchangeClient>();
    exchangeClient.PlaceOrderAsync(Arg.Any<OrderRequest>())
        .Returns(new OrderResponse { Id = "ORD-123", Status = "Placed" });

    var processor = new OrderProcessor(exchangeClient);

    var result = await processor.ProcessAsync(new Order("BTCUSDT", 1m));

    result.ExternalId.ShouldBe("ORD-123");
    await exchangeClient.Received(1).PlaceOrderAsync(
        Arg.Is<OrderRequest>(request => request.Symbol == "BTCUSDT"));
}
```

### Arg matchers

```csharp
// Любое значение
Arg.Any<Order>()
Arg.Any<string>()
Arg.Any<CancellationToken>()

// Конкретное значение
Arg.Is<int>(value => value > 0)
Arg.Is<Order>(order => order.Symbol == "BTCUSDT")

// Точное значение
Arg.Is(42)
```

### Returns vs Throws

```csharp
// Возврат
mock.GetAsync(Arg.Any<Guid>()).Returns(user);

// Возврат через callback (зависит от аргументов)
mock.GetAsync(Arg.Any<Guid>())
    .Returns(callInfo => new User { Id = callInfo.Arg<Guid>() });

// Throws
mock.GetAsync(Arg.Any<Guid>()).Throws(new InvalidOperationException());

// Async throws
mock.GetAsync(Arg.Any<Guid>())
    .ThrowsAsync(new HttpRequestException("503"));
```

### Verify взаимодействия

```csharp
// Был вызов с такими аргументами хотя бы раз
mock.Received().Method(Arg.Any<int>());

// Был вызов ровно N раз
mock.Received(3).Method(Arg.Any<int>());

// Не было вызова
mock.DidNotReceive().Method(Arg.Any<int>());

// Async
await mock.Received(1).MethodAsync(Arg.Any<int>());
```

### Что НЕ делаем с моками

```csharp
// ❌ Strict моки — overengineering
var mock = Substitute.For<IService>();
mock.Configure().Strict();  // NSubstitute даже не имеет этого

// ❌ Мок на класс, который сами написали и можем создать
var calculator = Substitute.For<ISpreadCalculator>();
calculator.Calculate(Arg.Any<Order[]>()).Returns(5m);
// ↑ Используй настоящий SpreadCalculator если он без I/O

// ❌ Несколько моков для проверки одной интеграции
var clientA = Substitute.For<IClientA>();
var clientB = Substitute.For<IClientB>();
var clientC = Substitute.For<IClientC>();
// ↑ Признак того, что нужен integration тест, не unit
```

---

## 8. Test data

### Object Mother / Builder для сложных объектов

Если объект имеет 5+ полей и используется в 3+ тестах — выноси
конструирование в Builder:

```csharp
public sealed class OrderBuilder
{
    private string symbol = "BTCUSDT";
    private decimal quantity = 1m;
    private decimal price = 100m;
    private OrderStatus status = OrderStatus.New;

    public OrderBuilder WithSymbol(string value)
    {
        symbol = value;
        return this;
    }

    public OrderBuilder WithQuantity(decimal value)
    {
        quantity = value;
        return this;
    }

    public OrderBuilder WithPrice(decimal value)
    {
        price = value;
        return this;
    }

    public OrderBuilder WithStatus(OrderStatus value)
    {
        status = value;
        return this;
    }

    public Order Build() => new()
    {
        Symbol = symbol,
        Quantity = quantity,
        Price = price,
        Status = status,
    };
}
```

Использование:

```csharp
var order = new OrderBuilder()
    .WithSymbol("ETHUSDT")
    .WithQuantity(10m)
    .Build();
```

Builders живут в **`Builders/`** папке тест-проекта, не в production-коде.

### Bogus для массовой генерации

Когда нужно много объектов с реалистичными значениями:

```csharp
var faker = new Faker<User>()
    .RuleFor(user => user.Id, faker => Guid.NewGuid())
    .RuleFor(user => user.Email, faker => faker.Internet.Email())
    .RuleFor(user => user.CreatedAt, faker => faker.Date.Past());

var users = faker.Generate(100);
```

`Bogus` хорош для seed-data в integration тестах и для load-сценариев.
Для unit тестов чаще достаточно Builder с явными значениями.

### Фиксированный seed для воспроизводимости

```csharp
Randomizer.Seed = new Random(42);

var faker = new Faker<Order>()
    .UseSeed(42)
    .RuleFor(order => order.Id, faker => Guid.NewGuid());
```

Без seed тесты с Bogus могут падать неустойчиво — каждый запуск даёт
разные данные.

---

## 9. Anti-patterns

### Тесты, которые тестируют моки

```csharp
// ❌ Это тест что NSubstitute работает, а не наш код
var repository = Substitute.For<IUserRepository>();
repository.GetByIdAsync(Arg.Any<Guid>()).Returns(new User { Id = id });

var result = await repository.GetByIdAsync(id);

result.Id.ShouldBe(id);  // Что мы проверили? Что мок возвращает то, что мы ему сказали возвращать.
```

### Один тест на много кейсов через if/switch

```csharp
// ❌ Сложный тест, непонятно что упало
[Fact]
public void HandleAllOrderTypes()
{
    foreach (var orderType in Enum.GetValues<OrderType>())
    {
        var result = processor.Process(orderType);

        if (orderType == OrderType.Market)
        {
            result.ShouldNotBeNull();
        }
        else if (orderType == OrderType.Limit)
        {
            result.Price.ShouldBeGreaterThan(0);
        }
        // ...
    }
}

// ✅ Theory с InlineData
[Theory]
[InlineData(OrderType.Market)]
[InlineData(OrderType.Limit)]
[InlineData(OrderType.Stop)]
public void ReturnNonNullResult(OrderType orderType)
{
    var result = processor.Process(orderType);

    result.ShouldNotBeNull();
}
```

### Магические числа без объяснения

```csharp
// ❌ Что за 0.0017?
spread.ShouldBe(0.0017m);

// ✅ Константа с именем
const decimal ExpectedBtcUsdtSpread = 0.0017m;
spread.ShouldBe(ExpectedBtcUsdtSpread);

// ✅ Или комментарий, если константа нужна только здесь
// 0.17% — типичный spread BTC/USDT в обычных рыночных условиях
spread.ShouldBe(0.0017m);
```

### Излишний setup / teardown

```csharp
// ❌ Глобальный setup, который дёргается перед каждым тестом
public sealed class CalculatorShould : IDisposable
{
    private readonly Calculator calculator;
    private readonly List<int> testData;
    private readonly Mock<ILogger> logger;

    public CalculatorTests()
    {
        calculator = new Calculator();
        testData = new List<int> { 1, 2, 3 };
        logger = new Mock<ILogger>();
    }

    [Fact]
    public void ReturnCorrectSum()
    {
        var result = calculator.Add(2, 3);  // testData и logger не нужны
        result.ShouldBe(5);
    }
}

// ✅ Создаём что нужно прямо в тесте
public sealed class CalculatorShould
{
    [Fact]
    public void ReturnCorrectSum()
    {
        var result = new Calculator().Add(2, 3);

        result.ShouldBe(5);
    }
}
```

### Тестирование private методов

```csharp
// ❌ Reflection до private — sign что архитектура неправильная
typeof(Calculator)
    .GetMethod("ComputeInternal", BindingFlags.NonPublic | BindingFlags.Instance)!
    .Invoke(calculator, [1, 2]);

// ✅ Либо тестируем через public, либо метод вынесен в отдельный класс
// который сам по себе public
```

### `Task.Delay` для ожидания async операций

```csharp
// ❌ Flaky
worker.Start();
await Task.Delay(1000);
worker.ProcessedCount.ShouldBeGreaterThan(0);

// ✅ Polling с timeout
worker.Start();
await WaitForAsync(() => worker.ProcessedCount > 0, TimeSpan.FromSeconds(5));
worker.ProcessedCount.ShouldBeGreaterThan(0);

// ✅ Или явный signal через TaskCompletionSource в самом коде
```

Утилита `WaitForAsync`:

```csharp
public static async Task WaitForAsync(
    Func<bool> condition,
    TimeSpan timeout,
    TimeSpan? pollInterval = null)
{
    var interval = pollInterval ?? TimeSpan.FromMilliseconds(50);
    var deadline = DateTime.UtcNow + timeout;

    while (DateTime.UtcNow < deadline)
    {
        if (condition())
        {
            return;
        }

        await Task.Delay(interval);
    }

    throw new TimeoutException($"Condition not met within {timeout}");
}
```

### Игнорирование/skipping тестов без TODO

```csharp
// ❌ Без объяснения когда вернёмся
[Fact(Skip = "Broken")]
public void Test() { ... }

// ✅ С контекстом и ссылкой
[Fact(Skip = "Flaky — gh-issue #1234, recheck after Q2 refactor")]
public void Test() { ... }
```

Skipped тесты в проекте дольше двух недель — либо чиним, либо удаляем.

---

## 10. CI integration

### Запуск в CI

```bash
# Прогон всех тестов
dotnet test --logger "console;verbosity=minimal" --logger "trx" --collect:"XPlat Code Coverage"

# Только integration (фильтр по имени проекта)
dotnet test --filter "FullyQualifiedName~Integration"

# Только unit
dotnet test --filter "FullyQualifiedName~Unit"
```

Через xUnit v3 + Microsoft.Testing.Platform работают также:

```bash
# Прямой запуск проекта (быстрее чем dotnet test)
dotnet run --project tests/Acme.Shop.Api.Public.Integration.Health
```

### `<Company>.<App>.Testing` проект — собираем, не запускаем

Shared infrastructure (см. раздел 5) — это проект-библиотека, не тест-проект.
В нём нет `[Fact]` методов, его CI **собирает** (`dotnet build`), но
**не запускает** через `dotnet test`.

Защита от случайного запуска: `IsTestProject` и `UseMicrosoftTestingPlatformRunner`
**не** выставлены в csproj `.Testing` проекта. `dotnet test` его пропустит.

Если кто-то по ошибке добавит `[Fact]` в `.Testing` проект и пометит его
как тестовый — линтер NetArchTest должен это поймать:

```csharp
[Fact]
public void NotContainTests()
{
    var result = Types.InAssembly(typeof(IIntegrationFactory).Assembly)
        .That()
        .HaveCustomAttribute(typeof(FactAttribute))
        .Or()
        .HaveCustomAttribute(typeof(TheoryAttribute))
        .Should()
        .NotExist()
        .GetResult();

    result.IsSuccessful.ShouldBeTrue();
}
```

### Coverage thresholds

В `Directory.Build.props` тест-проектов:

```xml
<PropertyGroup>
  <CollectCoverage>true</CollectCoverage>
  <CoverletOutputFormat>cobertura,opencover</CoverletOutputFormat>
  <Threshold>70</Threshold>
  <ThresholdType>line</ThresholdType>
  <ThresholdStat>total</ThresholdStat>
</PropertyGroup>
```

**70% line coverage** — разумный минимум. Не 80%+ — это ведёт к
бессмысленным тестам ради метрики.

Что **исключаем** из coverage:

```xml
<PropertyGroup>
  <ExcludeByFile>
    **/Program.cs,
    **/*.Designer.cs,
    **/Migrations/**/*.cs,
    **/Generated/**/*.cs
  </ExcludeByFile>
  <Exclude>[*.Tests]*,[*.Benchmarks]*</Exclude>
</PropertyGroup>
```

### Параллельные jobs в CI

Integration тесты с Testcontainers требуют Docker. На CI это либо
docker-in-docker, либо Linux runner с доступом к docker socket.

Если CI-runner один — запускаем последовательно (`--no-parallel`).
Если runner-ов много — разбиваем по test-проектам:

```yaml
# GitHub Actions пример
strategy:
  matrix:
    test-project:
      - Acme.Shop.Api.Public.Integration.Health
      - Acme.Shop.Api.Public.Integration.Orders
      - Fabrikam.Trading.Pattern.Arbitrage.Integration.Execution

steps:
  - run: dotnet test tests/${{ matrix.test-project }}
```

### Локальный pre-commit

Запускаем только unit-тесты (быстро):

```bash
# scripts/test-unit.sh
dotnet test --filter "FullyQualifiedName~Unit" --logger "console;verbosity=minimal"
```

Integration оставляем на CI — на локали часто Docker не поднят, или
поднят с другой версией Postgres.

---

## Quick reference

| Что | Где |
|-----|-----|
| Структура папок и нейминг тест-проектов | `PROJECT-STRUCTURE.md §10` |
| Test framework | xUnit v3 |
| Assertions | Shouldly (`.ShouldBe(...)`, не `.Should().Be(...)`) |
| Mocks | NSubstitute (только для внешних, не для своего кода) |
| Integration DB | Testcontainers + Respawn (не EF InMemory, не SQLite) |
| Integration API | `WebApplicationFactory<Program>` |
| Test data | Builder для сложных, Bogus для массовой генерации |
| Coverage | 70% line (исключая Migrations, Program.cs, Generated) |

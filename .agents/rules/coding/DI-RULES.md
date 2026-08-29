---
description: DI, IOptions pattern, configuration validation, composition root, service lifetimes — Installer pattern, ValidateOnStart, captive dependency, IServiceScopeFactory
always: true
---

# Dependency Injection & Configuration Rules

Правила организации DI-регистраций, конфигурации и composition root
для C# / .NET проектов. Использует только нативные `Microsoft.Extensions.*`
без кастомных абстракций.

Структура проектов и где живут Installer-ы — в **`PROJECT-STRUCTURE.md`**.
Стиль кода — в **`CODING-RULES.md`**.

---

## Table of Contents

1. [Stack & принципы](#1-stack--принципы)
2. [Installer pattern](#2-installer-pattern)
3. [IOptions pattern](#3-ioptions-pattern)
4. [Validation](#4-validation)
5. [IOptions vs Snapshot vs Monitor](#5-ioptions-vs-snapshot-vs-monitor)
6. [Composition root](#6-composition-root)
7. [Service lifetimes](#7-service-lifetimes)
8. [Anti-patterns](#8-anti-patterns)

---

## 1. Stack & принципы

| Назначение | Что используем |
|------------|----------------|
| DI container | `Microsoft.Extensions.DependencyInjection` (нативный MSDI) |
| Configuration | `Microsoft.Extensions.Configuration` + `IOptions<T>` |
| Hosting | `Microsoft.Extensions.Hosting` + `WebApplicationBuilder` |
| Options validation | `DataAnnotations` + `.ValidateOnStart()` |

### Принципы

1. **Только native Microsoft.** Никаких кастомных абстракций над MSDI
   (собственных Builder/Module/Registrar интерфейсов). Они изобретают
   терминологию, которую не понимают агенты и новые разработчики, и
   дублируют то, что уже даёт `IServiceCollection`.
2. **Никакой рефлексии для авторегистрации.** Не сканируем сборки на типы,
   реализующие маркер-интерфейсы. Регистрации явные, видны в коде.
3. **Composition root явный.** В `Program.cs` каждого entry-point видна
   полная цепочка `.Add*().Add*()...` — без скрытых auto-discovery.
4. **Конфигурация через `IOptions<T>`.** Никакого `IConfiguration` прямо
   в сервисах. Никаких `configuration["MySection:MyKey"]` по строкам.
5. **Валидация при старте.** Битая конфигурация падает на `host.RunAsync()`,
   не в первом запросе через час после деплоя.

### Чего НЕ используем

| Анти-паттерн | Чем заменяем |
|--------------|--------------|
| Кастомные wrapper-интерфейсы над `IServiceCollection` | `Installer` extension class (см. §2) |
| `Assembly.GetTypes().Where(...)` для авторегистрации | Явные вызовы `Add<Module>Core` |
| `[SectionOverride("Path:To:Section")]` атрибут | `builder.Configuration.GetSection("Path:To:Section").Bind(options)` |
| `GetOptionsExternal<T>(IConfiguration)` хелпер | `configuration.GetSection("...").Get<T>()` (одна строка) |
| `ServiceProvider.BuildServiceProvider()` до Build приложения | Чтение конфига напрямую через `IConfiguration` |
| Autofac / Lamar / Castle.Windsor / прочие сторонние контейнеры | Нативный MSDI — функциональности хватает |

---

## 2. Installer pattern

### Что такое Installer

**Installer** = `static class` с двумя extension-методами,
**инкапсулирующий регистрацию одного модуля**.

```csharp
namespace Acme.Shop.Order.Installers;

public static class OrderFeatureInstaller
{
    /// <summary>
    /// Регистрирует сервисы Order-feature в DI.
    /// </summary>
    public static IServiceCollection AddOrderFeatureCore(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<OrderOptions>()
            .Bind(configuration.GetSection(OrderOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddScoped<IOrderService, OrderService>();
        services.AddSingleton<TimeProvider>(TimeProvider.System);

        return services;
    }

    /// <summary>
    /// Подключает middleware/endpoints Order-feature, если есть.
    /// </summary>
    public static WebApplication UseOrderFeatureCore(this WebApplication application)
    {
        // Middleware / endpoints, специфичные для feature
        return application;
    }
}
```

### Структура

- **Папка**: `Installers/` в корне проекта, рядом с `Interfaces/`,
  `Services/`, `Extensions/`.
- **Имя класса**: `<Module>Installer` (не `<Module>Extensions`,
  не `<Module>Registrar`).
- **Имя метода Add**: `Add<Module>Core` (см. ниже почему `Core`).
- **Имя метода Use**: `Use<Module>Core` (если нужен).

### Почему суффикс `Core`

`Core` отличает «основную» регистрацию модуля от опциональных
дополнений:

```csharp
services
    .AddOrderFeatureCore(configuration)            // обязательно
    .AddOrderFeatureMetrics(configuration)         // опциональный модуль метрик
    .AddOrderFeatureBackgroundJobs(configuration); // опциональные фоновые задачи
```

Если у модуля только одна точка входа — `Core` всё равно ставится,
для консистентности.

### Когда какой метод нужен

| Что регистрируется | `Add<Module>Core` | `Use<Module>Core` |
|--------------------|-------------------|-------------------|
| Только сервисы и options | ✅ | ❌ не нужен |
| Сервисы + middleware/endpoints | ✅ | ✅ |
| Только middleware (без своих сервисов) | ❌ не нужен | ✅ |
| Hosted services | ✅ | ❌ не нужен |

Не пиши пустой `Use<Module>Core`, который ничего не делает — это шум.

### Один модуль — один Installer

Каждый **проект** в `feature/`, `database/`, `client/`, `shared/`
экспонирует **один** Installer.

```csharp
// ✅ Один проект, один Installer
src/feature/Acme.Shop.Order/Installers/OrderFeatureInstaller.cs
src/database/Acme.Shop.Database.Orders/Installers/OrdersDatabaseInstaller.cs
src/shared/Acme.Shop.Logging/Installers/LoggingInstaller.cs

// ❌ Не дроби на подмодули внутри одного проекта
src/feature/Acme.Shop.Order/Installers/OrderServicesInstaller.cs
src/feature/Acme.Shop.Order/Installers/OrderHandlersInstaller.cs
src/feature/Acme.Shop.Order/Installers/OrderMappingInstaller.cs
```

Если внутри проекта **очень** много регистраций, разделяй на private
extension methods **внутри одного Installer**:

```csharp
public static class OrderFeatureInstaller
{
    public static IServiceCollection AddOrderFeatureCore(
        this IServiceCollection services,
        IConfiguration configuration)
        => services
            .AddOrderFeatureOptions(configuration)
            .AddOrderFeatureServices()
            .AddOrderFeatureHandlers();

    private static IServiceCollection AddOrderFeatureOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<OrderOptions>()
            .Bind(configuration.GetSection(OrderOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        return services;
    }

    private static IServiceCollection AddOrderFeatureServices(this IServiceCollection services)
    {
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<IOrderValidator, OrderValidator>();
        return services;
    }

    private static IServiceCollection AddOrderFeatureHandlers(this IServiceCollection services)
    {
        services.AddScoped<ICreateOrderHandler, CreateOrderHandler>();
        services.AddScoped<ICancelOrderHandler, CancelOrderHandler>();
        return services;
    }
}
```

### Использование в Program.cs

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddSharedInfrastructureCore(builder.Configuration)
    .AddOrderFeatureCore(builder.Configuration)
    .AddOrdersDatabaseCore(builder.Configuration);

var application = builder.Build();

application.UseSharedInfrastructureCore();
application.UseOrderFeatureCore();

application.MapControllers();

await application.RunAsync();
```

**Порядок в Program.cs виден глазами.** Нет авторегистрации через
рефлексию — есть явный список того, что подключено.

---

## 3. IOptions pattern

### Каждый Options-класс — отдельный

Один Options-класс на одну логическую секцию конфигурации.
Не складывай всё в один `AppOptions` с десятком вложенных классов.

```csharp
// ✅ Один Options — одна секция
public sealed class OrderOptions
{
    /// <summary>
    /// Путь к секции в IConfiguration.
    /// </summary>
    public const string SectionName = "Features:Order";

    /// <summary>
    /// Максимальное количество заказов на пользователя в час.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int MaxOrdersPerUserPerHour { get; init; } = 100;

    /// <summary>
    /// Минимальная сумма заказа.
    /// </summary>
    [Range(typeof(decimal), "0.01", "1000000")]
    public decimal MinimumOrderAmount { get; init; } = 0.01m;
}
```

### `SectionName` как const внутри класса

Путь к секции — **константа `SectionName`** прямо в Options-классе.
Не магические строки в Installer, не отдельный constants-файл.

```csharp
// ✅ Single source of truth
public sealed class OrderOptions
{
    public const string SectionName = "Features:Order";
    // ...
}

// Installer использует:
services.AddOptions<OrderOptions>()
    .Bind(configuration.GetSection(OrderOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// ❌ Магическая строка в Installer
services.AddOptions<OrderOptions>()
    .Bind(configuration.GetSection("Features:Order"))  // дублируется по проекту
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

### Свойства Options — `init` и `required`

Options — иммутабельные. После старта приложения значения не меняются
(если только не используется `IOptionsMonitor`, см. §5).

```csharp
public sealed class KafkaOptions
{
    public const string SectionName = "Brokers:Kafka";

    /// <summary>
    /// Список bootstrap-серверов через запятую.
    /// </summary>
    [Required]
    public required string BootstrapServers { get; init; }

    /// <summary>
    /// Префикс consumer group для этого приложения.
    /// </summary>
    [Required]
    public required string ConsumerGroupPrefix { get; init; }

    /// <summary>
    /// Размер батча при чтении.
    /// </summary>
    [Range(1, 10_000)]
    public int BatchSize { get; init; } = 100;
}
```

`required` — для значений, у которых нет разумного default. EF Core,
JSON binding и `IConfiguration` это понимают.

### `sealed class`, не record

Options-классы — `sealed class` (см. **`CODING-RULES.md §4`**). Record не
нужен — value equality для Options не имеет смысла, и binding из
`IConfiguration` работает с обоими, но class более явный для EF/JSON.

### Условная регистрация на основе конфига

Иногда **что регистрировать** зависит от значения в конфиге. Примеры:

- Multi-provider: Kafka / RabbitMQ / InMemory message bus.
- Окружения: `MockEmailSender` в dev, `SendGridEmailSender` в prod.
- Feature flags на уровне DI: если `Features:NewPricing:Enabled = true`,
  регистрируем `NewPricingService` вместо старого.
- Регистрация по списку: `EnabledExchanges: ["Binance", "OKX"]` —
  регистрируется клиент для каждого имени.
- Условный hosted service: запускать `WorkerXyz` только если включён.

В `Installer` это **разрешено** через прямое чтение `IConfiguration`.
Это не противоречит правилу «никакого `IConfiguration` в сервисах» —
сервисы остаются чистыми, `IConfiguration` живёт только на этапе
регистрации.

#### Почему `IConfiguration` доступен, а `ServiceProvider` ещё нет

`IConfiguration` готов сразу после `WebApplication.CreateBuilder(args)` —
это словарь из `appsettings.json` + env-vars + secrets, строится без DI.
`ServiceProvider` собирается только при `builder.Build()`, после всех
регистраций. Поэтому читать конфиг в Installer можно, а резолвить
сервисы — нет.

#### Пример: multi-provider registration

```csharp
public sealed class MessagingOptions
{
    public const string SectionName = "Messaging";

    [Required]
    public required MessagingProvider Provider { get; init; }
}

public enum MessagingProvider { InMemory, Kafka, RabbitMq }

public static IServiceCollection AddMessagingCore(
    this IServiceCollection services,
    IConfiguration configuration)
{
    var messagingOptions = configuration
        .GetSection(MessagingOptions.SectionName)
        .Get<MessagingOptions>()
        ?? throw new InvalidOperationException(
            $"Section '{MessagingOptions.SectionName}' is missing");

    services.AddOptions<MessagingOptions>()
        .Bind(configuration.GetSection(MessagingOptions.SectionName))
        .ValidateDataAnnotations()
        .ValidateOnStart();

    switch (messagingOptions.Provider)
    {
        case MessagingProvider.Kafka:
            services.AddSingleton<IMessageBus, KafkaMessageBus>();
            services.AddHostedService<KafkaConsumerHost>();
            break;
        case MessagingProvider.RabbitMq:
            services.AddSingleton<IMessageBus, RabbitMqMessageBus>();
            services.AddHostedService<RabbitMqConsumerHost>();
            break;
        case MessagingProvider.InMemory:
            services.AddSingleton<IMessageBus, InMemoryMessageBus>();
            break;
        default:
            throw new InvalidOperationException(
                $"Unknown messaging provider: {messagingOptions.Provider}");
    }

    return services;
}
```

Структура любого Installer с условной регистрацией одна и та же:

1. **Прочитать** конфиг через `GetSection(SectionName).Get<T>()`.
2. **Зарегистрировать** Options для runtime-использования.
3. **Решить** что регистрировать на основе прочитанных значений.

Дальше — только различия в третьем шаге.

#### Пример: регистрация по списку из конфига

```json
{ "Exchanges": { "Enabled": ["Binance", "OKX"] } }
```

```csharp
public sealed class ExchangesOptions
{
    public const string SectionName = "Exchanges";

    [Required]
    [MinLength(1)]
    public required ExchangeName[] Enabled { get; init; }
}

public enum ExchangeName { Binance, OKX, Bybit }

public static IServiceCollection AddExchangeClientsCore(
    this IServiceCollection services,
    IConfiguration configuration)
{
    var exchangesOptions = configuration
        .GetSection(ExchangesOptions.SectionName)
        .Get<ExchangesOptions>()
        ?? throw new InvalidOperationException(
            $"Section '{ExchangesOptions.SectionName}' is missing");

    services.AddOptions<ExchangesOptions>()
        .Bind(configuration.GetSection(ExchangesOptions.SectionName))
        .ValidateDataAnnotations()
        .ValidateOnStart();

    foreach (var exchange in exchangesOptions.Enabled)
    {
        switch (exchange)
        {
            case ExchangeName.Binance: services.AddHttpClient<IBinanceClient, BinanceClient>(); break;
            case ExchangeName.OKX:     services.AddHttpClient<IOkxClient, OkxClient>(); break;
            case ExchangeName.Bybit:   services.AddHttpClient<IBybitClient, BybitClient>(); break;
            default: throw new InvalidOperationException($"Unknown exchange: {exchange}");
        }
    }

    return services;
}
```

`Microsoft.Extensions.Configuration.Binder` сам парсит строки в enum.
Неизвестное значение упадёт на `Get<ExchangesOptions>()`, до регистраций.

#### Пример: feature flag на уровне DI

```csharp
public sealed class PricingOptions
{
    public const string SectionName = "Pricing";

    public bool UseNewAlgorithm { get; init; }
}

public static IServiceCollection AddPricingCore(
    this IServiceCollection services,
    IConfiguration configuration)
{
    var pricingOptions = configuration
        .GetSection(PricingOptions.SectionName)
        .Get<PricingOptions>()
        ?? new PricingOptions();

    services.AddOptions<PricingOptions>()
        .Bind(configuration.GetSection(PricingOptions.SectionName))
        .ValidateOnStart();

    if (pricingOptions.UseNewAlgorithm)
    {
        services.AddScoped<IPricingService, NewPricingService>();
    }
    else
    {
        services.AddScoped<IPricingService, LegacyPricingService>();
    }

    return services;
}
```

Секция здесь опциональна (отсутствие = legacy), поэтому `?? new PricingOptions()`,
а не `?? throw` как в предыдущих примерах с обязательными секциями.

#### Чего не делаем — собираем ServiceProvider ради чтения Options

```csharp
// ❌ Wrong — строим временный ServiceProvider
using var tempProvider = services.BuildServiceProvider();
var messagingOptions = tempProvider
    .GetRequiredService<IOptions<MessagingOptions>>().Value;
```

Что не так:

- Создаёт ServiceProvider, который выбрасывается — лишние аллокации и dispose chain.
- Singleton с side-effects (открывает соединение, стартует таймер) создастся дважды.
- Запускает `IValidateOptions<T>` на полу-готовом state.
- Если в DI есть unresolved dependencies — `Build()` упадёт загадочно.

Правильный путь — `configuration.GetSection(...).Get<T>()` одной строкой.

### Никакого `IConfiguration` в сервисах

В **сервисах** (то, что выполняется в runtime) — только `IOptions<T>`,
никаких `IConfiguration`:

```csharp
// ❌ Wrong — сервис знает про IConfiguration напрямую
public sealed class OrderService(IConfiguration configuration)
{
    public Task DoAsync()
    {
        var maxOrders = configuration["Features:Order:MaxOrdersPerUserPerHour"];
        // ...
    }
}

// ✅ Correct — сервис принимает типизированные Options
public sealed class OrderService(IOptions<OrderOptions> orderOptions)
{
    public Task DoAsync()
    {
        var maxOrders = orderOptions.Value.MaxOrdersPerUserPerHour;
        // ...
    }
}
```

`IConfiguration` остаётся **только** в `Program.cs` и в Installer-ах —
для условной регистрации (см. выше). В рабочей логике сервисов — типизированные
Options.

---

## 4. Validation

### `.ValidateOnStart()` обязательно

Каждый `AddOptions<T>()` обязан заканчиваться `.ValidateOnStart()`:

```csharp
services.AddOptions<OrderOptions>()
    .Bind(configuration.GetSection(OrderOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();           // ← обязательно
```

Без `.ValidateOnStart()` валидация выполняется лениво — при первом
обращении к `IOptions<T>.Value`. Это значит, что битый `appsettings.json`
в проде упадёт не при старте, а через час в первом HTTP-запросе.

С `.ValidateOnStart()` приложение **не стартует** при битой конфигурации —
deployment-pipeline ловит проблему раньше клиента.

### Что валидируем

1. **`[Required]`** для всех обязательных полей.
2. **`[Range]`** для числовых ограничений.
3. **`[StringLength]`** или `[MaxLength]` для строк с ограничением.
4. **`[Url]`**, **`[EmailAddress]`** где применимо.
5. **Custom validators** через `.Validate(options => ..., "message")` для
   сложных проверок (cross-field validation, regex и т.п.).

```csharp
public sealed class KafkaOptions
{
    public const string SectionName = "Brokers:Kafka";

    [Required]
    [MinLength(1)]
    public required string BootstrapServers { get; init; }

    [Required]
    [RegularExpression(@"^[a-z0-9-]+$",
        ErrorMessage = "Consumer group prefix must contain only lowercase letters, digits and dashes")]
    public required string ConsumerGroupPrefix { get; init; }

    [Range(1, 10_000)]
    public int BatchSize { get; init; } = 100;

    [Range(typeof(TimeSpan), "00:00:01", "00:10:00")]
    public TimeSpan PollTimeout { get; init; } = TimeSpan.FromSeconds(30);
}
```

### Cross-field validation

Когда правило не сводится к атрибуту на одном поле:

```csharp
services.AddOptions<RetryOptions>()
    .Bind(configuration.GetSection(RetryOptions.SectionName))
    .ValidateDataAnnotations()
    .Validate(options => options.MaxAttempts > 0,
        "MaxAttempts must be positive")
    .Validate(options => options.InitialDelay <= options.MaxDelay,
        "InitialDelay must not exceed MaxDelay")
    .ValidateOnStart();
```

Для крупных классов с большим количеством правил — выноси в `IValidateOptions<T>`:

```csharp
public sealed class KafkaOptionsValidator : IValidateOptions<KafkaOptions>
{
    public ValidateOptionsResult Validate(string? name, KafkaOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.BootstrapServers))
        {
            failures.Add("BootstrapServers is required");
        }

        if (options.BatchSize > 1_000 && options.PollTimeout < TimeSpan.FromSeconds(10))
        {
            failures.Add("Large BatchSize requires PollTimeout >= 10s");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}

// В Installer
services.AddOptions<KafkaOptions>()
    .Bind(configuration.GetSection(KafkaOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

services.AddSingleton<IValidateOptions<KafkaOptions>, KafkaOptionsValidator>();
```

---

## 5. IOptions vs Snapshot vs Monitor

### Default — `IOptions<T>`

В **контейнеризованных приложениях с env-variables / mounted ConfigMaps** —
всегда `IOptions<T>`:

```csharp
public sealed class OrderService(IOptions<OrderOptions> orderOptions)
{
    public Task DoAsync()
    {
        var maxOrders = orderOptions.Value.MaxOrdersPerUserPerHour;
        // ...
    }
}
```

`IOptions<T>` — **singleton**, значение фиксируется при старте приложения,
не меняется в runtime. Это правильно для:

- Контейнеров (новая конфигурация = новый deploy).
- `appsettings.json` в build artifact.
- Env-variables.

### `IOptionsMonitor<T>` — только при remote config provider

Используй **только когда** в стэке есть:

- **Consul KV** (через `Steeltoe.Extensions.Configuration.ConfigServerCore`
  или `Winton.Extensions.Configuration.Consul`).
- **Azure App Configuration** (через `Microsoft.Azure.AppConfiguration.AspNetCore`).
- **AWS AppConfig** / **HashiCorp Vault** с динамической перезагрузкой.
- `reloadOnChange: true` в `appsettings.json` файле, который **реально**
  меняется в runtime (редко в проде).

```csharp
public sealed class FeatureFlagService(IOptionsMonitor<FeatureFlagsOptions> featureFlags)
{
    public bool IsEnabled(string feature)
        => featureFlags.CurrentValue.Flags.GetValueOrDefault(feature);

    // Подписка на изменения
    public IDisposable OnChange(Action<FeatureFlagsOptions> listener)
        => featureFlags.OnChange(listener);
}
```

`IOptionsMonitor<T>` — **singleton**, но `CurrentValue` всегда актуальное.
Поддерживает callback `OnChange()` для реакции на обновления.

### `IOptionsSnapshot<T>` — почти не используем

`IOptionsSnapshot<T>` — **scoped**, читает значение один раз на scope
(обычно один HTTP-запрос). Использовать **только** в редком сценарии:

- Конфигурация может меняться между запросами (есть reload provider).
- В рамках одного запроса нужна консистентность значения.
- И при этом нужна привязка к scope, не singleton.

В реальности это очень редко нужно. Чаще всего `IOptions<T>` или
`IOptionsMonitor<T>` покрывают всё.

### Решение в правилах

```
Какой интерфейс использовать?

├── В стэке есть remote config provider (Consul/Azure/AWS/...)?
│   │
│   ├── Нужна реакция на изменения (callback на смену значения)?
│   │   └── IOptionsMonitor<T> + OnChange()
│   │
│   └── Нужно просто читать всегда актуальное значение?
│       └── IOptionsMonitor<T> + CurrentValue
│
└── Контейнер + env / appsettings.json без reload?
    └── IOptions<T>  (default)
```

В сомнении — `IOptions<T>`. Переход на `IOptionsMonitor` тривиальный
(меняется только тип параметра в конструкторе и `.Value` → `.CurrentValue`).

### Запрет смешивания

В рамках **одного приложения** для одного и того же Options-класса
используется **один** интерфейс. Не один сервис берёт `IOptions<KafkaOptions>`,
а другой — `IOptionsMonitor<KafkaOptions>`.

Если внутри приложения часть сервисов должна реагировать на изменения
конфигурации, а часть — нет, для всех используется `IOptionsMonitor<T>` —
проще и явнее (не реагирующие сервисы просто читают `.CurrentValue` один
раз в конструкторе).

---

## 6. Composition root

### Два варианта организации

**A. Per-module Installer + entry-point собирает в Program.cs**

Каждый проект (`feature/`, `database/`, `client/`, `shared/`) экспонирует
свой Installer. `Program.cs` каждого entry-point вызывает то, что ему нужно:

```csharp
// Acme.Shop.Api.Public/Program.cs
var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddLoggingCore(builder.Configuration)
    .AddTelemetryCore(builder.Configuration)
    .AddOrderFeatureCore(builder.Configuration)
    .AddOrdersDatabaseCore(builder.Configuration);

var application = builder.Build();
application.UseLoggingCore();
application.UseTelemetryCore();
await application.RunAsync();
```

**Когда выбирать**: solution с несколькими entry-points (API, workers, bots),
у каждого свой набор зависимостей.

**Плюсы**: entry-point тащит только то что нужно. Минимум транзитивных
зависимостей. Видно глазами в Program.cs что подключено.

**Минусы**: cross-cutting setup (логирование, telemetry) дублируется в
каждом `Program.cs`.

---

**B. Application-level Composition проект**

Отдельный проект `*.Composition` собирает «полный набор» для типичного
приложения через один общий Installer:

```csharp
// shared/Acme.Shop.Composition/Installers/SharedInfrastructureInstaller.cs
public static class SharedInfrastructureInstaller
{
    public static IServiceCollection AddSharedInfrastructureCore(
        this IServiceCollection services,
        IConfiguration configuration)
        => services
            .AddLoggingCore(configuration)
            .AddTelemetryCore(configuration)
            .AddHealthChecksCore(configuration);

    public static WebApplication UseSharedInfrastructureCore(this WebApplication application)
        => application
            .UseLoggingCore()
            .UseTelemetryCore()
            .UseHealthChecksCore();
}
```

И в `Program.cs`:

```csharp
builder.Services
    .AddSharedInfrastructureCore(builder.Configuration)   // одной строкой
    .AddOrderFeatureCore(builder.Configuration)
    .AddOrdersDatabaseCore(builder.Configuration);
```

**Когда выбирать**: всегда для cross-cutting. Дополнительно — когда есть
группа feature-проектов, которые обычно подключаются вместе.

### Default: гибрид A + B

- **`shared/<Company>.<App>.Composition`** — единственный Composition-проект.
  Содержит **только cross-cutting** Installer-ы:
  - Логирование (Serilog config, enrichers).
  - OpenTelemetry (traces, metrics, exporters).
  - Healthchecks.
  - Общие middleware (exception handling, correlation ID, request logging).

- **Каждый `feature/`, `database/`, `client/` проект** — свой Installer
  в собственной папке `Installers/`. Не зависит от Composition.

- **`Program.cs` каждого entry-point** — явная цепочка вызовов: сначала
  cross-cutting из Composition, потом feature/database/client по необходимости.

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddSharedInfrastructureCore(builder.Configuration)   // ← cross-cutting (shared/Composition)
    .AddOrderFeatureCore(builder.Configuration)           // ← feature/Order
    .AddOrdersDatabaseCore(builder.Configuration)         // ← database/Orders
    .AddPublicApiClientCore(builder.Configuration);       // ← client/Public

var application = builder.Build();

application.UseSharedInfrastructureCore();                        // ← cross-cutting middleware

application.MapControllers();

await application.RunAsync();
```

### Per-application `*.Dependency.*` (опционально)

Если у приложения **очень** специфичный набор зависимостей, и оно
сильно отличается от других entry-points — можно завести отдельный
`*.Dependency.<App>` проект:

```
src/
├── shared/Acme.Shop.Composition/             # cross-cutting для всех
├── application/
│   └── api/Acme.Shop.Api.Public/
│       └── Program.cs
└── dependency/
    └── Acme.Shop.Dependency.Public/          # специфика именно для Public API
        └── Installers/
            └── PublicApiDependencyInstaller.cs
```

`PublicApiDependencyInstaller` собирает специфичный для Public API
набор:

```csharp
public static class PublicApiDependencyInstaller
{
    public static IServiceCollection AddPublicApiDependenciesCore(
        this IServiceCollection services,
        IConfiguration configuration)
        => services
            .AddSharedInfrastructureCore(configuration)
            .AddOrderFeatureCore(configuration)
            .AddOrdersDatabaseCore(configuration)
            .AddAuthenticationCore(configuration);
}
```

И тогда `Program.cs` становится одной строкой:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPublicApiDependenciesCore(builder.Configuration);

var application = builder.Build();
application.UsePublicApiDependenciesCore();
await application.RunAsync();
```

**Когда заводить `Dependency.<App>`**:

- Setup приложения занимает > 30 строк в `Program.cs`.
- Логика регистрации требует условий, которые не помещаются в один Installer.
- Этот set-up предполагается переиспользовать (например, в integration
  тестах).

**Когда НЕ заводить**:

- Маленький `Program.cs` (< 20 строк) — оставь как есть.
- Уникальные регистрации для одного entry-point — не переиспользуются.

### Layer placement

`*.Composition` и `*.Dependency.*` — это **shared infrastructure**,
живёт в `shared/` слое (см. `PROJECT-STRUCTURE.md §4`):

```
src/shared/
├── Acme.Shop.Composition/                # cross-cutting, обязательно
└── (опционально) Acme.Shop.Dependency.Public/
└── (опционально) Acme.Shop.Dependency.Worker/
```

Эти проекты могут ссылаться на любые `feature/`, `database/`, `client/`,
`shared/` проекты. Обратное запрещено — никакой feature не ссылается
на `Composition`.

---

## 7. Service lifetimes

В MSDI три lifetime: **Singleton**, **Scoped**, **Transient**. Выбор
определяет:

- **Количество аллокаций** (один на приложение / один на scope / один на каждое использование).
- **Требования к thread safety** (singleton обязан, scoped — нет).
- **Время жизни `IDisposable` ресурсов** (когда они освобождаются).
- **Корректность работы с per-request state** (kто что видит).

Неправильный выбор lifetime — одна из самых дорогих ошибок: ловится не
сразу, проявляется как race conditions, утечки памяти или stale data.

### Что значит каждый lifetime в runtime

| Lifetime | Сколько экземпляров | Когда создаётся | Когда диспозится |
|----------|---------------------|-----------------|------------------|
| Singleton | **1** на всё приложение | При первом резолве (lazy) или при `BuildServiceProvider()` | При shutdown приложения |
| Scoped | **1** на DI scope | При первом резолве в scope | При `Dispose()` scope (обычно конец HTTP-запроса) |
| Transient | **N** — новый на каждый inject | При каждом резолве | См. ниже про disposable |

### Когда выбирать Singleton

**Default для большинства сервисов.** Если у тебя нет конкретной
причины делать иначе — singleton.

#### Используй Singleton для

1. **Stateless сервисов** — нет mutable полей, поведение определяется
   только аргументами методов.

   ```csharp
   services.AddSingleton<ISpreadCalculator, SpreadCalculator>();
   services.AddSingleton<IOrderValidator, OrderValidator>();
   services.AddSingleton<IPriceFormatter, PriceFormatter>();
   ```

2. **Concurrent-safe state** — используешь `ConcurrentDictionary`,
   `ImmutableList`, `Channel<T>`, `Interlocked`. Mutable state можно,
   если он thread-safe.

   ```csharp
   public sealed class ExchangeRateCache : IExchangeRateCache
   {
       private readonly ConcurrentDictionary<string, decimal> rates = new();
       // ...
   }
   services.AddSingleton<IExchangeRateCache, ExchangeRateCache>();
   ```

3. **Конфигурация и Options** — `IOptions<T>` всегда singleton.

   ```csharp
   services.AddSingleton(TimeProvider.System);
   services.AddSingleton<IConnectionMultiplexer>(/* Redis */);
   ```

4. **Дорогая инициализация** — то что компилируется/прогревается
   один раз: precompiled regex, expression trees, кеш типов через
   рефлексию, JSON-сериализаторы с кастомными settings.

   ```csharp
   services.AddSingleton<JsonSerializerOptions>(_ => new JsonSerializerOptions
   {
       PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
       Converters = { new JsonStringEnumConverter() },
   });

   services.AddSingleton<IRegexValidator>(_ => new RegexValidator(
       compiledPattern: new Regex(@"^[A-Z]{3,5}USDT$", RegexOptions.Compiled)));
   ```

5. **Factory-объекты** — `IHttpClientFactory`, `ILoggerFactory`,
   `IDbContextFactory<T>`. Они сами создают per-call объекты.

#### Требования к Singleton

- **Thread safety обязательна.** Singleton используется конкурентно
  из множества потоков (HTTP-запросы, hosted services).
  Если есть mutable state — он должен быть thread-safe.
- **Не зависит от Scoped или Transient** (см. Captive dependency ниже).
- **Может зависеть от других Singletons.**

#### НЕ используй Singleton для

- Сервисов с per-request state (текущий пользователь, correlation ID).
- Сервисов, использующих `DbContext` напрямую.
- Сервисов, держащих mutable state без синхронизации.

### Когда выбирать Scoped

Scoped используется реже singleton, но всегда явно — там, где scope
имеет смысл.

#### Используй Scoped для

1. **`DbContext`** — это правило EF Core, не выбор. `DbContext` не
   thread-safe, держит change tracker per-request, обязан быть scoped.

   ```csharp
   services.AddDbContext<OrdersDbContext>(options => /* ... */);
   // AddDbContext регистрирует scoped по умолчанию
   ```

2. **Сервисы, использующие `DbContext`** — repositories, query services,
   handlers. Они наследуют lifetime от своих зависимостей.

   ```csharp
   services.AddScoped<IOrderRepository, OrderRepository>();
   services.AddScoped<IOrderService, OrderService>();
   ```

3. **Per-request state** — текущий пользователь, request ID, scope-локальный
   кеш на время запроса.

   ```csharp
   services.AddScoped<ICurrentUserContext, CurrentUserContext>();
   services.AddScoped<IRequestContext, RequestContext>();
   ```

4. **UnitOfWork** — если паттерн используется.

#### Особенности Scoped

- **Single-threaded внутри scope** — в один scope (одно HTTP-обращение)
  обычно один поток. Можно не делать thread-safe.
- **Disposed at scope end** — все scoped с `IDisposable` корректно
  освобождаются вместе со scope.
- **Вне HTTP — создавай scope вручную** через `IServiceScopeFactory`:

  ```csharp
  public sealed class OrderArchiveWorker(
      IServiceScopeFactory scopeFactory,
      ILogger<OrderArchiveWorker> logger) : BackgroundService
  {
      protected override async Task ExecuteAsync(CancellationToken stoppingToken)
      {
          while (!stoppingToken.IsCancellationRequested)
          {
              await using var scope = scopeFactory.CreateAsyncScope();
              var orderService = scope.ServiceProvider.GetRequiredService<IOrderService>();

              await orderService.ArchiveExpiredAsync(stoppingToken);

              await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
          }
      }
  }
  ```

### Когда выбирать Transient

Самый редкий выбор. Используется только когда **точно знаешь зачем**.

#### Используй Transient для

1. **Builders с накапливающимся состоянием** — каждому потребителю нужен
   свежий, не shared экземпляр.

   ```csharp
   services.AddTransient<IQueryBuilder, QueryBuilder>();
   services.AddTransient<IPdfReportBuilder, PdfReportBuilder>();

   // Использование:
   var query = queryBuilder
       .From("orders")
       .Where("status = 'pending'")
       .OrderBy("created_at")
       .Build();
   ```

2. **Mediator handlers** (MediatR-style) — каждое сообщение получает
   свой handler.

3. **Когда нужен `IDisposable` с коротким временем жизни** — короче чем
   scope. Создаётся в начале операции, освобождается в конце.

4. **Когда нужны разные конфигурации одного типа** — каждый injection
   получает свой экземпляр с собственной конфигурацией. Редкий случай.

#### Особенности Transient

- **Самый дорогой по аллокациям** — N экземпляров на N injects. Не
  используй для горячего пути.
- **Disposable transient опасен.** Если transient реализует `IDisposable`,
  DI container **держит ссылку** и диспозит только при shutdown scope/root.
  Это утечка памяти. Решение — explicit creation:

  ```csharp
  // ❌ Wrong — DI держит все экземпляры, утечка
  services.AddTransient<IDisposableWorker, DisposableWorker>();

  public sealed class OrderProcessor(IDisposableWorker worker)  // утечка с каждым вызовом
  {
      public async Task ProcessAsync()
      {
          await worker.DoAsync();
      }
  }

  // ✅ Correct — factory pattern, освобождаем сами
  services.AddSingleton<Func<IDisposableWorker>>(provider =>
      () => new DisposableWorker());

  public sealed class OrderProcessor(Func<IDisposableWorker> workerFactory)
  {
      public async Task ProcessAsync()
      {
          using var worker = workerFactory();
          await worker.DoAsync();
      }
  }
  ```

#### НЕ используй Transient для

- "По умолчанию" — это не default. Default — singleton.
- Сервисов в горячем пути обработки запросов.
- IDisposable сервисов без factory pattern.

### Decision tree

```
Какой lifetime выбрать?

├── Зависит от DbContext или другого Scoped сервиса?
│   └── Scoped
│
├── Держит per-request state (CurrentUser, RequestId, request-local cache)?
│   └── Scoped
│
├── Builder с накапливающимся состоянием?
│   └── Transient
│
├── IDisposable с lifetime короче чем scope?
│   └── Transient через factory (не register напрямую как Transient)
│
├── Stateless или concurrent-safe state?
│   └── Singleton
│
└── Дорогая инициализация (compile, prewarm, build cache)?
    └── Singleton
```

В сомнении — **Singleton**. Если потом выяснится что нужен scope, рефакторинг
прост: меняешь `AddSingleton` на `AddScoped`. Обратное (с scoped на singleton)
обычно сложнее из-за thread safety.

### Captive dependency — главная ошибка

**Singleton не может зависеть от Scoped или Transient.** Это **captive
dependency** — singleton при первом создании захватит **один** экземпляр
scoped/transient и будет держать его до конца жизни приложения.

```csharp
// ❌ Wrong — singleton принимает scoped DbContext
services.AddSingleton<IOrderArchiver, OrderArchiver>();
services.AddDbContext<OrdersDbContext>(/* ... */);   // Scoped по умолчанию

public sealed class OrderArchiver(OrdersDbContext dbContext)
{
    // dbContext будет тот же самый между разными запросами
    // → state leaks, race conditions, stale change tracker
}
```

Результат: тот же `DbContext` живёт сколько и приложение. Change tracker
накапливает entities, между запросами видны изменения других, рано или
поздно `ObjectDisposedException` или `InvalidOperationException`.

**Решение** — `IServiceScopeFactory`:

```csharp
// ✅ Correct
services.AddSingleton<IOrderArchiver, OrderArchiver>();

public sealed class OrderArchiver(IServiceScopeFactory scopeFactory)
{
    public async Task ArchiveAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();

        var expiredOrders = await dbContext.OrdersSet
            .Where(order => order.UpdatedAt < DateTimeOffset.UtcNow.AddDays(-30))
            .ToListAsync(cancellationToken);

        // ...

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
```

### Защита от captive dependency

В Development включай валидацию scope при сборке container — поймает
captive dependency до того как код попадёт в прод:

```csharp
builder.Host.UseDefaultServiceProvider((context, options) =>
{
    options.ValidateScopes = context.HostingEnvironment.IsDevelopment();
    options.ValidateOnBuild = true;
});
```

- **`ValidateScopes`** — проверяет, что singleton не зависит от scoped.
- **`ValidateOnBuild`** — пытается резолвить **все** зарегистрированные
  сервисы при `Build()`. Ловит missing dependencies до первого запроса.

В Production `ValidateScopes` отключаем — он стоит производительности.
Но `ValidateOnBuild` оставляем включённым — это разовая проверка при
старте.

### Disposable services

`IDisposable` / `IAsyncDisposable` ведут себя по-разному в зависимости
от lifetime:

| Lifetime | Когда вызывается `Dispose()` |
|----------|-------------------------------|
| Singleton | При shutdown приложения (root scope dispose) |
| Scoped | При завершении scope (конец HTTP-запроса) |
| Transient | **При shutdown root scope** — если зарегистрирован напрямую |

Последний пункт — источник утечек. Transient `IDisposable` без factory
pattern означает, что DI container держит ссылки на **все** созданные
экземпляры до завершения приложения.

```csharp
// ❌ Wrong — все DisposableWorker живут до shutdown
services.AddTransient<DisposableWorker>();

// ✅ Correct — factory, освобождение управляется потребителем
services.AddSingleton<Func<DisposableWorker>>(_ => () => new DisposableWorker());

// Альтернатива: явное создание, без DI
public sealed class OrderProcessor
{
    public async Task ProcessAsync()
    {
        await using var worker = new DisposableWorker();  // освобождается at scope end
        await worker.DoAsync();
    }
}
```

Из этого правила есть один важный случай — **`DbContext`**. Он
`IDisposable`, но регистрируется через `AddDbContext` как **Scoped**.
Это корректно: `Dispose()` вызывается в конце HTTP-запроса.

### HTTP clients — особый случай

**Никогда не делай `new HttpClient()` руками** и не регистрируй
`services.AddSingleton<HttpClient>()`. Причины:

- Singleton `HttpClient` не обновляет DNS (если IP бэкенда меняется).
- `new HttpClient()` на каждый запрос приводит к **port exhaustion**
  (TCP connections в TIME_WAIT не успевают освобождаться).

**Используй `IHttpClientFactory`** через `AddHttpClient<T>`:

```csharp
services.AddHttpClient<IPublicApiClient, PublicApiClient>(client =>
{
    client.BaseAddress = new Uri(options.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
})
.AddStandardResilienceHandler();   // Retry, circuit breaker, timeout — из коробки в .NET 8+
```

- `IPublicApiClient` инжектируется как **transient**, но создание дешёвое.
- Внутри переиспользуется `HttpMessageHandler` (singleton через handler pool).
- DNS периодически обновляется (handler ротируется каждые 2 минуты по
  умолчанию).

### Quick reference matrix

| Сценарий | Lifetime | Пример |
|----------|----------|--------|
| Calculator / Validator / Mapper (stateless) | Singleton | `SpreadCalculator`, `OrderValidator` |
| Thread-safe кеш | Singleton | `ExchangeRateCache : ConcurrentDictionary` |
| `IOptions<T>` | Singleton | автоматически через `AddOptions<T>` |
| `TimeProvider` | Singleton | `services.AddSingleton(TimeProvider.System)` |
| Дорогая инициализация | Singleton | `Regex` с `RegexOptions.Compiled`, `JsonSerializerOptions` |
| `DbContext` | Scoped | через `AddDbContext<T>` |
| Repository / QueryService | Scoped | использует `DbContext` |
| Business service с persistence | Scoped | использует Repository |
| `ICurrentUserContext` | Scoped | per-request данные |
| Builder с состоянием | Transient | `QueryBuilder`, `PdfReportBuilder` |
| Mediator handler | Transient | per-message обработка |
| `HttpClient` потребитель | Transient через `AddHttpClient<T>` | `PublicApiClient` |
| `DbContext` в singleton | Через `IServiceScopeFactory` | hosted service / background worker |
| `IDisposable` transient | Через factory `Func<T>` | избегаем утечки |

---

## 8. Anti-patterns

### Service locator

```csharp
// ❌ Wrong — service locator anti-pattern
public sealed class OrderService(IServiceProvider serviceProvider)
{
    public async Task DoAsync()
    {
        var repository = serviceProvider.GetRequiredService<IOrderRepository>();
        var validator = serviceProvider.GetRequiredService<IOrderValidator>();
        // ...
    }
}

// ✅ Correct — явные зависимости в конструкторе
public sealed class OrderService(IOrderRepository repository, IOrderValidator validator)
{
    public async Task DoAsync()
    {
        // используем repository / validator напрямую
    }
}
```

Допустимое исключение — **singleton, которому нужен scoped service**
(см. §7), там используется `IServiceScopeFactory`.

### Сборка ServiceProvider до Build

```csharp
// ❌ Wrong — собираем DI ради чтения настроек
var tempProvider = builder.Services.BuildServiceProvider();
var options = tempProvider.GetRequiredService<IOptions<MyOptions>>().Value;

if (options.SomeFlag)
{
    builder.Services.AddSingleton<ISomething, SomethingElse>();
}

// ✅ Correct — читаем IConfiguration напрямую
var options = builder.Configuration
    .GetSection(MyOptions.SectionName)
    .Get<MyOptions>();

if (options is { SomeFlag: true })
{
    builder.Services.AddSingleton<ISomething, SomethingElse>();
}
```

### Авторегистрация через рефлексию

```csharp
// ❌ Wrong — сканирование сборок
foreach (var type in Assembly.GetExecutingAssembly().GetTypes())
{
    if (type.GetInterfaces().Any(i => i.Name.StartsWith("I")))
    {
        services.AddScoped(type.GetInterfaces().First(), type);
    }
}

// ✅ Correct — явные регистрации в Installer
public static IServiceCollection AddOrderFeatureCore(
    this IServiceCollection services,
    IConfiguration configuration)
{
    services.AddScoped<IOrderService, OrderService>();
    services.AddScoped<IOrderValidator, OrderValidator>();
    services.AddScoped<IOrderMapper, OrderMapper>();
    return services;
}
```

Регистрация трёх сервисов руками — это три строки. Регистрация через
рефлексию — это магия, ломающаяся при первой нестандартной ситуации
(generic-сервисы, decorators, named services).

### `IConfiguration` в сервисах

```csharp
// ❌ Wrong
public sealed class OrderService(IConfiguration configuration)
{
    public Task DoAsync()
    {
        var maxAttempts = configuration.GetValue<int>("Features:Order:MaxAttempts");
    }
}

// ✅ Correct
public sealed class OrderService(IOptions<OrderOptions> options)
{
    public Task DoAsync()
    {
        var maxAttempts = options.Value.MaxAttempts;
    }
}
```

### Магические строки путей к секциям

```csharp
// ❌ Wrong — путь к секции дублируется
services.AddOptions<KafkaOptions>()
    .Bind(configuration.GetSection("Brokers:Kafka"))   // здесь
    .ValidateOnStart();

var kafkaOptions = configuration.GetSection("Brokers:Kafka").Get<KafkaOptions>();  // и здесь

// ✅ Correct — SectionName const в самом классе
public sealed class KafkaOptions
{
    public const string SectionName = "Brokers:Kafka";
    // ...
}

services.AddOptions<KafkaOptions>()
    .Bind(configuration.GetSection(KafkaOptions.SectionName))
    .ValidateOnStart();
```

### Один Options-класс на всё приложение

```csharp
// ❌ Wrong — мега-Options на всё
public sealed class ApplicationOptions
{
    public DatabaseOptions Database { get; init; } = null!;
    public KafkaOptions Kafka { get; init; } = null!;
    public RedisOptions Redis { get; init; } = null!;
    public OrderOptions Order { get; init; } = null!;
    // ... ещё 10 секций
}

// ✅ Correct — отдельный Options на каждую логическую секцию
public sealed class DatabaseOptions { /* ... */ }
public sealed class KafkaOptions { /* ... */ }
public sealed class RedisOptions { /* ... */ }
public sealed class OrderOptions { /* ... */ }
```

Каждый модуль регистрирует свой Options отдельно — `OrderFeatureInstaller`
не должен знать про существование `KafkaOptions`.

### `IOptions<T>.Value` в конструкторе сохранён в поле

```csharp
// ❌ Wrong — теряем возможность обновлений (для Monitor)
//          и работаем с моментальным снимком
public sealed class OrderService(IOptions<OrderOptions> options)
{
    private readonly OrderOptions orderOptions = options.Value;
}

// ✅ Correct — храним IOptions, читаем .Value при использовании
public sealed class OrderService(IOptions<OrderOptions> orderOptions)
{
    public Task DoAsync()
    {
        var maxOrders = orderOptions.Value.MaxOrdersPerUserPerHour;
    }
}
```

Для `IOptions<T>` это правило менее важно (значение всё равно фиксированное),
но для `IOptionsMonitor<T>` критично — `.CurrentValue` должен читаться
каждый раз, иначе теряются обновления.

### Пустой `Use<Module>Core`

```csharp
// ❌ Wrong — пустой метод-шум
public static WebApplication UseOrderFeatureCore(this WebApplication application)
{
    // do nothing
    return app;
}
```

Если модулю не нужен `Use*Core` — не пиши его вообще. Меньше шума в
`Program.cs`, меньше ложных ожиданий «там что-то происходит».

---

## Quick reference

| Что | Где |
|-----|-----|
| Регистрация сервисов модуля | `<Module>Installer.Add<Module>Core(IServiceCollection, IConfiguration)` |
| Middleware/endpoints модуля | `<Module>Installer.Use<Module>Core(WebApplication)` |
| Путь к секции конфига | `OrderOptions.SectionName` const внутри Options-класса |
| Валидация конфига | `.ValidateDataAnnotations().ValidateOnStart()` |
| Cross-field validation | `.Validate(predicate, message)` или `IValidateOptions<T>` |
| Чтение конфига в Installer | `configuration.GetSection(X).Get<T>()` |
| Default Options интерфейс | `IOptions<T>` (singleton snapshot) |
| Options с remote provider | `IOptionsMonitor<T>` |
| Cross-cutting setup | `shared/<Company>.<App>.Composition` |
| Per-application setup | `shared/<Company>.<App>.Dependency.<App>` (опционально) |
| HTTP-клиенты | `services.AddHttpClient<T>` (никогда `new HttpClient()`) |
| Singleton ↔ Scoped | через `IServiceScopeFactory.CreateAsyncScope()` |

---

## Связанные правила

- `CODING-RULES.md` §4 — почему `sealed class` (record не подходит для Options)
- `CODING-RULES.md` §1 — нейминг (не путать `Options` / `Settings` / `Config`)
- `FRAMEWORK-RULES.md` §1 — EF Core: `DbContext` через `AddDbContext`, scope
- `FRAMEWORK-RULES.md` §3 — ASP.NET Core: Controller pattern
- `PROJECT-STRUCTURE.md` §4 — где лежит `*.Composition` (слой `shared/`)
- `PROJECT-STRUCTURE.md` §8 — internal structure проекта, папка `Installers/`
- `TESTING-RULES.md` §5 — `WebApplicationFactory<Program>` (как раз использует composition root)
- `process/build-verification.md` — `ValidateOnBuild` валидация при сборке
- `coding/ANALYZERS.md` — analyzer пакеты, как ловится DI-misuse

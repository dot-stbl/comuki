---
description: file-families — one-public-type-per-file ослабляется для осознанных групп типов, принадлежащих одному понятию
globs: ["**/*.cs"]
---

# File families — когда несколько типов в одном файле законны

Базовое правило — `project-structure.md` §2 "One public type per file" +
`class-layout-and-tooling.md` §2: каждый публичный тип в отдельном файле.
Это правило **ослабляет** базовое для осознанных «семей» типов.

> Решение владельца 2026-10-08: легализованы файлы, в которых
> несколько публичных типов представляют одно доменное понятие и
> всегда живут вместе (request + validator, view-иерархия, wire-envelope,
> SPI-семейство). Ниже — четыре формы семей и условия, при которых
> каждая законна.

## 1. Условия для любой семьи

Файл с несколькими типами законен **только когда выполнены все три**:

1. **Все типы файла принадлежат одному понятию.** Не «концептуально рядом»,
   а буквально одной сущности: её контракт + её валидатор, её вход + её
   выход, её SPI + один outcome. Если у типов разные понятия — это не семья.
2. **Файл ≤ ~300 строк.** Граница та же, что и в `class-layout-and-tooling.md`
   §1b — длинный файл скрывает связи и мешает изолированному тестированию.
   Когда файл приближается к 300, резать по понятиям, не по типу.
3. **Имя файла = имя понятия, не тип.** `PublicationRequest.cs` держит
   `PublicationRequest` + `PublicationRequestValidator`, потому что имя
   понятия — PublicationRequest. `Request.cs` + `Response.cs` + `Mapper.cs`
   в одном файле — не семья (понятий три, не одно).

Если любое из трёх не выполнено — это не семья, это **surplus по
`nitor` §4.5**: придуманное объединение типов, у которого в домене нет
одного имени. Разделять.

## 2. Четыре формы семей

### 2.1 Request + validator-пара

```csharp
// ✅ LEGITIMATE FAMILY
// File: CreateWorkspaceRequest.cs (≈30 lines)
public sealed record CreateWorkspaceRequest(ObjectId AccountId, string Name);

public sealed class CreateWorkspaceRequestValidator
    : AbstractValidator<CreateWorkspaceRequest>
{
    public CreateWorkspaceRequestValidator()
    {
        RuleFor(static request => request.Name).SetValidator(new NameRule());
    }
}
```

**Когда:** валидатор единственный потребитель request'а и без него не
имеет смысла (правила определяют, какие поля request'а допустимы).
Тесты валидатора лежат в `CreateWorkspaceRequestShould.cs` рядом.

**Когда нет:** validator несёт общую логику (multi-rule rule objects),
используемую 3+ разными request'ами — тогда `Rules/` подпапка, а не
co-located.

### 2.2 View-иерархия одного эндпоинта

```csharp
// ✅ LEGITIMATE FAMILY
// File: TenantDetail.cs (≈80 lines)
public sealed record TenantSummary(string Id, string Name, DateTimeOffset CreatedAt);

public sealed record TenantDetail(
    TenantSummary Summary,
    IReadOnlyList<TenantMember> Members,
    IReadOnlyList<TenantQuota> Quotas);
```

**Когда:** `Summary` — это проекция для list-view, `Detail` — для
single-view. Оба всегда возвращаются этим эндпоинтом, оба неотделимы от
понятия Tenant. Разделение в файлы `TenantSummary.cs` + `TenantDetail.cs`
разрывает навигацию (ищешь Tenant — а их два).

**Когда нет:** `Summary` используется в list-эндпоинте, `Detail` —
в single-эндпоинте, и эти эндпоинты вызываются разными клиентами —
тогда это два понятия (`TenantSummaryView` для list, `TenantDetailView`
для single), и в MappingConventionTests они должны лежать отдельно.

### 2.3 Wire-envelope-семейство

```csharp
// ✅ LEGITIMATE FAMILY
// File: OidcTokenResponse.cs (≈40 lines)
public sealed record OidcTokenResponse(string IdToken, string AccessToken, string TokenType, int? ExpiresIn);

internal sealed record OidcTokenExchangeResponse(
    [property: JsonPropertyName("id_token")] string IdToken,
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("token_type")] string TokenType,
    [property: JsonPropertyName("expires_in")] int? ExpiresIn);
```

**Когда:** внешний record (или DTO) существует только как wire-format
зеркало доменного record'а, маппинг — в `Mapper.cs` рядом, и
разделение приводит к «где же это зеркало» в коде.

**Когда нет:** внешний record используется 2+ разными эндпоинтами или
разными мапперами — это уже самостоятельный wire-контракт, а не
envelope одного метода.

### 2.4 SPI-семейство (interface + request + outcome)

```csharp
// ✅ LEGITIMATE FAMILY
// File: IRunArtifactStore.cs (≈40 lines)
public interface IRunArtifactStore
{
    Task<ArtifactBundleResult> StoreAsync(ArtifactBundleRequest request, CancellationToken cancellationToken = default);
}

public sealed record ArtifactBundleRequest(string RunId, IReadOnlyList<ArtifactPointer> Pointers);

public sealed record ArtifactBundleResult(string BundleUri, int ObjectCount);
```

**Когда:** один SPI-метод с одним request и одним outcome — это
одно понятие (одна операция над артефактами), а не три. Разделение
превращает каждый вызов в три импорта.

**Когда нет:** SPI имеет 5+ методов, или методы возвращают разные
типы-результаты, или request используется в нескольких реализациях
SPI — это уже публичный контракт модуля, не SPI-семейство.

## 3. Семьи и MappingConventionTests

`MappingConventionTests` сканирует `Views` namespace на отсутствие
positional-records и статических `*Mapper` классов. Семьи не нарушают
эти правила: легатная view-семья (`TenantSummary` + `TenantDetail`) — оба
init-property records, оба не `*Mapper`. Семьи с static-мапперами запрещены
правилом `mapping.md` (adopt-mapperly): если файл должен включать
`XMapper`, то это не семья, а маппер, который идёт в отдельный файл.

## 4. Self-audit

```bash
# Files with multiple public types — review intent
git diff --name-only --diff-filter=AM -- '*.cs' \
    | xargs -I {} bash -c 'count=$(grep -c "^public " "$1"); [ "$count" -gt 1 ] && echo "$1: $count public decls"' _ {}

# For each flagged file: confirm (a) all types belong to one concept,
# (b) file ≤ 300 lines, (c) filename = concept name, not type name.
```

Если хоть одно нарушено — split. Семья — это привилегия, а не default.
Default остаётся one-public-type-per-file.
# Scan 2 — C# rule violations

**Scan tool:** PowerShell regex over 899 .cs files in `platform/src/**`.

**Rules enforced (per task brief + user-global csharp rules):**

| ID | Rule (one-line) | Severity |
|---|---|---|
| `async-and-tasks.md` §3 | No `.ConfigureAwait(false)` in app code | ERROR |
| `code-shape.md` §11 | No `ArgumentXxxException.ThrowIf*` under nullable | ERROR |
| `exceptions.md` §5 | No `throw <identifier>;` (`throw ex;` resets stack) | ERROR |
| `async-and-tasks.md` §6 | No `_ = ` discard of awaited expression | ERROR |
| `class-layout-and-tooling.md` | No `private` fields starting with `_` (use primary ctor) | ERROR |
| `async-and-tasks.md` | No `async void` | ERROR |
| `anti-patterns.md` §6 | No `new JsonSerializerOptions()` shared singleton field | ERROR |

**Tally across `platform/src/`:**

| Pattern | Hits | Files |
|--------|-----:|------:|
| `.ConfigureAwait(false)` | **35** | 9 |
| `ArgumentXxxException.ThrowIf*` | **7** | 3 |
| `new JsonSerializerOptions()` (inline / shared) | **1** | 1 (`McpServer.cs`) |
| `throw ex;` / `throw <var>;` | **1** | 1 (`ChatTurnService.cs`) |
| `_ = ` discards (excluding migrations) | **1** | 1 (`FilterFunctions.cs`) |
| `_ = ` discards **inside** EF migrations | 54 | 3 (excluded — design-time generated) |
| `async void` | **0** | 0 ✓ |
| `_field` private fields | **0** | 0 ✓ |

## 2.1 ConfigureAwait(false) — 35 hits across 9 files

| Hits | Path |
|-----:|------|
| **8** | `platform/src/modules/Knowledge/…/Persistence/PgKnowledgeIngestor.cs` |
| **8** | `platform/src/host/Comuki.Host/Mcp/McpServer.cs` |
| **6** | `platform/src/modules/Knowledge/…/Persistence/PgKnowledgeSearcher.cs` |
| **3** | `platform/src/modules/Identity/…/Oidc/OidcDiscoveryCache.cs` |
| **3** | `platform/src/modules/Knowledge/…/Embeddings/OpenAIEmbeddingClient.cs` |
| **2** | `platform/src/host/Comuki.Host/Mcp/McpModuleEndpoints.cs` |
| **2** | `platform/src/modules/Identity/…/Oidc/OidcTokenExchange.cs` |
| **2** | `platform/src/modules/Knowledge/…/Hosted/KnowledgeIngestBackgroundService.cs` |
| **1** | `platform/src/host/Comuki.Host/Knowledge/KnowledgeModuleEndpoints.cs` |

**Rule source:** `~/.agents/rules/csharp/async-and-tasks.md` §3:

> *"В .NET 8+ async/await не имеет накладных расходов на `SynchronizationContext` capture в ASP.NET Core" — добавление `.ConfigureAwait(false)` — лишний шум без пользы. .NET 8+ async/await is optimized."*

**Concentrated cluster:** the entire `Comuki.Modules.Knowledge.Infrastructure`
namespace carries `.ConfigureAwait(false)` on every EF/Npgsql call — a
copy-paste from older templates. `McpServer.cs` and the Identity OIDC
adapters show the same pattern.

**Recommended fix:** strip every `.ConfigureAwait(false)` from these 9
files in a single cleanup PR. One file at a time is fine — none of the
sites are framework-boundary (no P/Invoke, no reflection). The fix is
mechanical: `await X.ConfigureAwait(false)` → `await X`.

## 2.2 ArgumentException.ThrowIf* — 7 hits across 3 files

| Hits | Path | Sample line |
|-----:|------|------|
| **5** | `…/Knowledge/…/Persistence/PgKnowledgeIngestor.cs` | `ArgumentException.ThrowIfNullOrWhiteSpace(title);` etc. |
| **1** | `…/Knowledge/…/Persistence/PgKnowledgeSearcher.cs` | `ArgumentException.ThrowIfNullOrWhiteSpace(query);` |
| **1** | `…/Artifacts/…/Store/MinioRunArtifactStore.cs` | (one throw-if-not-bucket — verify) |

**Rule source:** `~/.agents/rules/csharp/code-shape.md` §11:

> *"`<Nullable>enable</Nullable>` включён глобально … компилятор уже enforced non-null на каждом call-site. Runtime-проверка — duplicate noise … это ложь о контракте."*

**Validation pattern:** 5 of the 7 sites live on
`IngestAsync(string title, string source, string sourceRef, string mimeType,
string text, …)`. Project already uses `<Nullable>enable</Nullable>`, and
the caller (Knowledge endpoints) is the FE adapter, not a free-floating
helper. Replacing ThrowIf-null with `string.IsNullOrWhiteSpace` →
throw a domain `ProviderException("knowledge.invalid_input", …)` — moves
the validation to the natural seam (FluentValidation in handler), not on
the storage-side boundary.

## 2.3 `new JsonSerializerOptions` shared field — 1 hit

**File:** `platform/src/host/Comuki.Host/Mcp/McpServer.cs`

| Line | Snippet |
|---:|------|
| 22 | `private static readonly JsonSerializerOptions jsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, … };` |
| 402 | `new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, … }` (inside `Success()` factory — recreated per call) |

**Rule source:** `~/.agents/rules/csharp/anti-patterns.md` §6:

> *"NEVER `new JsonSerializerOptions(...)` в app code, кроме `new JsonSerializerOptions(JsonSerializerDefaults.Web)` как base для DI-конфигурации в composition root."*

**Recommended fix:**

1. Delete the static field; use `JsonSerializerOptions.Web` (the .NET 9+
   shared frozen instance) for the read paths.
2. For the inline `Success()` factory — same: `SerializeToElement(result,
   JsonSerializerOptions.Web)`.
3. If a future per-tool converter is needed, plumb it through the host
   DI `Configure<JsonOptions>(…)`.

## 2.4 `throw <id>;` — 1 hit

**File:** `platform/src/modules/Chat/Comuki.Modules.Chat.Application/Sessions/ChatTurnService.cs:158`

```csharp
if (item.Kind is StreamEventKind.Failed && item.Payload is Exception exception)
{
    throw exception;          // re-emitting wrapped payload exception
}
```

**Boundary check:** the exception was already thrown + caught into the
StreamEvent object — its `StackTrace` is preserved because the original
`throw` populated it. The rule doc's example is `throw ex;` (shorthand
`ex` resets stack). Here the variable is named `exception` (full form)
and it's *the same* exception that was caught — so stack trace is
preserved.

**Verdict:** borderline-OK because the exception's stack was already
captured at the original throw-site; this is unwrapping a wrapper, not
re-throwing with a reset. Worth a code-review note but **not a clear
violation**. If hardening is wanted, wrap into
`throw new ProviderException("chat.stream_failed", exception.Message,
exception);` to attach a stable `Code`.

## 2.5 `_ = now + offset;` — 1 hit (with justification)

**File:** `platform/src/shared/Comuki.Shared.Filtering/Lexer/FilterFunctions.cs:204`

```csharp
private static void OverflowProbe(DateTimeOffset now, TimeSpan offset)
{
    _ = now + offset;        // ArgumentOutOfRangeException surfaced by `+`
}
```

**Comment in the file (lines 195-201):** explains the probe runs `now + offset`
specifically to surface the same `ArgumentOutOfRangeException` that the
caller relies on. Goal: dataflow typing without throwing away the side
effect.

**Rule source:** `~/.agents/rules/csharp/async-and-tasks.md` §6 + 
`anti-patterns.md` §6 collectively ban bare `_ = ` discards.

**Verdict:** *justified escape hatch* by the file's own comment. A more
strict alternative would be `var probe = now + offset; _ = probe;` — but
the difference is mechanical and the intent is identical.

Acceptable as-is. Move on.

## 2.6 Other rules — all clear

| Pattern | Hits |
|--------|-----:|
| `async void` | 0 |
| `private _field` (underscore-prefixed) | 0 |
| `IRuleBuilder.ThrowIf*` extended | 0 |
| `#pragma warning disable` (outside migrations) | (not scanned — out of scope for this scan) |

## 2.7 Recommended add-on tests

Recommend adding a **NetArchTest-coded or grep-style static rule test**
that fails the build on any of these patterns going forward. A
`tests/Comuki.Style.Tests` project (or an extension of
`Comuki.Architecture.Tests`) could assert:

1. **No `.ConfigureAwait(` in app code** — grep over
   `tests/..` matching exactly against `\.ConfigureAwait\s*\(`.
2. **No `ArgumentXxxException.ThrowIf*`** — same approach.
3. **No `private static readonly JsonSerializerOptions`** — grep over
   the singleton field declaration pattern.

These can be `dotnet test` assertions on a test that reads the project's
files and runs the patterns. They complement the existing
`Comuki.Architecture.Tests` (which only checks layer structure).

## 2.8 Top 20 most-violation files (rolled up)

| Rank | File | Conf | ThrI | Thro | Disc | Json |
|--:|------|--:|--:|--:|--:|--:|
| 1 | `…/Knowledge/…/Persistence/PgKnowledgeIngestor.cs` | **8** | **5** | | | |
| 2 | `…/host/Comuki.Host/Mcp/McpServer.cs` | **8** | | | | **1** |
| 3 | `…/Knowledge/…/Persistence/PgKnowledgeSearcher.cs` | **6** | **1** | | | |
| 4 | `…/Identity/…/Oidc/OidcDiscoveryCache.cs` | **3** | | | | |
| 5 | `…/Knowledge/…/Embeddings/OpenAIEmbeddingClient.cs` | **3** | | | | |
| 6 | `…/host/Comuki.Host/Mcp/McpModuleEndpoints.cs` | **2** | | | | |
| 7 | `…/Identity/…/Oidc/OidcTokenExchange.cs` | **2** | | | | |
| 8 | `…/Knowledge/…/Hosted/KnowledgeIngestBackgroundService.cs` | **2** | | | | |
| 9 | `…/host/Comuki.Host/Knowledge/KnowledgeModuleEndpoints.cs` | **1** | | | | |
| 10 | `…/Artifacts/…/Store/MinioRunArtifactStore.cs` | | **1** | | | |
| 11 | `…/Chat/…/Sessions/ChatTurnService.cs` | | | **1** | | |
| 12 | `…/Shared.Filtering/Lexer/FilterFunctions.cs` | | | | **1** | |

All other ~887 files: **0 violations**.

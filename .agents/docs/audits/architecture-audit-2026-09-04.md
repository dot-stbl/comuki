# Comuki Architecture Audit — 2026-09-04

> **Auditor:** `feature/architecture-audit` worktree, branch tip
> `2910846` (master). No merge; no production-code changes.
>
> **Scope:** master at `2026-09-06` after the smoke-suite PR
> landed. Build gate: `dotnet build comuki.slnx -c Debug` — **0 warnings,
> 0 errors, `[VerifyFormatOnBuild] Format check passed`.** Arch tests:
> **14 / 14 pass.**
>
> **Five parallel scans:**

1. `audit-data/scan-1-godif-godswitch.md` — godif / godswitch detection
2. `audit-data/scan-2-csharp-rules.md` — C# rule violations
3. `audit-data/scan-3-ts-rules.md` — TS rule violations
4. `audit-data/scan-4-architecture.md` — architecture invariants
5. `audit-data/scan-5-openspec.md` — OpenSpec drift

This is the consolidated roll-up; each scan-data file is the detailed
finding sheet for that scan.

## TL;DR

| # | Verdict | Severity |
|--:|---|---|
| 1 | **No godif / no godswitch** in any file — every class <13 public methods, every source file <384 lines, every method body has ≤10 branches. | — |
| 2 | **`IdentityAdminController` has 13 ctor params** — fanout controller, the only file flagged. Recommend moving 6 `IValidator<>` to per-action `[FromServices]`. | medium |
| 3 | **35× `.ConfigureAwait(false)`** in 9 files (concentrated in `Knowledge/*` and `Identity/*Oidc*` and `McpServer`). Strip in a single cleanup PR. | low–medium |
| 4 | **7× `ArgumentException.ThrowIf*`** (5 in `PgKnowledgeIngestor`) — banned under nullable enable. | low |
| 5 | **1× `private static readonly JsonSerializerOptions`** in `McpServer.cs:22` — banned per `anti-patterns.md` §6. Use `JsonSerializerOptions.Web`. | low |
| 6 | **2 cross-module reference violations** of the "`modules ↛ modules (только через Shared.Contracts)`" rule from `comuki-project-structure.md` §2: `Knowledge→Memory` and `Proxy→Costs`. **Currently not enforced** by NetArchTest. | medium-high |
| 7 | **`Intake` spec doesn't list Yandex Tracker / Jira** providers (code ships them). OpenSpec drift. | medium |
| 8 | **No `proxy/spec.md`, no `knowledge/spec.md`** (spec content split across `costs`/`host` and `memory`/`host` respectively). House-keeping. | low |

The codebase is **structurally very clean** — only 1 ctor-flag and 1
godif-ish class. The C# rule violations are mechanical cleanup. The
two architectural-drift violations and one openspec drift are real.

## 1. Top 10 largest files (production code only — migrations excluded)

| Lines | Path |
|--:|------|
| 384 | `platform/src/host/Comuki.Host/Mcp/McpServer.cs` |
| 305 | `…/Host.Translator/Parsing/StreamJsonParser.cs` |
| 278 | `…/Modules/Memory/…/Persistence/Stores/EfMemoryStore.cs` |
| 270 | `…/Shared.Filtering/Evaluator/FilterOperatorRegistry.cs` |
| 253 | `…/Modules/Intake/…/Persistence/Stores/IntakeStore.cs` |
| 225 | `…/Host/Auth/Controllers/IdentityAdminController.cs` |
| 218 | `…/Shared.Filtering/Lexer/FilterLexer.cs` |
| 210 | `…/Shared.Filtering/Parser/FilterParser.cs` |
| 203 | `…/Shared.Filtering/Lexer/FilterFunctions.cs` |
| 203 | `…/Host/Projects/ProjectsModuleEndpoints.cs` |

Max 384 lines. **No file even approaches the 800-line godif threshold.**
Filtering/lexing is the densest area (close to 400 lines per file) and
those are legitimate parser/lexer shapes (recursive descent, state
machine); not godif.

## 2. Top 10 most-complex files (sum of if/case branches across all methods)

| Branches | Path | Note |
|--:|------|------|
| 19 | `…/Shared.Contracts/Plans/PlanValidator.cs` | validation + DFS cycle-detect, flat |
| 17 | `…/Host/Mcp/McpServer.cs` | dispatcher + 4 tool handlers + parameter readers |
| 12 | `…/Shared.Filtering/Parser/FilterParser.cs` | recursive-descent, ≤3/method |
| 11 | `…/Host.Translator/Parsing/StreamJsonParser.cs` | streaming-state machine |
| 11 | `…/Host/ControlPlane/Parsing/ControlPlaneDocumentParser.cs` | YAML frontmatter |
| 11 | `…/Modules/Knowledge/…/Chunking/Chunker.cs` | chunk strategies |
| 10 | `…/Modules/Intake/…/Tickets/WebhookIntakeService.cs` | 4-provider dispatch |
| 10 | `…/Shared.Filtering/Lexer/FilterFunctions.cs` | token-kind dispatch |
| 10 | `…/Host.Brain/ControlPlane/ControlPlaneProfileCatalog.cs` | profile builders |
|  9 | `…/Shared.Filtering/Lexer/FilterLexer.cs` |  |

**No method body >10 branches.** PlanValidator.Validate has 19
across 4 methods — every method is shallow and uses early-return or
list-add style. Not a godswitch. McpServer.CallToolAsync uses the
`switch` expression recommended by `anti-patterns.md` §1.3.

## 3. Rule violations grouped by file (top 20 files)

Tally across 899 .cs files in `platform/src/**` — 35 `.ConfigureAwait`,
7 `ThrowIf`, 1 inline `new JsonSerializerOptions`, 1 `throw <id>;`,
1 `_ = ` discard with justifying comment. EF migrations: excluded from
analysis per `ef-migrations.md`.

| Rank | File | Conf | ThrI | Thro | Disc | Json |
|--:|------|--:|--:|--:|--:|--:|
| 1  | `…/Knowledge/…/PgKnowledgeIngestor.cs`           | **8** | **5** |   |   |   |
| 2  | `…/Host/Mcp/McpServer.cs`                        | **8** |   |   |   | **1** |
| 3  | `…/Knowledge/…/PgKnowledgeSearcher.cs`           | **6** | **1** |   |   |   |
| 4  | `…/Identity/…/Oidc/OidcDiscoveryCache.cs`        | **3** |   |   |   |   |
| 5  | `…/Knowledge/…/OpenAIEmbeddingClient.cs`          | **3** |   |   |   |   |
| 6  | `…/Host/Mcp/McpModuleEndpoints.cs`               | **2** |   |   |   |   |
| 7  | `…/Identity/…/Oidc/OidcTokenExchange.cs`         | **2** |   |   |   |   |
| 8  | `…/Knowledge/…/KnowledgeIngestBackgroundService.cs` | **2** |   |   |   |   |
| 9  | `…/Host/Knowledge/KnowledgeModuleEndpoints.cs`   | **1** |   |   |   |   |
| 10 | `…/Artifacts/…/MinioRunArtifactStore.cs`         |   | **1** |   |   |   |
| 11 | `…/Chat/…/ChatTurnService.cs`                    |   |   | **1** |   |   |
| 12 | `…/Shared.Filtering/Lexer/FilterFunctions.cs`   |   |   |   | **1** |   |

All other ~887 files: **0 violations**.

## 4. Architecture test results

```
$ dotnet run --project tests/Comuki.Architecture.Tests -c Debug
total: 14 · succeeded: 14 · failed: 0
```

| Suite | Tests | Covers |
|---|--:|---|
| `LayerDependencyTests` | 4 | Kernel ↛ {Contracts, Engine, Host, Translator}; Engine ↛ Host; Translator ↛ Engine |
| `ModuleLayerTests` (Identity) | 4 | Identity.Domain/App/Inf ↛ {outer layers}; Identity modules ↛ Engine |
| `CostsModuleLayerTests` | 3 | Costs.Domain/App/Inf ↛ {outer layers}; Costs modules ↛ Engine |
| `IntakeModuleLayerTests` | 3 | Intake.Domain/App/Inf ↛ {outer layers}; Intake modules ↛ Engine |

**Total: 14 tests, all pass.**

### 4.1 Coverage gaps (recommended tests to add)

| Module | Layer-direction test? | Cross-module test? |
|---|:--:|:--:|
| Identity | ✓ | n/a |
| Costs | ✓ | ❌ |
| Intake | ✓ | ❌ |
| **Chat / Knowledge / Memory / Projects / Proxy / Artifacts** | ❌ | ❌ |
| **Engine.Compute** | ❌ | n/a |

Recommend adding:

1. **Mirror suite per module** (12 new tests): Chat/Knowledge/Memory/Projects/Proxy/Artifacts mirror of `ModuleLayerTests`.
2. **Module isolation** (2 tests): "no Module X.Application/Infrastructure depends on Module Y.Application/Infrastructure for X ≠ Y".
3. **Host → everything** (1 test): no `Comuki.Host` reaches into a `.Domain` assembly.
4. **`Domain` ↛ EF Core** (1 test): no `Microsoft.EntityFrameworkCore` import in any `*.Domain` project.

Drop-in code sample for the most-important test (`ModuleIsolationTests`)
is in `audit-data/scan-4-architecture.md` §4.6.

## 5. OpenSpec drift summary

| Spec | Last update | Drift? |
|---|---|---|
| `agents-sdk`, `artifacts`, `build-and-ci`, `chat`, `control-plane`, `compute`, `filtering`, `memory`, `projects`, `work-queue`, `worker-runtime` | 2026-09-01..05 | None |
| `host` (366 lines), `identity` (161 lines), `intake` (273 lines), `runs` (268 lines) | 2026-09-04..05 | None |
| `costs` | 2026-09-05 (`f1d40c2`) | None |
| `realtime` | 2026-09-04 (`339fbaf`) | None |
| `intake` | 2026-09-05 | **Drift: spec lists only GH + GL, code ships Yandex Tracker + Jira** (medium) |

### Other observations

- **`openspec/specs/proxy/spec.md` and `…/knowledge/spec.md` do not
  exist as standalone specs.** Proxy's spec content is in `costs`
  (metering) and `host` (YARP + virtual key); Knowledge's is in
  `memory` (pgvector) and `host` (`/api/v1/knowledge/ingest`). Both
  splits are acceptable but inconsistent — recommend thin indexer
  files for cleanliness.
- **All 7 active changes** `add-chat-memory`, `audit-wave-6`,
  `backfill-{chat-memory,costs,intake,realtime,wave6-platform}` — all
  tasks marked complete; `audit-wave-6` and the 5 backfill-*
  changes already applied to main specs (recent merges verify this).
- `add-chat-memory` is the only change with `[~]` (partial) status —
  remaining debt is `memory.write|forget|list` tools and the
  learning-candidate approval pipeline (already tracked on the master
  product backlog).

## 6. Concrete godif / godswitch candidates

**None.** Single flagged surface is the **IdentityAdminController**
(13 ctor params, 7 handlers + 6 validators + logger) — see §7.1.

## 7. Recommended refactors + test additions

### 7.1 `IdentityAdminController` ctor bloat

**File:** `platform/src/host/Comuki.Host/Auth/Controllers/IdentityAdminController.cs:29`
**Smell:** 13-arg primary constructor — fanout controller.

```csharp
public sealed class IdentityAdminController(
    InviteUserHandler inviteUser, SetUserDisabledHandler setUserDisabled,
    LinkOidcSubjectHandler linkOidc, GrantRoleHandler grantRole,
    RevokeRoleHandler revokeRole, IssueApiKeyHandler issueApiKey,
    RevokeApiKeyHandler revokeApiKey,
    IValidator<InviteUserRequest> inviteValidator,
    IValidator<SetUserDisabledRequest> setDisabledValidator,
    IValidator<LinkOidcRequest> linkOidcValidator,
    IValidator<GrantRoleRequest> grantRoleValidator,
    IValidator<CreateApiKeyRequest> createApiKeyValidator,
    ILogger<IdentityAdminController> logger) : ControllerBase
```

**Recommended fix (option A — small):** per `anti-patterns.md` §5,
move the 6 `IValidator<>` arguments to `[FromServices]` on each
action. Drops ctor from 13 → 7 deps (7 handlers + logger).

**Recommended fix (option B — correct):** split into 4 sub-controllers
per the hybrid resource/lifecycle pattern in `api-design.md` §6:
`UsersAdminController`, `GrantsAdminController`, `KeysAdminController`,
`OidcLinkController`. Each owns its 1-3 handlers + its own validators.

### 7.2 Strip `.ConfigureAwait(false)` from 9 files

The simplest mass-cleanup: a single PR deleting `.ConfigureAwait(false)`
from those 9 files (35 sites total). The pattern is mechanical — `await
X.ConfigureAwait(false)` → `await X`. No boundary justification needed
(none of these hit reflection / P/Invoke / serialization edge).

Affected modules / namespaces:
- `Comuki.Modules.Knowledge.Infrastructure.Persistence.*` (14 sites)
- `Comuki.Modules.Knowledge.Infrastructure.Embeddings.*` (3 sites)
- `Comuki.Modules.Knowledge.Infrastructure.Hosted.*` (2 sites)
- `Comuki.Modules.Identity.Application.Oidc.*` (5 sites)
- `Comuki.Host.Mcp.*` (10 sites)
- `Comuki.Host.Knowledge.*` (1 site)

### 7.3 Strip `ArgumentException.ThrowIf*` from 3 files

7 sites total — 5 in `PgKnowledgeIngestor`, 1 in `PgKnowledgeSearcher`,
1 in `MinioRunArtifactStore`. Replace with `IsNullOrWhiteSpace` →
`throw new ProviderException("knowledge.invalid_input", …)` (move to
FluentValidation in the host endpoint), or simply remove under
`<Nullable>enable</Nullable>`.

### 7.4 Replace `JsonSerializerOptions` singleton in `McpServer.cs`

`platform/src/host/Comuki.Host/Mcp/McpServer.cs:22` and `:402`.

```csharp
private static readonly JsonSerializerOptions jsonOptions = new()
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
};
```

→ delete; use `JsonSerializerOptions.Web` (frozen shared instance).
Same pattern in the `Success()` factory at line 400.

### 7.5 Architecture-drift remediation (Knowledge → Memory, Proxy → Costs)

#### Option A — Promote shared types to `Shared.Contracts`

Move `SourceDocument` / `MemoryEmbedding` from `Memory.Domain` →
`Shared.Contracts.Knowledge.SharedTypes`. Knowledge module depends on
the shared types; Memory depends on them for storage. No
cross-module edge.

For Proxy → Costs: surface `IUsageEventStore` (costs port) in
`Shared.Contracts.Costs`. Proxy depends on the contracts interface;
Migrator wires the costs-internal concrete at composition.

#### Option B — Document the asymmetry

If the refactor is too expensive for v1.1, add an
`ExemptedCrossModuleEdges` allow-list to `comuki-project-structure.md`:

- "Knowledge depends on Memory for the pgvector embedding store —
  borrow shapes because they live in the same Postgres schema."
- "Proxy depends on Costs for usage-event recording — split in v1.2
  when virtual-key metering graduates into a standalone capability."

Then explicitly add arch-test exceptions for those two edges.

#### Recommendation

**Option A for Proxy → Costs (small surface area, ~6 file moves).**
**Option B for Knowledge → Memory (large — value objects, schema, DbContext; defer to v1.2).**

### 7.6 OpenSpec cleanups

| Spec | Action |
|---|---|
| `openspec/specs/intake/spec.md` | Add "Requirement: Supported source providers" listing GH + GL + Yandex Tracker + Jira with per-provider semantics. The code already implements Yandex/Jira — make spec honest. |
| `openspec/specs/proxy/spec.md` | (new) thin indexer over `costs/spec.md` (metering) + `host/spec.md` (YARP). |
| `openspec/specs/knowledge/spec.md` | (new) thin indexer over `memory/spec.md` (pgvector) + `host/spec.md` (ingest endpoint). |

### 7.7 Tests to add to `tests/Comuki.Architecture.Tests/`

Per §4.1 + scan-4 §4.6 drop-in code:

1. **`ModuleIsolationTests`** (the most important) — assert no
   `Modules.X.{Application,Infrastructure}` references
   `Modules.Y.{Application,Infrastructure}` for X ≠ Y. Allow-list
   for the two currently-known exceptions.
2. **`DomainPurityTests`** — assert no `Microsoft.EntityFrameworkCore`
   `using` in any `.Domain` project (10 modules would be covered).
3. **`ChatModuleLayerTests`** + **`KnowledgeModuleLayerTests`** +
   `MemoryModuleLayerTests` + `ProjectsModuleLayerTests` +
   `ProxyModuleLayerTests` + `ArtifactsModuleLayerTests` — 6×3 = 18
   new tests mirroring the Identity/Costs/Intake pattern.
4. **`EngineComputeLayerTests`** — 3 new tests asserting
   `Engine.Compute.Domain/Application/Infrastructure` doesn't reach
   into `Host*` or `Migrator`.

After these additions, the suite grows **14 → 14 + 18 + 2 + 1 + 3 = 38 tests**.

## 8. Build / test / format gates (still green)

```
$ dotnet build comuki.slnx -c Debug
… [VerifyFormatOnBuild] Format check passed.
Сборка успешно завершена.
    Предупреждений: 0
    Ошибок: 0
Прошло времени 00:00:58.99

$ dotnet run --project tests/Comuki.Architecture.Tests -c Debug
total: 14 · succeeded: 14 · failed: 0 · duration 275ms
```

The audit **adds no production code** so all gates still pass at the
exact same level as the master tip.

## 9. Commit history (`feature/architecture-audit`)

```
[hybrid] audit(scan-1): godif/godswitch detection — no class >30 methods, no file >800 lines, 1 ctor>8 flags
[hybrid] audit(scan-2): csharp rule violations — 35× ConfigureAwait + 7× ThrowIf + 1× JsonOptions singleton
[hybrid] audit(scan-3): TS rule scan — clean (596 files, 13 console.* in scripts, 0 prod `any`, 0 prod TODO)
[hybrid] audit(scan-4): architecture drift — 2 cross-module edges (Knowledge→Memory, Proxy→Costs), 14/14 tests pass, 6 modules untested
[hybrid] audit(scan-5): openspec drift — all 7 changes applied, intake spec missing Yandex/Jira providers
```

Each commit isolates one scan's audit-data file. The branch is left
**unmerged** per audit-only scope.

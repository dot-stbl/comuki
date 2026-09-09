# Comuki v1 Architecture Audit — 2026-09-08

> Read-only architecture audit of the Comuki v1 codebase at branch
> `fix/audit-arch` tip `47b3f47b4b9b986631acf01f40dbd320f2f29e23`.
> Repo path: `C:\Users\bradw\source\hybrid\comuki.orchestrator`.
> No code was changed; this is a read-only survey.
>
> Every finding cites a file and line so the owner can navigate
> immediately. File paths are repo-relative unless prefixed.
>
> Methodology: static analysis with `ripgrep` against the documented
> rules in `~/.agents/rules/csharp/` and `.agents/docs/architecture/`,
> plus selective reads of the layer roots (Domain / Application /
> Infrastructure / Host / Engine), every `DbContext.OnModelCreating`,
> the composition root `HostComposer.cs`, and the architecture tests.

---

## Executive Summary

**State.** The modular-monolith skeleton is structurally sound:
11 modules + 2 engines + 4 hosts + 4 shared libraries + build tools,
declared in `comuki.slnx`. The 6 universal laws (`csharp/architecture.md`)
hold **on the dependency-direction axis** for 10 of the 11 modules —
the exception is a single Costs ↔ Contracts leak that is the most
consequential finding. Composition root (`HostComposer.cs`, 360 lines)
and Program.cs (78 lines) are clean.

**Three things must change before any v1.1 / v2 work:**

1. **`Comuki.Shared.Contracts` ProjectReferences `Comuki.Modules.Costs.Domain`**
   (`platform/src/shared/Comuki.Shared.Contracts/Comuki.Shared.Contracts.csproj:16`)
   — a shared library depends on a specific module. The same leak surfaces
   transitively in two Proxy.Application files. Law 1 violation.
2. **8 of 10 `DbContext`s lack `HasQueryFilter` for the subject-scope**
   ambient (only `OrchestrationDbContext` and `ProjectsDbContext` have it).
   Per-tenant isolation is partially enforced; the rest rely on the
   application code remembering the filter. High-impact correctness gap.
3. **Architecture tests cannot catch the first issue** — the existing
   `ContractsMustDependOnlyOnKernel` test (`tests/Comuki.Architecture.Tests/LayerDependencyTests.cs:31`)
   forbids dependencies on `Engine`/`Host`/`Translator` but says nothing
   about `Modules.*`, so the violation is invisible to CI.

**Other notable.** 38 `_ = ` discards in production code (mostly the
newly-added MergeQueue feature), 4 leftover `.ConfigureAwait(false)`
(removed by analyzers, but still in source), 1 banned `*Dto` suffix
(`TokenResponseDto`), 8 catch-parameter abbreviations (`ex`), 36 private
methods (half of them legitimate lexer/parser internals; the rest in
workers/controllers/handlers that violate `class-layout-and-tooling §1a`).
Folder-organisation: the 3-file cap is **widely and systemically** broken
— Identity.Application has 69 files in one folder, Host has 135.

**Six Laws summary.**

| Law | Status | Worst finding |
|-----|--------|---------------|
| 1. Inward deps, no cycles | ✗ FAIL | `Comuki.Shared.Contracts` → `Comuki.Modules.Costs.Domain` (3 transitively reachable sites) |
| 2. Domain has no framework/IO | ✓ PASS | Zero `Microsoft.*`/`Npgsql`/`HttpClient` imports in any Domain layer |
| 3. Modules don't reference each other | ✓ PASS | Only the Costs↔Proxy leak (Law 1) — otherwise module-to-module imports are clean |
| 4. Concretes wired at composition root | ⚠ PARTIAL | One stray `new JwtSecurityTokenHandler()` in Application; Host.csproj still references `Artifacts.Domain` |
| 5. Entry points are composition-only | ✓ PASS | `HostComposer.Compose` + minimal `Program.cs` (78 lines) |
| 6. Arch enforced by NetArchTest | ⚠ PARTIAL | Test exists; covers 4 layer rules + 8 module-layer tests; **no module-boundary rule** |

---

## 1. Six Laws Compliance

### Law 1 — Dependencies point one way inward, no cycles

**Status: ✗ FAIL** — one concrete module dependency in Shared.Contracts,
two Proxy.Application files reach into Costs.Domain directly.

**Findings.**

| File:line | What | Severity |
|---|---|---|
| `platform/src/shared/Comuki.Shared.Contracts/Comuki.Shared.Contracts.csproj:16` | `<ProjectReference Include="..\..\modules\Costs\Comuki.Modules.Costs.Domain\Comuki.Modules.Costs.Domain.csproj" />` — Shared.Contracts depends on a specific module | **High** |
| `platform/src/shared/Comuki.Shared.Contracts/Usage/IUsageEventStore.cs:1` | `using Comuki.Modules.Costs.Domain.Events;` — interface signature carries `UsageEvent` from Costs.Domain | **High** |
| `platform/src/modules/Proxy/Comuki.Modules.Proxy.Application/Metering/ProxyUsageMeter.cs:2` | `using Comuki.Modules.Costs.Domain.Events;` — Proxy module reads Costs.Domain type | **High** |
| `platform/src/modules/Proxy/Comuki.Modules.Proxy.Application/Budgeting/DefaultProxyBudgetEnforcer.cs:1` | `using Comuki.Modules.Costs.Domain.Events;` — same | **High** |

The comment on `IUsageEventStore.cs:3-6` explains the intent: "Reads
are exposed here so the proxy pre-flight can sum proxy-source spend
without taking a dependency on the Costs module's internal Application
assembly." The author tried to abstract via Contracts, but the
`UsageEvent` shape lives in `Costs.Domain`, so the abstraction leaks.
The right fix is either (a) move `UsageEvent` (or its read-model twin)
into `Shared.Contracts` and have `Costs.Infrastructure` map to it, or
(b) move the data shape to `Shared.Kernel` as a wire-shape event.

**Other checks (all PASS).**

- `rg -n "using Comuki\.Modules\." platform/src/shared/` → only the one leak above.
- `rg -n "using Comuki\.Engine\." platform/src/shared/` → 0 hits.
- `rg -n "using Comuki\.Host" platform/src/shared/ platform/src/modules/ platform/src/engine/` → 0 hits.
- `rg -n "using Comuki\.Engine\." platform/src/modules/` → 0 hits (modules → engine: clean).
- Cross-module imports (`Identity` referencing `Projects`, etc.) → all 0.
- Engine → Modules → 0.

**Recommendation.** Break the chain in either order:
1. Move `UsageEvent` to `Shared.Contracts` (or `Shared.Kernel`) and have `Costs.Infrastructure` map on the way in/out, then drop the `ProjectReference` in `Comuki.Shared.Contracts.csproj`.
2. Add an arch test `ContractsMustNotDependOnAnyModule` to catch regressions.

---

### Law 2 — Domain/core has zero framework/IO dependencies

**Status: ✓ PASS** — every Domain layer is pure types + business logic.

**Checked.** All 10 Domain layers:
`Engine.Orchestration/Domain`, `Modules.Identity/Domain`,
`Modules.Projects/Domain`, `Modules.Chat/Domain`,
`Modules.Memory/Domain`, `Modules.Intake/Domain`,
`Modules.Costs/Domain`, `Modules.Scheduler/Domain`,
`Modules.Knowledge/Domain`.

Plus `Shared.Kernel` (csproj empty — `<Project Sdk="Microsoft.NET.Sdk">`
with no PackageReference).

**Grep.** `^using (Microsoft|System\.Net\.Http|Npgsql|EntityFramework|EF|Microsoft\.EntityFrameworkCore|MySql|StackExchange\.Redis|Confluent\.Kafka|Voluta)`
across all 10 Domain roots → **0 hits**.

**Notable purity.** The Engine.Orchestration Domain (`Domain/Runs`,
`Domain/WorkItems`, `Domain/Journal`, `Domain/MergeQueue`) imports only
`Comuki.Shared.Kernel.Ids`. Engine.Compute has no separate Domain folder;
its `Supervisor` and `Pool` types are application code (Microsoft.Extensions.Hosting
allowed).

---

### Law 3 — Modules don't reference each other

**Status: ✓ PASS** — except for the same Law-1 leak above.

**Cross-module import matrix** (`using Comuki\.Modules\.<other>` per module):

| Source → | refs other Modules |
|---|---|
| Identity | 0 |
| Projects | 0 |
| Chat | 0 |
| Memory | 0 |
| Intake | 0 |
| Costs | 0 |
| Artifacts | 0 |
| **Proxy** | **2** (Costs.Domain.Events, both via Shared.Contracts) |
| Knowledge | 0 |
| Verify | 0 |
| Scheduler | 0 |

The two Proxy→Costs hits are the same Law-1 leak; no other module
talks to its siblings. Inter-module integration happens through
`Shared.Contracts` ports (`IApproveRunPort`, `ICancelRunPort`,
`IRunArtifactJournalSource`, `IRunArtifactRunSource`, `IBrainClient`,
`IMemoryDigest`, `IProjectBudgetSettings`, `IBudgetGate`,
`ISchedulerDispatcher`, `IRunLauncher`, `IRunStatusReader`,
`IUsageEventStore`, `IIntakeProfileRouter`, etc.).

**Recommendation.** Same fix as Law 1.

---

### Law 4 — Concretes wired only at the composition root

**Status: ⚠ PARTIAL** — composition root is correct; one Application-layer
stray `new`; Host references one module's Domain layer.

**Findings.**

| File:line | What | Severity |
|---|---|---|
| `platform/src/modules/Identity/Comuki.Modules.Identity.Application/Oidc/OidcIdTokenValidator.cs:41` | `var handler = new JwtSecurityTokenHandler();` — a `Microsoft.IdentityModel.Tokens` BCL helper; not a Comuki concrete. Acceptable per di-installer.md. | Low |
| `platform/src/host/Comuki.Host/Comuki.Host.csproj:45` | `<ProjectReference Include="..\..\modules\Artifacts\Comuki.Modules.Artifacts.Domain\Comuki.Modules.Artifacts.Domain.csproj" />` — the host reaches into one module's Domain assembly | **Medium** |

Composition root check:

| File | Lines | What it does |
|---|---|---|
| `platform/src/host/Comuki.Host/Program.cs` | 78 | Top-level composition only: `WebApplication.CreateBuilder`, one `HostComposer.Compose` call, three `MapGet`s for control-plane catalog, `MapWorkerRuntime`, `app.RunAsync`. No business logic. |
| `platform/src/host/Comuki.Host/HostComposer.cs` | 360 | DI wiring + auth schemes + controller mapping + endpoint mapping. Each module's `Add<Module>Application()` + `Add<Module>Persistence()` extensions. Options bound with `ValidateDataAnnotations().ValidateOnStart()` (OidcOptions exception noted in audit-product-report §1.4). No business logic. |

Per the cross-cutting `AuditProductReport §1.4`, `HostComposer.cs:191-192`
binds `SchedulerOptions` **without** `ValidateOnStart()`. The audit found
no other `ValidateOnStart` misses, except `OidcOptions` (`HostComposer.cs:210-211`).

**Recommendation.** Either remove the `Artifacts.Domain` reference from
`Host.csproj` (have the artifacts Application layer re-export the
domain types it needs) or document why the host needs it.

---

### Law 5 — Entry points are composition-only

**Status: ✓ PASS** — `Program.cs` is 78 lines, `HostComposer.Compose`
is the only business in the host entry.

`Program.cs:36-40`:
```csharp
builder.Services
    .AddOrchestrationPersistence(database.ConnectionString)
    .AddOrchestrationQueue(builder.Configuration)
    .AddOrchestrationApplication()
    .AddWorkerRuntime(builder.Configuration);
```
…followed by HostComposer.Compose and three MapGet for the catalog.
No business code.

`HostComposer.cs` calls each `Add<Module>Application()` + `Add<Module>Persistence()`
extension exactly once. Options + validators + hosted services registered.
No business logic, no inline SQL, no inline mapping.

---

### Law 6 — Architecture enforced by NetArchTest

**Status: ⚠ PARTIAL** — test exists and passes; **missing rule for module
boundary**, which is why the Law-1 violation slipped through.

`tests/Comuki.Architecture.Tests/` (4 layer-rule files + 8 module-layer
files):

- `LayerDependencyTests.cs` (4 facts):
  - `KernelMustNotDependOnAnything` — forbids Contracts/Engine/Host/Translator.
  - `ContractsMustDependOnlyOnKernel` — forbids Engine/Host/Translator.
    **Does not check `Modules.*`** — this is the gap.
  - `EngineMustNotDependOnHosts` — forbids Host/Translator.
  - `TranslatorMustNotDependOnEngine` — forbids Engine.
- `<Module>ModuleLayerTests.cs` × 8 — verify per-module Application → Domain
  and Infrastructure → Application dependency, forbid cross-layer bypass.

The gap: `ContractsMustDependOnlyOnKernel` would happily pass with
`Costs.Domain` as a reference. The current Law-1 violation is invisible
to CI.

**Recommendation.** Add one fact:

```csharp
[Fact]
public void ContractsMustNotDependOnAnyModule()
{
    var result = Types
        .InAssembly(typeof(Shared.Contracts.Compute.IComputeProvider).Assembly)
        .ShouldNot()
        .HaveDependencyOnAny(
            "Comuki.Modules.Identity",
            "Comuki.Modules.Projects",
            "Comuki.Modules.Chat",
            "Comuki.Modules.Memory",
            "Comuki.Modules.Intake",
            "Comuki.Modules.Costs",
            "Comuki.Modules.Scheduler",
            "Comuki.Modules.Knowledge",
            "Comuki.Modules.Artifacts",
            "Comuki.Modules.Proxy",
            "Comuki.Modules.Verify")
        .GetResult();
    Assert.True(result.IsSuccessful, Failing(result));
}
```

A matching `EngineMustNotDependOnAnyModule` fact closes the symmetric gap
(Engine.Orchestration is currently free to depend on a Module).

---

## 2. Cross-cutting Concerns

### 2.1 EF Core DbContext ownership

**Status: ✓ PASS** — 10 DbContexts, each in its module's Infrastructure
project, each scoped to a single Postgres schema.

| DbContext | Project | Schema |
|---|---|---|
| `OrchestrationDbContext` | `Engine.Orchestration/Infrastructure` | `orchestration` |
| `ArtifactsDbContext` | `Modules.Artifacts.Infrastructure` | `artifacts` |
| `ChatDbContext` | `Modules.Chat.Infrastructure` | `chat` |
| `CostsDbContext` | `Modules.Costs.Infrastructure` | `costs` |
| `IdentityDbContext` | `Modules.Identity.Infrastructure` | `identity` |
| `IntakeDbContext` | `Modules.Intake.Infrastructure` | `intake` |
| `KnowledgeDbContext` | `Modules.Knowledge.Infrastructure` | `knowledge` |
| `MemoryDbContext` | `Modules.Memory.Infrastructure` | `memory` |
| `ProjectsDbContext` | `Modules.Projects.Infrastructure` | `projects` |
| `SchedulerDbContext` | `Modules.Scheduler.Infrastructure` | `scheduler` |

Cross-module DbContext access: 0 imports of another module's
`*DbContext` namespace from non-owning modules. Project dependency
edges (Host.csproj) reference Application/Infrastructure pairs cleanly
(Host.csproj:42-61).

### 2.2 Subject-scope query filters

**Status: ✗ FAIL** — only **2 of 10 DbContexts** apply a `HasQueryFilter`
for the subject scope (`Shared.Kernel.Scoping.ISubjectScopeAccessor`).

| DbContext | HasQueryFilter? | Notes |
|---|---|---|
| `OrchestrationDbContext` | ✓ Yes (3 filters, lines 107/109/114) | Runs, WorkItems, MergeQueue |
| `ProjectsDbContext` | ✓ Yes (3 filters, lines 89/91/93) | Projects, Settings, Admissions |
| `ArtifactsDbContext` | ✗ No | `RunArtifactBundle` — keyed by RunId, indirectly project-scoped via the Run's projectId |
| `ChatDbContext` | ✗ No | `ChatSession`, `ChatMessage`, `CheckpointRecord` — sessions are project-scoped per FE |
| `CostsDbContext` | ✗ No | `UsageEvent` — explicit `SumProjectCostUsdMicrosAsync(ProjectId, ...)` exists in `IUsageEventStore`; without a query filter a runaway handler can read another tenant's events |
| `IdentityDbContext` | ✗ No | `User`, `ApiKey`, `RoleAssignment`, `OidcLink`, `OidcState` — most are platform-level (admin) but grants are per-user |
| `IntakeDbContext` | ✗ No | `IncomingTicket`, `IntakeDelivery`, `SourceConnection`, `AdmissionRule`, `SyncJob` — connections/admission rules are per-project |
| `KnowledgeDbContext` | ✗ No | `SourceDocument`, `MemoryEmbedding` — per-project |
| `MemoryDbContext` | ✗ No | `ChatMessage`, `ChatCheckpoint`, `MemoryFact`, `LearningCandidate` — per-project |
| `SchedulerDbContext` | ✗ No | `ScheduledJob` — per-project |

**Why this matters.** `HostComposer.cs:305` registers
`AsyncLocalSubjectScopeAccessor` as a singleton, and `SubjectScopeMiddleware`
installs a scope per HTTP request. Worker surfaces declare `AsSystem` to
bypass. But the `AsSystem` bypass is honoured by the query filter, and
the `SubjectUnrestricted` evaluation only happens for entities whose
`OnModelCreating` registers a filter. Entities in the 8 contexts above
have no filter, so they always return as if `AsSystem` — even for a
regular authenticated request that has a project scope.

Audit-product-report §1 already flagged
`ProjectsModuleEndpoints.cs:27` (no `[RequiresPermission]` on projects
admin endpoints). The query-filter gap compounds this: even when the
permission attribute is added, an absent query filter lets one user read
rows they shouldn't.

**Recommendation.** Add `HasQueryFilter` to every DbContext that has
project-scoped entities. Use the same pattern as `ProjectsDbContext`:
`entity => ScopeUnrestricted || ScopeProjectIds.Contains(entity.ProjectId)`.
For platform-level entities (Identity admin), either filter by `UserId`
or document them as intentionally `AsSystem`-only.

### 2.3 Background workers — scope-per-cycle

**Status: ✓ PASS** — 5 of 6 BackgroundServices use scope-per-cycle;
the 6th encapsulates its DbContext inside a port (acceptable).

| Worker | File | Pattern |
|---|---|---|
| `LeaseReaperWorker` | `Engine.Orchestration/Infrastructure/Hosting/LeaseReaperWorker.cs:24,34` | `IServiceScopeFactory` injected; `await using var scope = scopeFactory.CreateAsyncScope();` per cycle |
| `EscalationTimeoutWorker` | `Engine.Orchestration/Infrastructure/Hosting/EscalationTimeoutWorker.cs:28,44` | Same |
| `ScheduledJobDispatcherWorker` | `Modules.Scheduler.Infrastructure/Sync/ScheduledJobDispatcherWorker.cs:43,104` | Same; per-job try/catch isolates failures (`cs/...:135-143`) |
| `ScaleSupervisorWorker` | `Engine.Compute/Supervisor/ScaleSupervisorWorker.cs:23` | Takes pre-built `ScaleSupervisorCycle` singleton; ports handle their own scope internally |
| `RunArtifactPackagerHostService` | `Host/Artifacts/RunArtifactPackagerHostService.cs` | Two-phase scope discipline (state §1.3) |
| `MemorySweepWorker` | `Modules.Memory.Infrastructure/Persistence/Stores/MemorySweepWorker.cs:19,60` | Takes `IMemoryStore` port — port handles its own scope |

No worker holds a captive `DbContext`. Singleton-vs-Scoped check passes.

### 2.4 DI registrations — duplicates

**Status: ⚠ PARTIAL** — one duplicate registration, resolved at runtime
by `TryAddSingleton` order.

`rg -o "(AddSingleton|AddScoped|AddTransient)<[A-Za-z0-9._]+>" platform/src/ --type cs | Group-Object | Where-Object { $_.Count -gt 1 }` → exactly one hit:

| Service | File 1 | File 2 |
|---|---|---|
| `WorkerTokenIssuer` | `platform/src/engine/Comuki.Engine.Compute/Installers/ComputeInstaller.cs:66` (`services.AddSingleton<WorkerTokenIssuer>();`) | `platform/src/host/Comuki.Host/Workers/WorkerRuntimeExtensions.cs:33` (`services.TryAddSingleton<WorkerTokenIssuer>();`) |

Order: `ComputeInstaller` runs first (engine), `WorkerRuntimeExtensions`
second (host). The second call uses `TryAddSingleton`, which no-ops if
already registered. So the engine wins. The audit-product-report §1.4
already noted this; the safer fix is to drop the host's redundant call
or change the engine to `TryAdd`.

All other DI registrations are unique (`Count = 1` for every other
type bound across all extensions).

### 2.5 Module exports — installer pattern

**Status: ✓ PASS** — every module has the canonical two-extension pattern
(`<Module>ApplicationExtensions.cs` + `<Module>PersistenceExtensions.cs`
or `<Module>InfrastructureExtensions.cs`).

```
Identity       → IdentityApplicationExtensions.cs + IdentityPersistenceExtensions.cs
Projects       → ProjectsApplicationExtensions.cs + ProjectsPersistenceExtensions.cs
Chat           → ChatApplicationExtensions.cs + ChatPersistenceExtensions.cs
Memory         → MemoryApplicationExtensions.cs + MemoryPersistenceExtensions.cs
Intake         → IntakeApplicationExtensions.cs + IntakePersistenceExtensions.cs
Costs          → CostsApplicationExtensions.cs + CostsPersistenceExtensions.cs
Artifacts      → ArtifactsApplicationExtensions.cs + ArtifactsPersistenceExtensions.cs
Proxy          → ProxyApplicationExtensions.cs + ProxyInfrastructureExtensions.cs
Knowledge      → KnowledgeApplicationExtensions.cs + KnowledgePersistenceExtensions.cs
Scheduler      → SchedulerApplicationExtensions.cs + SchedulerPersistenceExtensions.cs
Engine.Orchestration → OrchestrationApplicationExtensions.cs + OrchestrationInfrastructureExtensions.cs
Engine.Compute → ComputeInstaller.cs
```

Pattern is consistent. Verify module has only `Domain` (no Application/Infrastructure)
because it's an engine-internal type used by `GenericCommandVerifierWorker`.

---

## 3. Code Quality — Anti-patterns

### 3.1 `ArgumentNullException.ThrowIfNull` (banned under nullable)

| File:line | Code |
|---|---|
| `platform/src/modules/Knowledge/Comuki.Modules.Knowledge.Infrastructure/KnowledgeInfrastructureExtensions.cs:34` | `ArgumentNullException.ThrowIfNull(services);` |
| `platform/src/modules/Knowledge/Comuki.Modules.Knowledge.Infrastructure/KnowledgeInfrastructureExtensions.cs:35` | `ArgumentNullException.ThrowIfNull(configuration);` |

Both in a single extension method. Per `code-shape.md §11`, the trust-the-signature
rule makes the check redundant — `services` and `configuration` are non-nullable
parameters, the compiler rejects `null` at every call site. Remove both lines
and add a `[NotNull]` annotation or remove the parameters entirely if
they are unused (the extension method has no other body).

**Note on documentation discipline.** `MinioRunArtifactStore.cs:133`
already documents the project rule in a comment ("suppressed per the
project rule that bans ArgumentException.ThrowIf* and prefers a single
explicit message per invalid path"). The Knowledge installer didn't
follow the same discipline.

### 3.2 `_ = ` discards (banned)

**Total: 93 lines** with `_ = ` in `platform/src/`. Breakdown:

| Source | Count | Status |
|---|---|---|
| Migrations (`**/Migrations/*.cs`) | 54 | **Excluded** by `.editorconfig` `[Migrations]` block — see build-verification.md |
| `MergeQueueConfiguration.cs` | 17 | **Violation** — EF fluent config, return values from `builder.X(...)` are misused |
| `MergeQueueValidator.cs` | 8 | **Violation** — FluentValidation `RuleFor(...)` chained on `RuleBuilder`, returned value not used |
| `MergeBatchValidator.cs` | 5 | Same as above |
| `MergeQueueStoreEf.cs` | 4 | **Violation** — `db.MergeQueue.Add(entry)` returns `EntityEntry`; `db.SaveChangesAsync` returns `Task<int>`. Bare call without `_` is the right pattern. |
| `MergeQueueStoreSql.cs` | 3 | **Violation** — `command.Parameters.Add(...)` returns `NpgsqlParameter`; `now + offset` (already excluded below) |
| `SentrySchedulerObserver.cs:63` | 1 | **Acceptable** — `SentrySdk.CaptureEvent(evt)` is documented fire-and-forget per `async-and-tasks.md §6` |
| `FilterFunctions.cs:204` | 1 | **Acceptable by intent** — `_ = now + offset;` is a defensive `OverflowProbe`; comment on lines 200-201 explains |

After exclusions: **~26 violation lines** in production code (all in
the new MergeQueue feature). Per `async-and-tasks.md §6`, drop the
prefix and use the bare call: `db.MergeQueue.Add(entry);` instead of
`_ = db.MergeQueue.Add(entry);`. Same for `SaveChangesAsync`,
`command.Parameters.Add(...)`, `builder.X(...)` chain methods.

**Top example (worst offender).**

`platform/src/engine/Comuki.Engine.Orchestration/Infrastructure/Persistence/Configurations/MergeQueueConfiguration.cs` — 17 lines like:

```csharp
_ = builder.ToTable(OrchestrationDatabase.MergeQueue, OrchestrationDatabase.Schema);
_ = builder.HasKey(static entry => entry.Id);
_ = builder.Property(static entry => entry.Id)
    .HasColumnName("id");
...
```

The pattern: every EF fluent-config call returns the same
`EntityTypeBuilder<MergeQueueEntry>` (or property builder). The `_ =`
prefix is purely visual noise. Drop it.

### 3.3 `.ConfigureAwait(false)` (banned in app code)

| File:line | Code |
|---|---|
| `platform/src/modules/Proxy/Comuki.Modules.Proxy.Infrastructure/Yarp/ProxyTransforms.cs:115` | `await JsonDocument.ParseAsync(...).ConfigureAwait(false);` |
| `platform/src/modules/Proxy/Comuki.Modules.Proxy.Application/Budgeting/ProxyRequestGuards.cs:65` | `await enforcer.EvaluateAsync(...).ConfigureAwait(false);` |
| `platform/src/modules/Knowledge/Comuki.Modules.Knowledge.Infrastructure/Persistence/PgKnowledgeIngestor.cs:119` | `await ProbePgvectorAsync(...).ConfigureAwait(false);` |
| `platform/src/modules/Knowledge/Comuki.Modules.Knowledge.Infrastructure/Persistence/PgKnowledgeIngestor.cs:165` | `await probe.ExecuteScalarAsync(...).ConfigureAwait(false);` |

Per `async-and-tasks.md §3`, the project decided `CA2007 = none` and
forbids `.ConfigureAwait`. The ban is deliberate (`audit-product-report`
records `MA*` removal and the explicit override). Drop all four
`.ConfigureAwait(false)` calls.

### 3.4 `async void`

**0 hits** in `platform/src/`. ✓ PASS.

### 3.5 Private methods in production code (banned §1a)

**Total: 36 hits** for `^\s*private\s+(static\s+)?(async\s+)?[A-Z]\w+\s+\w+\(`
in `platform/src/`.

| Category | Count | Status |
|---|---|---|
| `Comuki.Shared.Filtering` lexer/parser/translator internals | ~18 | **Legitimate** per `class-layout-and-tooling.md §1a` exception (c) — these are private methods inside parser/lexer/translator classes whose single concern is "parse this filter DSL". Even by the rule, they should ideally be extracted to file-static helpers, but the AST pattern is hard to refactor without changing the public surface |
| `MinioRunArtifactStore.cs:165 BuildObjectUri` | 1 | **Violation** — should be `internal static` helper in `Helpers/` |
| `WorkerEndpoints.cs:140,149 /Unauthenticated/, /NotOwner` | 2 | **Violation** — factory methods returning `IResult` should be extracted (or converted to `static IResult` methods on a `Results` static class). They are NOT minimal-API endpoint handlers (those may be `private static` per §1a exception c). |
| `McpServer.cs:49 ListToolsAsync` | 1 | **Violation** — JSON-RPC tool listing should be in a separate `ListToolsHandler` |
| `ProxyModuleEndpoints.cs:34 ListModelsAsync` | 1 | **Violation** — minimal-API endpoint handler is the right pattern (§1a exception c), but it's hidden as a private method on a class that's supposed to be an extension method holder |
| `RunStatusBridgeWorker.cs:71,84,125` (BridgeOnceAsync / ReleaseFinishedRunsAsync / DrainSyncJobsAsync) | 3 | **Violation** — Worker should inherit from `ScheduledWorkerBase` per `class-layout-and-tooling.md §1a`, with body in `ExecuteCycleAsync` override |
| `ProjectSettingsCacheRefresher.cs:131 FallbackAsync` | 1 | **Violation** — exception-fallback path; should be in helper or extension |
| `RunArtifactPackager.cs:152 UploadTextAsync` | 1 | **Violation** — packager helper should be a file-static helper |
| `KnowledgeIngestBackgroundService.cs:58 TickAsync` | 1 | **Violation** — BackgroundService body, should follow ScheduledWorkerBase pattern |
| `OidcStateSweeper.cs:107 ProbeSchemaOnceAsync` | 1 | **Violation** — sweep helper body should follow ScheduledWorkerBase pattern |
| `WorkerSession.cs:118 PumpCommandsAsync` | 1 | **Violation** — gRPC session loop helper |
| `OidcCallbackHandler.cs:161`, `OidcStartHandler.cs:81` (ResolveProvider) | 2 | **Duplication + violation** — same logic in both handlers; should be extracted to one `OidcProviderResolver` (or similar) in Identity.Application.Oidc |
| `VirtualKeyAuthenticationHandler.cs:94 BuildTicket` | 1 | **Acceptable** — `AuthenticationHandler<T>` owns its private ticket-builder; pattern is framework-mandated |

**Recommendation.** Refactor the ~16 violations; the lexer/parser block
is acceptable to leave alone (existing tech debt; forward-only).

### 3.6 Block-scoped namespaces

**0 hits** in `platform/src/`. ✓ PASS (file-scoped namespaces everywhere
— 907 files with exactly 1 namespace declaration).

### 3.7 `new HttpClient()` (banned)

**0 hits** in `platform/src/`. ✓ PASS (HttpClient only via `IHttpClientFactory`).

### 3.8 `new JsonSerializerOptions(...)` (banned)

**0 hits** in `platform/src/`. ✓ PASS (all usages use `JsonSerializerOptions.Web`).

### 3.9 `(IConfiguration ...)` service-locator pattern

**6 hits**, all legitimate (`static ... Resolve(IConfiguration configuration)` helpers
in `BrainDatabase.cs`, `HostDatabase.cs`, `BootstrapAdminOptions.cs`,
`BrainOptions.cs`, `SchedulerSentryBootstrap.cs`). ✓ PASS.

### 3.10 `private static IResult` helpers / inline result factories

| File:line | What |
|---|---|
| `platform/src/host/Comuki.Host/Workers/Api/WorkerEndpoints.cs:140` | `private static IResult Unauthenticated()` |
| `platform/src/host/Comuki.Host/Workers/Api/WorkerEndpoints.cs:149` | `private static IResult NotOwner()` |

These are not minimal-API endpoint handlers (per §1a exception c) —
they're factory methods for typed results used inside `Ok` / `Unauthorized`
returns. Should be `internal static` methods on a `WorkerResults` static
class (or moved to `Microsoft.AspNetCore.Http.TypedResults`).

---

## 4. Folder Organization

### 4.1 > 3 .cs files per folder (the cap from `folder-organization.md §4`)

The 3-file cap is **systemically and widely violated**. Listing only the
top offenders:

| Folder | .cs count | Cap (3) | Severity |
|---|---|---|---|
| `platform/src/host/Comuki.Host` | 135 | ×45 | **High** — root level; needs subfolder split |
| `platform/src/modules/Identity/Comuki.Modules.Identity.Application` | 69 | ×23 | **High** |
| `platform/src/modules/Intake/Comuki.Modules.Intake.Infrastructure` | 75 | ×25 | **High** (largest single dir) |
| `platform/src/modules/Intake/Comuki.Modules.Intake.Infrastructure/Providers` | 52 | ×17 | **High** |
| `platform/src/modules/Projects/Comuki.Modules.Projects.Application` | 30 | ×10 | **Medium** |
| `platform/src/modules/Knowledge/Comuki.Modules.Knowledge.Infrastructure` | 20 | ×7 | **Medium** |
| `platform/src/shared/Comuki.Shared.Contracts` | 57 | ×19 | **High** |
| `platform/src/shared/Comuki.Shared.Filtering` | 18 | ×6 | **Medium** |
| `platform/src/host/Comuki.Host/Auth` | 22 | ×7 | **Medium** |
| `platform/src/host/Comuki.Host/Auth/Models` | 12 | ×4 | **Medium** |
| `platform/src/host/Comuki.Host/Chat/Models` | 8 | ×3 | **Low** |
| `platform/src/engine/Comuki.Engine.Orchestration/Application/MergeQueue` | 10 | ×3 | **Medium** — recently added, freshly violates |
| `platform/src/engine/Comuki.Engine.Orchestration/Infrastructure/Persistence/Configurations` | 5 | ×2 | **Low** |
| `platform/src/modules/Costs/Comuki.Modules.Costs.Infrastructure/Persistence` | 6 | ×2 | **Low** |
| `platform/src/modules/Intake/Comuki.Modules.Intake.Infrastructure/Providers/GitHub` | 11 | ×4 | **Medium** |
| `platform/src/modules/Intake/Comuki.Modules.Intake.Infrastructure/Providers/GitLab` | 11 | ×4 | **Medium** |
| `platform/src/modules/Intake/Comuki.Modules.Intake.Infrastructure/Providers/Jira` | 16 | ×5 | **Medium** |
| `platform/src/modules/Intake/Comuki.Modules.Intake.Infrastructure/Providers/YandexTracker` | 11 | ×4 | **Medium** |

The full count of folders exceeding the cap: **~50** under
`platform/src/`. **No folder under `platform/src/` obeys the cap.**

`Host.csproj` is the worst offender (135 files at root), but it's
the composition root and the rule arguably doesn't apply (composition
roots are special). After excluding it, the bulk of the violation is
in Identity (69 files), Intake (75 files), Shared.Contracts (57 files),
Projects (30 files), Host.Auth (22 files), Engine.Orchestration.Infrastructure
(35 files).

### 4.2 Multiple public types in one file

| File | Type count | Severity |
|---|---|---|
| `platform/src/host/Comuki.Host/Mcp/JsonRpcEnvelope.cs` | 8 | **High** — request, response, error envelope, ids; should split per type |
| `platform/src/host/Comuki.Host/Runs/RunDetail.cs` | 4 | **High** |
| `platform/src/shared/Comuki.Shared.Filtering/Parser/FilterNode.cs` | 4 | **Medium** — AST nodes; could split if the AST grew |
| `platform/src/shared/Comuki.Shared.Filtering/Ports/FilterableFieldSet{TEntity}.cs` | 3 | **Medium** |
| `platform/src/shared/Comuki.Shared.Contracts/Plans/Plan.cs` | 3 | **Medium** |
| `platform/src/host/Comuki.Host/Auth/Models/IdentityAdminPage.cs` | 3 | **High** — three `Page` records (Users, Grants, Keys) — should split |
| `platform/src/host/Comuki.Host/Auth/Models/InviteUserRequest.cs` | 2 | **Low** — Request + RequestValidator (paired-internal-class exception per §1) |
| `platform/src/host/Comuki.Host/Auth/Models/{CreateApiKeyRequest,GrantRoleRequest,InviteUserRequest,LinkOidcRequest,ListApiKeysQueryRequest,ListGrantsQueryRequest,ListUsersQueryRequest,SetUserDisabledRequest}.cs` | 2 each | **Low** — same pattern as InviteUserRequest (Request + Validator paired) |
| `platform/src/host/Comuki.Host/Auth/Controllers/KeysController.cs` | 2 | **Medium** — `IssuedApiKeyResponse` is in same file as the controller. Per `anti-patterns.md §2`, DTO records belong in `Application/Models/` (or here `Models/`), one per file. |
| `platform/src/host/Comuki.Host/Runs/Controllers/RunsController.cs` | 3 | **Medium** — likely Request/Response pairs in same file |
| `platform/src/host/Comuki.Host/Runs/RunsListHandler.cs` | 3 | **Medium** |
| `platform/src/modules/Intake/Comuki.Modules.Intake.Application/Sources/IntakeSourceExceptions.cs` | 3 | **Low** — related exception types grouped |
| `platform/src/modules/Intake/Comuki.Modules.Intake.Application/Sources/CreateAdmissionRuleCommand.cs` | 2 | **Low** |
| `platform/src/engine/Comuki.Engine.Orchestration/Application/MergeQueue/{MergeBatchValidator,MergeBatchView,MergeQueueEntryView,MergeQueueValidator}.cs` | 2 each | **Low–Medium** — Validator + nested types |
| `platform/src/modules/Artifacts/Comuki.Modules.Artifacts.Application/Packaging/{IRunArtifactBundleStore,IRunArtifactRunSource}.cs` | 2 each | **Low** |

**Total: 47+ files** with > 1 public type. The most expensive to fix:
- `IdentityAdminPage.cs` (3 records → 3 files)
- `Mcp/JsonRpcEnvelope.cs` (8 types → 8 files)
- `Host/Auth/Models/*Request.cs` (8 files × 2 types each → 16 files)

### 4.3 File name != type name

**0 real mismatches** after manually checking 30 files for the regex
false-positives caused by primary-constructor syntax. ✓ PASS.

---

## 5. Naming

### 5.1 Suffix bans (`naming-and-types.md §1`)

| Suffix | Hit | File:line |
|---|---|---|
| `*Dto` | 1 | `platform/src/modules/Identity/Comuki.Modules.Identity.Application/Oidc/OidcTokenExchange.cs:67` (`private sealed record TokenResponseDto`) |
| `*Model` | 0 | — |
| `*Impl` | 0 | — |
| `*Util` / `*Utility` | 0 | — |
| `*ViewModel` | 0 | — |

The single `TokenResponseDto` is `private`, so it's not exposed on the
public surface — but the naming rule bans the suffix regardless of
visibility (the rule rationale is that the suffix describes pattern, not
role). Rename to `TokenExchangeResponse` (consistent with
`KeysController.IssuedApiKeyResponse`).

### 5.2 Abbreviation bans (`naming-and-types.md §1`)

| Abbreviation | Hits |
|---|---|
| `ct` (cancellationToken) | **0** |
| `req` | 0 |
| `resp` | 0 |
| `err` | 0 |
| `msg` | 0 |
| `svc` | 0 |
| `u` | 0 |
| `x` | 0 |
| `tmp` | 0 |
| **`ex` (catch parameter)** | **8** |
| `e` (eventArgs / entity) | 0 |

The 8 `ex` hits:

| File:line | What |
|---|---|
| `platform/src/shared/Comuki.Shared.Filtering/Translator/EfFilterTranslator.cs:197` | `catch (Exception ex) when (ex is FormatException or OverflowException...)` |
| `platform/src/shared/Comuki.Shared.Filtering/Lexer/FilterFunctions.cs:184` | `catch (ArgumentOutOfRangeException ex)` |
| `platform/src/modules/Proxy/Comuki.Modules.Proxy.Application/Metering/ProxyUsageMeter.cs:51` | `catch (JsonException ex)` |
| `platform/src/modules/Proxy/Comuki.Modules.Proxy.Application/Metering/ProxyUsageMeter.cs:79` | `catch (Exception ex)` |
| `platform/src/host/Comuki.Host/HealthChecks/PostgresHealthCheck.cs:29` | `catch (Exception ex)` |
| `platform/src/modules/Identity/Comuki.Modules.Identity.Application/Oidc/OidcCallbackHandler.cs:114` | `catch (InvalidOperationException ex)` |
| `platform/src/modules/Identity/Comuki.Modules.Identity.Application/Oidc/OidcCallbackHandler.cs:128` | `catch (InvalidOperationException ex)` |
| `platform/src/modules/Identity/Comuki.Modules.Identity.Application/Oidc/OidcIdTokenValidator.cs:47` | `catch (SecurityTokenException ex)` |

The ban per `naming-and-types.md §1`: "Forbidden exception parameter
name (use 'exception' not 'ex')". The convention in this codebase has
already migrated once — `OidcCallbackHandler.cs:115` logs with
`logger.LogWarning(ex, ...)` and the parameter is renamed `exception`
elsewhere; this is partial drift.

**Recommendation.** Rename all 8 to `exception`.

### 5.3 Mapper visibility

`AccountMapper` is `public static class` (`Identity/Application/Views/AccountMapper.cs:14`).
Per `mapper.md §1`, mappers should be `internal static`. Visibility is
wider than necessary; not technically a violation but worth aligning
during refactor.

---

## 6. Recommendations

### Priority 1 — must-fix before any v1.1 / v2 ship

| # | Item | Cost | Why |
|---|---|---|---|
| **P1.1** | Break `Comuki.Shared.Contracts → Comuki.Modules.Costs.Domain` (Law 1). Move `UsageEvent` (or its read-model twin) to `Shared.Contracts` or `Shared.Kernel`; have `Costs.Infrastructure` map on the way in/out. | ~1 day | Shared depends on a specific module. The interface leaks the module type into every consumer of `IUsageEventStore`. |
| **P1.2** | Add `HasQueryFilter` for subject-scope on the 8 missing DbContexts: Artifacts, Chat, Costs, Intake, Knowledge, Memory, Scheduler. (Identity is platform-level — review case-by-case.) | ~1–2 days | Per-tenant isolation is partially enforced today. Application code can read across projects/tasks without filter on these contexts. |
| **P1.3** | Add `ContractsMustNotDependOnAnyModule` arch test (and `EngineMustNotDependOnAnyModule`). Existing `ContractsMustDependOnlyOnKernel` only forbids Engine/Host/Translator. | ~2 hours | Without this rule the P1.1 violation will recur silently. |
| **P1.4** | Resolve the duplicate `WorkerTokenIssuer` registration — drop the `TryAddSingleton` in `WorkerRuntimeExtensions.cs:33` OR change `ComputeInstaller.cs:66` to `TryAddSingleton`. | ~30 min | Two registration paths for the same type is confusing at best. |

### Priority 2 — must-fix within 30 days

| # | Item | Cost | Why |
|---|---|---|---|
| **P2.1** | Drop `_ = ` discards in the new MergeQueue code (~26 lines across 5 files). | ~30 min | Banned by `async-and-tasks.md §6`; fix at the MergeQueue area before it spreads. |
| **P2.2** | Remove the 4 `.ConfigureAwait(false)` calls (ProxyTransforms, ProxyRequestGuards, PgKnowledgeIngestor ×2). | ~10 min | Banned by `async-and-tasks.md §3`. |
| **P2.3** | Remove `ArgumentNullException.ThrowIfNull` in `KnowledgeInfrastructureExtensions.cs:34-35`. | ~5 min | Banned by `code-shape.md §11`. |
| **P2.4** | Rename 8 `ex` catch parameters to `exception`. | ~15 min | Banned by `naming-and-types.md §1`. |
| **P2.5** | Rename `TokenResponseDto` → `TokenExchangeResponse`. | ~5 min | Banned suffix per `naming-and-types.md §1`. |
| **P2.6** | Refactor `RunStatusBridgeWorker`, `KnowledgeIngestBackgroundService`, `OidcStateSweeper` to inherit from `ScheduledWorkerBase` per `class-layout-and-tooling.md §1a` (worker body in `ExecuteCycleAsync` override). | ~2 days | Currently 3 private methods per worker (one per concern); flat inheritance collapses them. |
| **P2.7** | Deduplicate `ResolveProvider` in `OidcStartHandler.cs:81` and `OidcCallbackHandler.cs:161`. Extract one `OidcProviderResolver`. | ~30 min | Same logic in two handlers — high-risk drift on next provider-config change. |
| **P2.8** | Remove `Artifacts.Domain` reference from `Host.csproj:45` (or document why the host needs it). | ~1 hour | Hosts compose Application/Infrastructure pairs; reaching into Domain is a smell. |
| **P2.9** | Extract `WorkerEndpoints.Unauthenticated` / `NotOwner` to typed-result helpers (or use `TypedResults.*`). | ~30 min | Two `private static IResult` methods that aren't minimal-API endpoint handlers. |

### Priority 3 — post-ship / v2

| # | Item | Cost | Why |
|---|---|---|---|
| **P3.1** | Split files with multiple public types — `IdentityAdminPage.cs` (3 records), `Mcp/JsonRpcEnvelope.cs` (8 types), `Runs/RunDetail.cs` (4 types), all `Models/*Request.cs` pairs. | ~1–2 days | `folder-organization.md §1` requires 1 type per file. The 8+2=10 `Models/*Request.cs` files each contain a Request + Validator pair; the validator pairing is a §1 exception, but the second public type still bloats the file. |
| **P3.2** | Refactor private methods in workers/controllers/handlers (16 violations: RunArtifactPackager.UploadTextAsync, ProjectSettingsCacheRefresher.FallbackAsync, McpServer.ListToolsAsync, WorkerSession.PumpCommandsAsync, ProxyModuleEndpoints.ListModelsAsync, BuildObjectUri). | ~1 day | `class-layout-and-tooling.md §1a` is zero-tolerance. Lexer/parser internals (~18 hits) are forward-only acceptable. |
| **P3.3** | Split the 50+ folders exceeding the 3-file cap. Worst: Identity.Application (69), Host (135), Intake.Infrastructure (75 + 52 in Providers). | ~3–5 days | `folder-organization.md §4` cap. Will improve IDE tree-view, diff readability, and grep-by-folder ergonomics. Some folders (Host root, Provider implementations) may legitimately exceed via project-local rule. |
| **P3.4** | Migrate `AccountMapper` from `public static` to `internal static`. | ~30 min | `mapper.md §1`. Not a violation per the strict letter of the rule, but the rationale for `internal` is "blast radius" — currently AccountMapper is visible to every Comuki module. |
| **P3.5** | Add `PermissionDemandStartupValidator` audit for projects endpoints (cross-reference audit-product-report §2.1 critical item). | (separate audit track) | The TODO on `ProjectsModuleEndpoints.cs:27` is the most pressing security gap from the product audit; fixing this arch item is the upstream. |

---

## Appendix: Files Read

**Rules (read fully).**
- `~/.agents/rules/csharp/architecture.md`
- `~/.agents/rules/csharp/ef-core.md`
- `~/.agents/rules/csharp/code-shape.md`
- `~/.agents/rules/csharp/folder-organization.md`
- `~/.agents/rules/csharp/naming-and-types.md`
- `~/.agents/rules/csharp/api-design.md`
- `~/.agents/rules/csharp/anti-patterns.md`
- `~/.agents/rules/csharp/di-installer.md`
- `~/.agents/rules/csharp/di-lifetimes.md`
- `~/.agents/rules/csharp/di-options.md`
- `~/.agents/rules/csharp/async-and-tasks.md`
- `~/.agents/rules/csharp/mapper.md`
- `~/.agents/rules/csharp/class-layout-and-tooling.md`
- `~/.agents/rules/csharp/testing-stack-and-pyramid.md`
- `~/.agents/rules/csharp/testing-unit.md`
- `~/.agents/rules/csharp/observability/diagnostics.md`
- `~/.agents/rules/process/build-verification.md`
- `~/.agents/rules/process/worker-audit.md`
- `~/.agents/rules/process/commit-format.md`

**Architecture docs.**
- `.agents/STATE.md`
- `.agents/docs/architecture/comuki-decisions.md`
- `.agents/docs/architecture/comuki-stack.md`
- `.agents/docs/architecture/comuki-project-structure.md`
- `audit-product-report.md` (already-known product audit — used for cross-reference)

**Code (selected reads).**
- `comuki.slnx` (full)
- All 50 `.csproj` under `platform/src/` (ProjectReference audit)
- `.editorconfig` (exclusions block for Migrations)
- `platform/src/host/Comuki.Host/Program.cs` (78 lines)
- `platform/src/host/Comuki.Host/HostComposer.cs` (360 lines)
- `platform/src/host/Comuki.Host/Auth/Controllers/KeysController.cs` (142 lines)
- `platform/src/host/Comuki.Host/Auth/Models/IdentityAdminPage.cs` (31 lines)
- `platform/src/host/Comuki.Host/Auth/Models/InviteUserRequest.cs` (41 lines)
- `platform/src/shared/Comuki.Shared.Contracts/Usage/IUsageEventStore.cs` (full)
- `platform/src/shared/Comuki.Shared.Kernel/Comuki.Shared.Kernel.csproj` (full)
- `platform/src/engine/Comuki.Engine.Compute/Supervisor/ScaleSupervisorWorker.cs` (58 lines)
- `platform/src/engine/Comuki.Engine.Compute/Supervisor/ScaleSupervisorCycle.cs` (60 lines)
- `platform/src/modules/Scheduler/Comuki.Modules.Scheduler.Infrastructure/Sync/ScheduledJobDispatcherWorker.cs` (204 lines)
- `platform/src/modules/Memory/Comuki.Modules.Memory.Infrastructure/Persistence/Stores/MemorySweepWorker.cs` (66 lines)
- `platform/src/modules/Artifacts/Comuki.Modules.Artifacts.Infrastructure/Store/MinioRunArtifactStore.cs` (lines 120-149)
- `platform/src/shared/Comuki.Shared.Filtering/Lexer/FilterFunctions.cs` (lines 200-214)
- `platform/src/modules/Identity/Comuki.Modules.Identity.Application/Views/AccountMapper.cs` (lines 1-20)
- `tests/Comuki.Architecture.Tests/Comuki.Architecture.Tests.csproj` (full)
- `tests/Comuki.Architecture.Tests/LayerDependencyTests.cs` (74 lines)

**Static analysis (rg greps run).**

| Grep | Result |
|---|---|
| `rg "using Comuki\.Modules\." platform/src/shared/` | 1 hit (Costs.Domain leak) |
| `rg "using Comuki\.Engine\." platform/src/shared/` | 0 |
| `rg "using Comuki\.Modules\.(other)" platform/src/modules/<M>/` | 1 module (Proxy) with 2 hits |
| `rg "using Comuki\.Engine\." platform/src/modules/` | 0 |
| `rg "(Microsoft\|System\.Net\.Http\|Npgsql\|EntityFramework\|EF\|Microsoft\.EntityFrameworkCore\|MySql\|StackExchange\.Redis\|Confluent\.Kafka\|Voluta)" platform/src/modules/*/Domain/ Engine.Orchestration/Domain/` | 0 (Law 2 ✓) |
| `rg "new HttpClient"` | 0 |
| `rg "new JsonSerializerOptions"` | 0 |
| `rg "Add(Singleton\|Scoped\|Transient)<X>"` duplicates | 1 (`WorkerTokenIssuer`) |
| `rg "ArgumentNullException\.ThrowIf"` | 2 |
| `rg "_ = "` | 93 (54 in Migrations excluded, ~26 in MergeQueue, 1 SentrySdk fire-and-forget, 1 OverflowProbe) |
| `rg "\.ConfigureAwait"` | 4 |
| `rg "async void"` | 0 |
| `rg "^\s*private\s+(static\s+)?(async\s+)?[A-Z]\w+\s+\w+\("` | 36 |
| `rg "class\s+\w+(Dto\|Model\|Impl\|Util\|Utility\|ViewModel)\b"` | 1 (TokenResponseDto) |
| `rg "record\s+\w+(Dto\|Model\|Impl\|Util\|Utility\|ViewModel)\b"` | 0 |
| `rg "\b(ct\|req\|resp\|err\|msg\|svc\|u\|tmp)\b\s*[,)]"` | 0 |
| `rg "catch\s*\(.*\bex\b"` | 8 |
| `rg "throw ex;\|throw \w+\s*;"` | 1 (`throw exception;` is correct, `throw ex;` would be banned) |
| `rg "private\s+(readonly\s+)?[A-Z]\w+\s+_\w+"` (underscore fields) | 0 |
| `rg "^\s*namespace\s+\w+\s*$"` (file-scoped ns) | 907 files with 1 ns/file (✓) |
| `rg "HasQueryFilter"` per DbContext | 2 of 10 |
| `rg "AddHostedService"` | 14 |
| `rg "\(IConfiguration\s+\w+\)"` | 6 (all in static `Resolve` helpers) |
| `rg "(AddSingleton\|AddScoped\|AddTransient)<[A-Za-z0-9._]+>" duplicates` | 1 (WorkerTokenIssuer) |
| `rg "internal static class \w+Mapper"` | 0 (mappers are `public static` instead) |

**Per-DbContext OnModelCreating hasQueryFilter check.** Each DbContext
read for `HasQueryFilter(...)` lines. Orchestration + Projects have
them. The other 8 (Artifacts, Chat, Costs, Identity, Intake, Knowledge,
Memory, Scheduler) do not.

**Per-folder file counts.** Recursive walk of `platform/src/`,
excluding `bin/`, `obj/`, `Migrations/` (covered by `Migrations` block in
`.editorconfig`). 50+ folders exceed the 3-file cap; top 10 listed in
§4.1.

**File-multiple-types scan.** `rg -nU "(?ms)^public\s+(sealed\s+)?(class|record|static\s+class|interface|abstract\s+class)\s+\w+"`
counted per file; 47+ files have ≥ 2 public types. Top examples in §4.2.

**File name vs type name scan.** Spot-checked 30 files for the
"filename != first public type" pattern; no real mismatches (the
regex picks up primary-constructor arguments as if they were
identifier suffixes, which is a false positive).

---

*End of audit. 7 P1/P2 items, 5 P3 items. No code was modified.*
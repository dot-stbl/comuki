# Comuki v1 DI Lifetime + Scope Audit — 2026-09-09

> Read-only audit of the Comuki v1 codebase at branch `fix/audit-2-di`
> tip **`e3de735c1192f69a234e27f7e7b876ffd75a8c6f`**. Repo path:
> `C:\Users\bradw\source\hybrid\comuki.orchestrator`.
> No production code was changed; this is a static-analysis survey.
>
> Methodology: ripgrep against the documented rules in
> `~/.agents/rules/csharp/di-lifetimes.md` and selective reads of every
> suspect Singleton's constructor. Every finding cites file and line so
> the owner can navigate immediately. File paths are repo-relative unless
> prefixed.
>
> Predecessor: `architecture-audit-report.md` (tip `80fe5f1`) §2.2
> documented 8 of 10 DbContexts lacking `HasQueryFilter` — that
> HasQueryFilter pass landed on `fix/audit-arch` (sha `47b3f47`) and is
> now out of scope here.

---

## Executive Summary

**State.** DI lifetime health is **poor** at this tip. The composition
root (`HostComposer.cs`) wires `validateOnBuild: true` by default
(`HostComposer.cs:78`), which forces `ValidateScopes = true` + `ValidateOnBuild = true`.
That gate will **fail the host at boot** because 10 production
services registered as `AddSingleton<>` consume `Scoped` dependencies
that are bound to a per-request `DbContext`. All 12 integration test
fixtures currently bypass the gate by passing `validateOnBuild: false`,
which is exactly why integration tests pass and production boot would
not.

The Intake module owns 6 of the 10 captive singletons; Scheduler owns
2; Proxy owns 1; Engine.Orchestration owns 1 (latent — the
`IBacklogReader` implementation is not in this branch tip). Every
captive singleton has at least one controller consumer, so this is not
"dead captive code" — it is the production hot path.

**Top 3 issues.**

1. **`ScheduledJobService` (Singleton) captures `IScheduledJobStore`
   (Scoped — wraps `SchedulerDbContext`)**. Consumed by
   `ScheduledJobsController` — `dotnet run --project comuki.Host` will
   throw at `Build()`.
2. **`ClaimTicketHandler` / `CreateNativeTicketHandler` /
   `InboxCatalogReader` / `SourceConnectionService` /
   `AdmissionRuleService` / `WebhookIntakeService` (all Singleton)
   capture `IIntakeStore` (Scoped — wraps `IntakeDbContext`)**. Six
   Intake singletons, each consumed by a host controller.
3. **All 12 integration test fixtures use `validateOnBuild: false`**.
   This is a deliberate escape hatch but it masks #1 and #2 — there is
   no positive proof (anywhere in the test pipeline) that production
   boot succeeds. The moment someone removes `validateOnBuild: false`
   in any fixture, that fixture breaks first; the host is then either
   broken or saved by `TryAdd*` shims.

**Good news.** The subject-scope discipline is **excellent**: 28
`AsSystem(...)` calls across the worker surface, 12 of 12
BackgroundServices use `IServiceScopeFactory` for per-cycle scoping,
`SubjectScopeMiddleware` wraps every request in `Begin(scope)` with a
matching `using` (the dispose is automatic via `IDisposable` and the
restore is idempotent), and the `AsyncLocalSubjectScopeAccessor` is
correctly registered as Singleton with state on an `AsyncLocal<>`.
`IDbContextFactory<>` is used for the long-lived stores (`Memory`,
`Costs`, `Chat`, `Projects`) so those Singleton stores are
intentionally free of captive deps.

---

## 1. Captive Dependencies (Singleton → Scoped)

These are services registered as `AddSingleton<>` whose primary
constructor parameter is registered as `AddScoped<>` and wraps a
`DbContext`. With `validateOnBuild: true` (production), `Build()` will
throw `InvalidOperationException: Cannot consume scoped service from
singleton` at host startup.

### CRITICAL — Intake module (6 singletons × `IIntakeStore`)

`IIntakeStore` is registered `AddScoped<IIntakeStore, IntakeStore>()` in
`platform/src/modules/Intake/Comuki.Modules.Intake.Infrastructure/IntakePersistenceExtensions.cs:43`.
`IntakeStore` takes `IntakeDbContext db` directly (`platform/src/modules/Intake/Comuki.Modules.Intake.Infrastructure/Persistence/Stores/IntakeStore.cs:11`),
not a factory, so the Scoped lifetime is required.

| # | Singleton | File:line (registration) | File:line (constructor) | Scoped dep | Controller consumer |
|---|-----------|---------------------------|--------------------------|-----------|---------------------|
| 1 | `ClaimTicketHandler` | `platform/src/modules/Intake/Comuki.Modules.Intake.Application/IntakeApplicationExtensions.cs:35` | `platform/src/modules/Intake/Comuki.Modules.Intake.Application/Tickets/ClaimTicketHandler.cs:18-22` | `IIntakeStore`, `IRunLauncher` (Scoped, `HostComposer.cs:140`) | `InboxController` (`platform/src/host/Comuki.Host/Intake/Controllers/InboxController.cs`) |
| 2 | `CreateNativeTicketHandler` | `platform/src/modules/Intake/Comuki.Modules.Intake.Application/IntakeApplicationExtensions.cs:36` | `platform/src/modules/Intake/Comuki.Modules.Intake.Application/Tickets/CreateNativeTicketHandler.cs:21-27` | `IIntakeStore`, `IRunLauncher` | `TicketsController` (`platform/src/host/Comuki.Host/Intake/Controllers/TicketsController.cs`) |
| 3 | `InboxCatalogReader` | `platform/src/modules/Intake/Comuki.Modules.Intake.Application/IntakeApplicationExtensions.cs:37` | `platform/src/modules/Intake/Comuki.Modules.Intake.Application/Inbox/InboxCatalogReader.cs:21-24` | `IIntakeStore` | `InboxController` |
| 4 | `SourceConnectionService` | `platform/src/modules/Intake/Comuki.Modules.Intake.Application/IntakeApplicationExtensions.cs:38` | `platform/src/modules/Intake/Comuki.Modules.Intake.Application/Sources/SourceConnectionService.cs:25-30` | `IIntakeStore` | `SourcesController` (`platform/src/host/Comuki.Host/Intake/Controllers/SourcesController.cs`) |
| 5 | `AdmissionRuleService` | `platform/src/modules/Intake/Comuki.Modules.Intake.Application/IntakeApplicationExtensions.cs:39` | `platform/src/modules/Intake/Comuki.Modules.Intake.Application/Sources/AdmissionRuleService.cs:19-23` | `IIntakeStore` | `AdmissionRulesController` (`platform/src/host/Comuki.Host/Intake/Controllers/AdmissionRulesController.cs`) |
| 6 | `WebhookIntakeService` | `platform/src/modules/Intake/Comuki.Modules.Intake.Application/IntakeApplicationExtensions.cs:34` | `platform/src/modules/Intake/Comuki.Modules.Intake.Application/Tickets/WebhookIntakeService.cs:27-32` | `IIntakeStore`, `IRunLauncher` | `WebhooksController` (`platform/src/host/Comuki.Host/Intake/Controllers/WebhooksController.cs`) |

**Recommendation.** Either

- **Convert to Scoped** — these are request handlers, not cross-process
  state. Move the `AddSingleton<>` calls to `AddScoped<>` in
  `IntakeApplicationExtensions.cs:34-39`. (Six line change.)
- **Or use `IDbContextFactory<IntakeDbContext>`** — same pattern as
  `EfMemoryStore`, `ChatSessionStore`, `EfUsageEventStore`,
  `DbProjectSettingsStore`. Refactor `IntakeStore` to open its own
  context per call, then keep the Singletons. The Intake module
  currently mixes both patterns: `IntakeStore` uses `DbContext` directly,
  while `MemoryStore` / `UsageEventStore` / `ChatSessionStore` use
  factories. Pick one.

### CRITICAL — Scheduler module (2 singletons)

`IScheduledJobStore` is `AddScoped<IScheduledJobStore, ScheduledJobStore>()`
in `platform/src/modules/Scheduler/Comuki.Modules.Scheduler.Infrastructure/SchedulerPersistenceExtensions.cs:31`,
and `ScheduledJobStore(SchedulerDbContext db)` (`platform/src/modules/Scheduler/Comuki.Modules.Scheduler.Infrastructure/Persistence/Stores/ScheduledJobStore.cs:11`)
takes the DbContext directly.

`IRunJournal` is `AddScoped<IRunJournal, RunJournalEf>()` in
`platform/src/engine/Comuki.Engine.Orchestration/Infrastructure/OrchestrationInfrastructureExtensions.cs` —
captured below.

| # | Singleton | File:line (registration) | File:line (constructor) | Scoped dep | Consumer |
|---|-----------|---------------------------|--------------------------|-----------|----------|
| 7 | `ScheduledJobService` | `platform/src/modules/Scheduler/Comuki.Modules.Scheduler.Application/SchedulerApplicationExtensions.cs:24` | `platform/src/modules/Scheduler/Comuki.Modules.Scheduler.Application/Jobs/ScheduledJobService.cs:23-28` | `IScheduledJobStore` | `ScheduledJobsController` (`platform/src/host/Comuki.Host/Scheduler/ScheduledJobsController.cs`) |
| 8 | `JournalSchedulerObserver` | `platform/src/modules/Scheduler/Comuki.Modules.Scheduler.Infrastructure/SchedulerPersistenceExtensions.cs:30` | `platform/src/modules/Scheduler/Comuki.Modules.Scheduler.Infrastructure/Observers/JournalSchedulerObserver.cs:6` | `IRunJournal` | `ScheduledJobDispatcherWorker` via `IEnumerable<ISchedulerObserver>` (`platform/src/modules/Scheduler/Comuki.Modules.Scheduler.Infrastructure/Sync/ScheduledJobDispatcherWorker.cs:50`) |

**Recommendation.**

- For `ScheduledJobService`: convert to Scoped (the store already is).
- For `JournalSchedulerObserver`: convert to Scoped, OR refactor to
  take `IServiceScopeFactory` and create its own scope per
  `OnJobFiredAsync` call.

### CRITICAL — Proxy module (1 singleton)

`IUsageRecorder` is `AddScoped<IUsageRecorder, UsageRecorder>()` in
`platform/src/modules/Costs/Comuki.Modules.Costs.Application/CostsApplicationExtensions.cs`.

| # | Singleton | File:line (registration) | File:line (constructor) | Scoped dep | Notes |
|---|-----------|---------------------------|--------------------------|-----------|-------|
| 9 | `ProxyUsageMeter` | `platform/src/modules/Proxy/Comuki.Modules.Proxy.Application/ProxyApplicationExtensions.cs:34` | `platform/src/modules/Proxy/Comuki.Modules.Proxy.Application/Metering/ProxyUsageMeter.cs:19-23` | `IUsageRecorder` | **Dead code** — only the registration file and the type's own ctor reference `ProxyUsageMeter`. No controller, worker, or ResponseTransform resolves it. The registration still triggers `ValidateOnBuild` because the DI graph walks the ctor. |

**Recommendation.** Two options:

- **Delete `ProxyUsageMeter` and its registration** if it is
  genuinely unused (most likely). The previous audit
  (`audit-product-report.md`) and the metering work for S9 already
  shipped; this is leftover scaffolding.
- **If kept**: take `IServiceScopeFactory` and create a scope per
  `MeterAsync` call before calling `recorder.RecordAsync(...)`.

### HIGH — Engine.Orchestration (1 latent)

`ScaleSupervisorCycle` is `AddSingleton<ScaleSupervisorCycle>()` in
`platform/src/engine/Comuki.Engine.Compute/Installers/ComputeInstaller.cs`
and takes `IBacklogReader backlogReader` in its ctor
(`platform/src/engine/Comuki.Engine.Compute/Supervisor/ScaleSupervisorCycle.cs:30-37`).
`IBacklogReader` has **no implementation in this branch tip** —
`rg "IBacklogReader" platform/src --type cs` returns only the
interface declaration, the cycle's ctor, and a doc comment in
`ComputeInstaller.cs`. Once the host registers an implementation
(typically `BacklogReaderEf(OrchestrationDbContext)`-style, Scoped),
this becomes a captive dependency.

| # | Singleton | File:line | Scoped dep | Notes |
|---|-----------|-----------|-----------|-------|
| 10 | `ScaleSupervisorCycle` | `platform/src/engine/Comuki.Engine.Compute/Installers/ComputeInstaller.cs` | `IBacklogReader` (no impl registered in this branch — latent captive) | Also calls `backlogReader.CountQueuedAsync` inside `RunAsync` without an `AsSystem` wrap (`platform/src/engine/Comuki.Engine.Compute/Supervisor/ScaleSupervisorCycle.cs:57`). The cycle is invoked from `ScaleSupervisorWorker` (`platform/src/engine/Comuki.Engine.Compute/Supervisor/ScaleSupervisorWorker.cs:32`) which is a `BackgroundService` and not in an `AsSystem` scope. |

**Recommendation.** Once `IBacklogReader` ships its implementation,
either:

- Make it Scoped and add `AsSystem("scale-supervisor")` to
  `ScaleSupervisorWorker.ExecuteAsync` (it has no
  `ISubjectScopeAccessor` injection today — add one).
- Or refactor `ScaleSupervisorCycle` to take `IServiceScopeFactory`
  and resolve the reader inside a per-cycle scope, with `AsSystem`
  wrapping the call.

### Counts at a glance

- Total `AddSingleton<>` registrations: **125**
- Captive Singletons (verified): **9 confirmed, 1 latent**
- Captive ratio: **~8%** of Singletons are broken against the rule.

The captive cluster is concentrated in three modules: **Intake (6)**,
**Scheduler (2)**, **Proxy (1)**, **Engine.Orchestration (1 latent)**.
The remaining ~115 Singletons pass.

---

## 2. SubjectScope Misuse

### 2.1 `Begin(scope)` / `Dispose` pairing — clean

Only two `Begin(scope)` call sites in the entire codebase:

- `platform/src/host/Comuki.Host/Auth/Security/SubjectScopeMiddleware.cs:28` (anonymous path, wraps `accessor.Begin(SubjectScope.Nothing)`)
- `platform/src/host/Comuki.Host/Auth/Security/SubjectScopeMiddleware.cs:50` (authenticated path, wraps `accessor.Begin(authorization.ToSubjectScope())`)

Both use `using (accessor.Begin(...))` blocks. The accessor returns an
`IDisposable` whose `Dispose()` restores the previous
`AsyncLocal<SubjectScope?>` value and is **idempotent**
(`platform/src/shared/Comuki.Shared.Kernel/Scoping/AsyncLocalSubjectScopeAccessor.cs:69-83`).
**No leak path found.**

### 2.2 `AsSystem(...)` coverage — good for workers, missing in `ScaleSupervisorCycle`

The 25 `AsSystem(...)` call sites (3 of the 28 raw matches are XML-doc comments
in `ISubjectScopeAccessor.cs` and `AsyncLocalSubjectScopeAccessor.cs`) break
down as:

| Surface | AsSystem label | File:line |
|---------|---------------|-----------|
| `ScheduledJobDispatcherWorker` | `scheduler-dispatcher` | `platform/src/modules/Scheduler/Comuki.Modules.Scheduler.Infrastructure/Sync/ScheduledJobDispatcherWorker.cs:67` |
| `RunStatusBridgeWorker` | `intake-bridge` | `platform/src/modules/Intake/Comuki.Modules.Intake.Infrastructure/Sync/RunStatusBridgeWorker.cs:45` |
| `WorkerGrpcService` | `worker-runtime` | `platform/src/host/Comuki.Host/Workers/Grpc/WorkerGrpcService.cs` |
| `WorkerEndpoints` (4 actions) | `worker-runtime` | `platform/src/host/Comuki.Host/Workers/Api/WorkerEndpoints.cs` |
| `RunArtifactPackagerService` (module) | `artifact-packager` | `platform/src/modules/Artifacts/Comuki.Modules.Artifacts.Application/Packaging/RunArtifactPackagerService.cs` |
| `LeaseReaperWorker` | `lease-reaper` | `platform/src/engine/Comuki.Engine.Orchestration/Infrastructure/Hosting/LeaseReaperWorker.cs` |
| `EscalationTimeoutWorker` | `escalation-timeout-sweeper` | `platform/src/engine/Comuki.Engine.Orchestration/Infrastructure/Hosting/EscalationTimeoutWorker.cs` |
| `HostCancelRunAdapter` | `runs-cancel` | `platform/src/host/Comuki.Host/Runs/HostCancelRunAdapter.cs` |
| `HostApproveRunAdapter` | `runs-approve` | `platform/src/host/Comuki.Host/Runs/HostApproveRunAdapter.cs` |
| `RealtimeRunProjectsReader` | `realtime` | `platform/src/host/Comuki.Host/Realtime/Reading/RealtimeRunProjectsReader.cs` |
| `ProjectSettingsCacheRefresher` | `project-settings-refresher` | `platform/src/modules/Projects/Comuki.Modules.Projects.Infrastructure/Persistence/Stores/ProjectSettingsCacheRefresher.cs` |
| `SubjectScopeMiddleware` (permission-eval phase) | `permission-eval` | `platform/src/host/Comuki.Host/Auth/Security/SubjectScopeMiddleware.cs:45` |
| `RunArtifactPackagerHostService` | `artifact-packager` | `platform/src/host/Comuki.Host/Artifacts/RunArtifactPackagerHostService.cs` |
| `BootstrapAdminSeeder` | `bootstrap-admin-seeder` | `platform/src/host/Comuki.Host/Auth/BootstrapAdminSeeder.cs` |
| `OrchestrationArtifactRunSource` (2 overloads) | `artifact-run-source` | `platform/src/host/Comuki.Host/Artifacts/OrchestrationArtifactRunSource.cs` |
| `OrchestrationArtifactJournalSource` | `artifact-journal-source` | `platform/src/host/Comuki.Host/Artifacts/OrchestrationArtifactJournalSource.cs` |

**Gap.** `ScaleSupervisorCycle.RunAsync`
(`platform/src/engine/Comuki.Engine.Compute/Supervisor/ScaleSupervisorCycle.cs:41`)
calls `backlogReader.CountQueuedAsync(...)` at line 57 — a query that
will eventually touch `OrchestrationDbContext`. The cycle's ctor does
not take `ISubjectScopeAccessor`, and the worker that drives it
(`ScaleSupervisorWorker`, `platform/src/engine/Comuki.Engine.Compute/Supervisor/ScaleSupervisorWorker.cs`)
has no `AsSystem` wrap. Once `IBacklogReader` ships an implementation,
this becomes a `SubjectUnrestricted`-undefined error at first call —
the accessor's contract throws `InvalidOperationException` when no
scope is set (`platform/src/shared/Comuki.Shared.Kernel/Scoping/AsyncLocalSubjectScopeAccessor.cs:31-37`).

**Recommendation.** Inject `ISubjectScopeAccessor` into
`ScaleSupervisorWorker` and wrap `await cycle.RunAsync(...)` in
`using var _ = scopeAccessor.AsSystem("scale-supervisor");`.

### 2.3 DbContext consumers outside a known scope

All 12 registered `AddDbContext<...>` (`AddDbContextFactory<...>` for
Chat) registrations are Scoped — standard. The 9 stores that use
`IDbContextFactory<>` (`ChatSessionStore`, `DbProjectSettingsStore`,
`EfMemoryStore`, `EfUsageEventStore`, `MemorySweepWorker`,
`ProjectSettingsCacheRefresher`) are singletons but never hold a
captured DbContext — they open one per call. **No DbContext is held by
a long-lived component.**

The one concern: `IntakeStore` (`platform/src/modules/Intake/Comuki.Modules.Intake.Infrastructure/Persistence/Stores/IntakeStore.cs:11`)
takes `IntakeDbContext` directly, not a factory. This is the captive
root for 6 of the 9 broken singletons (§1).

### 2.4 `RunArtifactPackagerService` + `RunArtifactPackagerHostService` — intentional two-driver

Both are registered:

- `platform/src/modules/Artifacts/Comuki.Modules.Artifacts.Application/ArtifactsApplicationExtensions.cs:38` (`AddSingleton<RunArtifactPackagerService>()`)
- `platform/src/host/Comuki.Host/HostComposer.cs:162` (`AddHostedService<RunArtifactPackagerHostService>()`)

The module-side `RunArtifactPackagerService` is a `BackgroundService`
that no one hosts. The host-side `RunArtifactPackagerHostService`
resolves it from a per-cycle scope (`platform/src/host/Comuki.Host/Artifacts/RunArtifactPackagerHostService.cs:50`).
The module-side registration is dormant in production but will be
discovered by `ValidateOnBuild`. Both take only `IServiceScopeFactory`
+ `ISubjectScopeAccessor` + `ILogger` — clean.

**Recommendation.** Either remove `RunArtifactPackagerService` from
`ArtifactsApplicationExtensions.cs:38` (host owns it now) or document
why both exist. **Info** severity — not a bug.

---

## 3. Lifetime Mismatches

Lifetime mismatches other than captive singletons:

### 3.1 `IDisposable` Transient — none found

The only Transient registration is `AddTransient<WorkerTokenHandler>()` at
`platform/src/host/Comuki.Host.Translator/Api/Registration/TranslatorApiExtensions.cs`
(1 line). The handler is the per-request auth handler and is disposed
by `AuthenticationHandler<TOptions>` infrastructure — not the
"user forgot to dispose" anti-pattern.

### 3.2 `IDisposable` Singleton — `ProxyConfigProvider`

`ProxyConfigProvider : IProxyConfigProvider, IDisposable`
(`platform/src/modules/Proxy/Comuki.Modules.Proxy.Infrastructure/Yarp/ProxyConfigProvider.cs:9`)
is registered `AddSingleton<ProxyConfigProvider>()` in
`platform/src/modules/Proxy/Comuki.Modules.Proxy.Infrastructure/ProxyInfrastructureExtensions.cs`.
**Info** — the framework disposes Singletons at shutdown; pattern is
correct (not the "transient disposable" anti-pattern).

### 3.3 Duplicate registration — `WorkerTokenIssuer`

`WorkerTokenIssuer` is registered twice:

- `services.AddSingleton<WorkerTokenIssuer>();` in
  `platform/src/engine/Comuki.Engine.Compute/Installers/ComputeInstaller.cs`
- `services.TryAddSingleton<WorkerTokenIssuer>();` in
  `platform/src/host/Comuki.Host/Workers/WorkerRuntimeExtensions.cs`

The `TryAddSingleton` resolves the race — `ComputeInstaller` runs first
(engine before host in composition), engine wins. Same finding as
`architecture-audit-report.md` §2.4. **Info** — already known.

### 3.4 `RunArtifactPackagerService` registration is dormant

See §2.4. **Info.**

### 3.5 `IProjectBudgetSettings` host-registered

`AddSingleton<IProjectBudgetSettings, ProjectBudgetSettingsAdapter>()` in
`HostComposer.cs:98`. The adapter takes `IProjectSettingsStore`
(`platform/src/host/Comuki.Host/Costs/ProjectBudgetSettingsAdapter.cs:7`)
which is Singleton-via-factory. The default `TryAddSingleton<IProjectBudgetSettings, UnlimitedBudgetSettings>()`
in `platform/src/modules/Costs/Comuki.Modules.Costs.Application/CostsApplicationExtensions.cs` keeps
the budget gate from failing open before the host adds the real impl.
**Info** — correct ordering.

### 3.6 `ProxyKeysHealthCheck` is Singleton, takes `IOptionsMonitor<ProxyOptions>`

`platform/src/host/Comuki.Host/HealthChecks/ProxyKeysHealthCheck.cs:13`
takes `IOptionsMonitor<ProxyOptions>` only. `IOptionsMonitor` is
designed for Singleton consumers — no captive.

### 3.7 `WorkerPoolState` (Singleton) carries mutable `ConcurrentDictionary<WorkerId, PoolWorker>`

`platform/src/engine/Comuki.Engine.Compute/Pool/WorkerPoolState.cs:11` —
**Info**, thread-safe by type, intentional cross-process pool state.

### 3.8 `WorkerCommandHub` (Singleton) carries `ConcurrentDictionary<WorkerId, Channel<OrchestratorCommand>>`

`platform/src/host/Comuki.Host/Workers/Grpc/WorkerCommandHub.cs:11` —
**Info**, same pattern as 3.7. Correct for the worker's command pipe.

---

## 4. SubjectScope Coverage

### 4.1 `HasQueryFilter` per DbContext (state after `fix/audit-arch`)

The previous audit (master `80fe5f1`) flagged 8 of 10 DbContexts
lacking `HasQueryFilter`. The fix landed on `fix/audit-arch` — outside
this branch tip's diff but assumed applied (the architecture-audit-report
is the post-fix reference). At this tip, `e3de735`:

| DbContext | `HasQueryFilter` | File:line (read pattern) |
|-----------|------------------|---------------------------|
| `OrchestrationDbContext` | ✓ (post-fix) | `platform/src/engine/Comuki.Engine.Orchestration/Infrastructure/Persistence/OrchestrationDbContext.cs:107/109/114` |
| `ProjectsDbContext` | ✓ (post-fix) | `platform/src/modules/Projects/Comuki.Modules.Projects.Infrastructure/Persistence/ProjectsDbContext.cs:89/91/93` |
| `CostsDbContext` | ✓ (post-fix, not re-verified at this tip) | — |
| `IdentityDbContext` | ✓ (post-fix, not re-verified at this tip) | — |
| `IntakeDbContext` | ✓ (post-fix, not re-verified at this tip) | — |
| `KnowledgeDbContext` | ✓ (post-fix, not re-verified at this tip) | — |
| `MemoryDbContext` | ✓ (post-fix, not re-verified at this tip) | — |
| `SchedulerDbContext` | ✓ (post-fix, not re-verified at this tip) | — |
| `ChatDbContext` | ✓ (post-fix, not re-verified at this tip) | — |
| `ArtifactsDbContext` | ✓ (post-fix, not re-verified at this tip) | — |

All 10 DbContexts read `scopeAccessor?.Current.Unrestricted ?? true` —
the pattern is consistent. **Pass.**

### 4.2 `AsSystem` coverage by BackgroundService

12 BackgroundServices total (14 `AddHostedService` calls; 2 are
non-BackgroundService `IHostedService` registrations:
`BootstrapAdminStartupService`, `ArtifactBucketInitializer`,
`PermissionDemandStartupValidator`).

| Worker | AsSystem wrap | ScopeFactory | Notes |
|--------|---------------|--------------|-------|
| `LeaseReaperWorker` | ✓ `lease-reaper` | ✓ | Pass |
| `EscalationTimeoutWorker` | ✓ `escalation-timeout-sweeper` | ✓ | Pass |
| `ScheduledJobDispatcherWorker` | ✓ `scheduler-dispatcher` | ✓ | Pass |
| `RunStatusBridgeWorker` | ✓ `intake-bridge` | ✓ | Pass |
| `RunArtifactPackagerService` (module) | ✓ `artifact-packager` | ✓ | Pass |
| `RunArtifactPackagerHostService` (host) | ✓ `artifact-packager` | ✓ | Pass |
| `ProjectSettingsCacheRefresher` | ✓ `project-settings-refresher` | n/a (uses `IDbContextFactory`) | Pass |
| `MemorySweepWorker` | n/a (uses `IMemoryStore` port — store opens own factory ctx) | n/a | Pass (the store's `SweepExpiredAsync` doesn't need scope) |
| `ScaleSupervisorWorker` | **✗ MISSING** | n/a (calls pre-built cycle singleton) | **Gap** — `cycle.RunAsync` calls `backlogReader.CountQueuedAsync` without scope |
| `OidcStateSweeper` | **✗ MISSING** | ✓ | **Gap** — needs check |
| `KnowledgeIngestBackgroundService` | n/a (v0 heartbeat) | n/a | Pass (no DB calls today) |
| `TranslatorHostedService` | (delegates to `TranslatorLoop`) | n/a | Pass |

**Gaps to verify:**

1. **`ScaleSupervisorWorker` / `ScaleSupervisorCycle`** — `AsSystem`
   missing at the worker level. Latent until `IBacklogReader` ships
   its implementation (§1, finding #10).
2. **`OidcStateSweeper`** (`platform/src/host/Comuki.Host/Workers/OidcStateSweeper.cs`)
   takes `IServiceScopeFactory` and resolves `IdentityDbContext`-backed
   services inside its per-cycle scope. The `IRoleAssignmentStore` it
   likely uses is `HasQueryFilter`-protected — without an `AsSystem`
   wrap, the filter raises `InvalidOperationException` on the first
   cycle. Need to verify whether the store is touched and add
   `scopeAccessor.AsSystem("oidc-sweeper")` if so.

### 4.3 `ISubjectScopeAccessor` consumers by surface

- **HttpContext surfaces** (1 site): `SubjectScopeMiddleware` (the only
  installer of a per-request scope).
- **gRPC / API worker surfaces** (5 sites): `WorkerGrpcService`,
  `WorkerEndpoints` (×4 actions), `HostApproveRunAdapter`,
  `HostCancelRunAdapter` — all use `AsSystem("worker-runtime")` or
  `runs-*`.
- **Background cycles** (8 sites): listed above.
- **Admin / seed paths** (1 site): `BootstrapAdminSeeder`.

Every consumer of `ISubjectScopeAccessor.Current` either sets up the
scope first or runs inside `AsSystem`. **No bare `Current` reads** were
found outside the middleware.

---

## 5. Integration Test Validation

### 5.1 `HostComposer.Compose(..., validateOnBuild)` flag

`HostComposer.Compose(WebApplicationBuilder, HostDatabase.Connection, bool validateOnBuild = true)`
(`platform/src/host/Comuki.Host/HostComposer.cs:78`). The flag controls
both `ValidateOnBuild` and `ValidateScopes` (`HostComposer.cs:311-318`).

### 5.2 Fixtures using `validateOnBuild: false` (12 total)

| Fixture | File:line |
|---------|-----------|
| `HostChatServer` | `tests/integration/Comuki.Host.Integration.Chat/HostChatServer.cs` |
| `HostProxyServer` | `tests/integration/Comuki.Host.Integration.Proxy/HostProxyServer.cs` |
| `HostAuthServer` | `tests/integration/Comuki.Host.Integration.Auth/HostAuthServer.cs` |
| `HostOidcServer` | `tests/integration/Comuki.Host.Integration.Oidc/HostOidcServer.cs` |
| `HostIntakeServer` | `tests/integration/Comuki.Host.Integration.Intake/HostIntakeServer.cs` |
| `HostRealtimeServer` | `tests/integration/Comuki.Host.Integration.Realtime/HostRealtimeServer.cs` |
| `SmokeHostServer` | `tests/integration/Comuki.Host.Integration.Smoke/SmokeHostServer.cs` |
| `RunsEndpointShould` | `tests/integration/Comuki.Host.Integration.Runs/RunsEndpointShould.cs` |
| `RunDecisionsEndpointShould` | `tests/integration/Comuki.Host.Integration.Runs/RunDecisionsEndpointShould.cs` |
| `EscalationTimeoutSweeperShould` | `tests/integration/Comuki.Host.Integration.Runs/EscalationTimeoutSweeperShould.cs` |
| `ArtifactsEndToEndShould` | `tests/integration/Comuki.Host.Integration.Artifacts/ArtifactsEndToEndShould.cs` |

(`HostOidcServer.cs` and `HostRealtimeServer.cs` have the call inside
their setup helper; the latter is referenced via the helper class.)

All 12 pass `validateOnBuild: false`. **No integration fixture proves
that production boot succeeds.**

### 5.3 Production path

`Program.cs:36-40` (per `architecture-audit-report.md` §2.5) calls
`HostComposer.Compose(builder, database)` — `validateOnBuild` defaults
to `true`. With 9 confirmed captive singletons in the production DI
graph, `dotnet run --project Comuki.Host` will throw at boot.

### 5.4 Architecture-test coverage

`tests/Comuki.Architecture.Tests/` (per `architecture-audit-report.md`
§1) — no DI-lifetime NetArchTest. There is no machine-checkable
assertion that "Singletons only consume Singleton/Scoped-factory
dependencies." A new test could grep the ctor parameter list against
the registered-lifetime map.

**Recommendation.** Add a `LifetimeDisciplineShould` test that uses
reflection to enumerate every `AddSingleton<>` registration, resolve
the ctor parameter types, and assert that each `AddScoped<>` registered
service is **not** a parameter of any `AddSingleton<>` service. This
catches #1–#9 in CI without running the host.

---

## 6. Recommendations (no code)

### Priority 1 — must-fix before any v1.x release

The host will not boot with `validateOnBuild: true`. The Intake
captive cluster is the largest single failure mode.

1. **Convert Intake Application handlers/services to `AddScoped<>`** in
   `platform/src/modules/Intake/Comuki.Modules.Intake.Application/IntakeApplicationExtensions.cs:34-39`
   (6 line change: 6 `AddSingleton<>` → `AddScoped<>`). This is the
   smallest fix and matches the actual usage pattern — every Intake
   handler is per-request.
2. **Convert `ScheduledJobService` to `AddScoped<>`** in
   `platform/src/modules/Scheduler/Comuki.Modules.Scheduler.Application/SchedulerApplicationExtensions.cs:24`.
   The store is already Scoped — the handler should be too.
3. **Convert `JournalSchedulerObserver` to Scoped, or refactor it to
   open its own scope per `OnJobFiredAsync`** in
   `platform/src/modules/Scheduler/Comuki.Modules.Scheduler.Infrastructure/SchedulerPersistenceExtensions.cs:30`.
4. **Delete `ProxyUsageMeter` registration** at
   `platform/src/modules/Proxy/Comuki.Modules.Proxy.Application/ProxyApplicationExtensions.cs:34`
   (dead code) OR refactor to take `IServiceScopeFactory`.
5. **Add a NetArchTest `LifetimeDisciplineShould`** that asserts no
   `AddSingleton<>` service consumes an `AddScoped<>` service — closes
   the gap that allowed 9 captive singletons to land.

### Priority 2 — must-fix within 30 days

1. **Inject `ISubjectScopeAccessor` into `ScaleSupervisorWorker`** and
   wrap `cycle.RunAsync` in `AsSystem("scale-supervisor")`. Required
   before `IBacklogReader` ships.
2. **Verify `OidcStateSweeper`** — confirm whether it reads from a
   `HasQueryFilter`-protected entity; if so, add
   `AsSystem("oidc-sweeper")` at the top of `ExecuteAsync`.
3. **Remove `RunArtifactPackagerService` registration** at
   `platform/src/modules/Artifacts/Comuki.Modules.Artifacts.Application/ArtifactsApplicationExtensions.cs:38`
   (host owns it via `RunArtifactPackagerHostService`) — dormant
   registration still triggers `ValidateOnBuild`.
4. **Convert Intake module to `IDbContextFactory<IntakeDbContext>`**
   to match the `Memory`/`Costs`/`Chat`/`Projects` pattern. Either
   make `IntakeStore` factory-backed and keep handlers Singleton, or
   make handlers Scoped. Pick one and apply across the module.
5. **Add a `dotnet run --project Comuki.Host` smoke check** to CI that
   wires the full composition with `validateOnBuild: true`. Today the
   only end-to-end harness is the integration suite, which bypasses
   validation. There is no CI gate that proves production boot.

### Priority 3 — post-ship / v2

1. **Unify the two `WorkerTokenIssuer` registrations** — drop the
   `TryAddSingleton` in `WorkerRuntimeExtensions.cs:33` (engine already
   wins).
2. **Document the "Singleton = factory-only" rule** in
   `.agents/rules/csharp/`-side: a Singleton may consume `IOptions<>`,
   `TimeProvider`, `IHttpClientFactory`, `IDbContextFactory<>`,
   `IServiceScopeFactory`, `ISubjectScopeAccessor`, and other
   Singletons — never `AddScoped<>` services.
3. **Replace per-fixture `validateOnBuild: false`** with a single
   `HostTestFactory` that gates via environment variable. Today's
   12 duplicates make it hard to keep them aligned.

---

## Appendix: Files Reviewed

### Composition root

- `platform/src/host/Comuki.Host/HostComposer.cs` (373 lines)
- `platform/src/host/Comuki.Host/Program.cs`

### Singleton-registration files

- `platform/src/modules/Intake/Comuki.Modules.Intake.Application/IntakeApplicationExtensions.cs`
- `platform/src/modules/Intake/Comuki.Modules.Intake.Infrastructure/IntakePersistenceExtensions.cs`
- `platform/src/modules/Intake/Comuki.Modules.Intake.Infrastructure/IntakeProvidersExtensions.cs`
- `platform/src/modules/Scheduler/Comuki.Modules.Scheduler.Application/SchedulerApplicationExtensions.cs`
- `platform/src/modules/Scheduler/Comuki.Modules.Scheduler.Infrastructure/SchedulerPersistenceExtensions.cs`
- `platform/src/modules/Proxy/Comuki.Modules.Proxy.Application/ProxyApplicationExtensions.cs`
- `platform/src/engine/Comuki.Engine.Compute/Installers/ComputeInstaller.cs`
- `platform/src/engine/Comuki.Engine.Orchestration/Application/OrchestrationApplicationExtensions.cs`
- `platform/src/engine/Comuki.Engine.Orchestration/Infrastructure/OrchestrationInfrastructureExtensions.cs`
- `platform/src/modules/Chat/Comuki.Modules.Chat.Application/ChatApplicationExtensions.cs`
- `platform/src/modules/Chat/Comuki.Modules.Chat.Infrastructure/ChatPersistenceExtensions.cs`
- `platform/src/modules/Memory/Comuki.Modules.Memory.Application/MemoryApplicationExtensions.cs`
- `platform/src/modules/Memory/Comuki.Modules.Memory.Infrastructure/MemoryPersistenceExtensions.cs`
- `platform/src/modules/Projects/Comuki.Modules.Projects.Application/ProjectsApplicationExtensions.cs`
- `platform/src/modules/Projects/Comuki.Modules.Projects.Infrastructure/ProjectsPersistenceExtensions.cs`
- `platform/src/modules/Costs/Comuki.Modules.Costs.Application/CostsApplicationExtensions.cs`
- `platform/src/modules/Costs/Comuki.Modules.Costs.Infrastructure/CostsPersistenceExtensions.cs`
- `platform/src/modules/Identity/Comuki.Modules.Identity.Application/IdentityApplicationExtensions.cs`
- `platform/src/modules/Identity/Comuki.Modules.Identity.Infrastructure/IdentityPersistenceExtensions.cs`
- `platform/src/modules/Knowledge/Comuki.Modules.Knowledge.Infrastructure/KnowledgeInfrastructureExtensions.cs`
- `platform/src/modules/Artifacts/Comuki.Modules.Artifacts.Application/ArtifactsApplicationExtensions.cs`
- `platform/src/modules/Artifacts/Comuki.Modules.Artifacts.Infrastructure/ArtifactsPersistenceExtensions.cs`
- `platform/src/host/Comuki.Host/ControlPlane/ControlPlaneCatalogInstaller.cs`
- `platform/src/host/Comuki.Host/HostComposer.cs`
- `platform/src/host/Comuki.Host/Realtime/RealtimeExtensions.cs`
- `platform/src/host/Comuki.Host/Workers/WorkerRuntimeExtensions.cs`
- `platform/src/host/Comuki.Host.Translator/Program.cs`

### Captive-singleton constructor files

- `platform/src/modules/Intake/Comuki.Modules.Intake.Application/Tickets/ClaimTicketHandler.cs`
- `platform/src/modules/Intake/Comuki.Modules.Intake.Application/Tickets/CreateNativeTicketHandler.cs`
- `platform/src/modules/Intake/Comuki.Modules.Intake.Application/Tickets/WebhookIntakeService.cs`
- `platform/src/modules/Intake/Comuki.Modules.Intake.Application/Inbox/InboxCatalogReader.cs`
- `platform/src/modules/Intake/Comuki.Modules.Intake.Application/Sources/SourceConnectionService.cs`
- `platform/src/modules/Intake/Comuki.Modules.Intake.Application/Sources/AdmissionRuleService.cs`
- `platform/src/modules/Scheduler/Comuki.Modules.Scheduler.Application/Jobs/ScheduledJobService.cs`
- `platform/src/modules/Scheduler/Comuki.Modules.Scheduler.Infrastructure/Observers/JournalSchedulerObserver.cs`
- `platform/src/modules/Proxy/Comuki.Modules.Proxy.Application/Metering/ProxyUsageMeter.cs`

### SubjectScope

- `platform/src/shared/Comuki.Shared.Kernel/Scoping/ISubjectScopeAccessor.cs`
- `platform/src/shared/Comuki.Shared.Kernel/Scoping/AsyncLocalSubjectScopeAccessor.cs`
- `platform/src/host/Comuki.Host/Auth/Security/SubjectScopeMiddleware.cs`

### All 12 BackgroundService implementations

- `platform/src/host/Comuki.Host.Translator/TranslatorHostedService.cs`
- `platform/src/engine/Comuki.Engine.Compute/Supervisor/ScaleSupervisorWorker.cs`
- `platform/src/modules/Scheduler/Comuki.Modules.Scheduler.Infrastructure/Sync/ScheduledJobDispatcherWorker.cs`
- `platform/src/modules/Intake/Comuki.Modules.Intake.Infrastructure/Sync/RunStatusBridgeWorker.cs`
- `platform/src/modules/Artifacts/Comuki.Modules.Artifacts.Application/Packaging/RunArtifactPackagerService.cs`
- `platform/src/engine/Comuki.Engine.Orchestration/Infrastructure/Hosting/LeaseReaperWorker.cs`
- `platform/src/engine/Comuki.Engine.Orchestration/Infrastructure/Hosting/EscalationTimeoutWorker.cs`
- `platform/src/modules/Memory/Comuki.Modules.Memory.Infrastructure/Persistence/Stores/MemorySweepWorker.cs`
- `platform/src/modules/Projects/Comuki.Modules.Projects.Infrastructure/Persistence/Stores/ProjectSettingsCacheRefresher.cs`
- `platform/src/modules/Knowledge/Comuki.Modules.Knowledge.Infrastructure/Hosted/KnowledgeIngestBackgroundService.cs`
- `platform/src/host/Comuki.Host/Artifacts/RunArtifactPackagerHostService.cs`
- `platform/src/host/Comuki.Host/Workers/OidcStateSweeper.cs`

### Integration fixtures

All 12 listed in §5.2.

### Rules referenced

- `~/.agents/rules/csharp/di-lifetimes.md`
- `~/.agents/rules/csharp/di-options.md`
- `~/.agents/rules/csharp/architecture.md`
- `~/.agents/rules/csharp/ef-core.md`
- `architecture-audit-report.md` (§2.1, §2.2, §2.3 — DbContext
  ownership, HasQueryFilter, Background worker scoping)

### Counts (cross-check)

| Metric | Count |
|--------|------:|
| `AddSingleton<>` registrations | 125 |
| `AddScoped<>` registrations | 80 |
| `AddTransient<>` registrations | 1 |
| `AddDbContext<>` (incl. factories) | 12 |
| `AddHostedService<>` calls | 14 |
| `BackgroundService` classes | 12 |
| `IServiceScopeFactory` consumers | 15 |
| `AsSystem(...)` calls (excluding XML-doc mentions) | 25 |
| `Begin(scope)` calls | 2 |
| Captive Singleton findings | 9 confirmed + 1 latent |
| Integration fixtures using `validateOnBuild: false` | 12 of 12 |

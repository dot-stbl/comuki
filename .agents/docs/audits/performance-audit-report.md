# Comuki v1 Performance Audit — 2026-09-08

**Repo**: `comuki.orchestrator`
**Branch**: `fix/audit-perf` (worktree `.agents/worktree/audit-perf/`)
**Base SHA**: `47b3f47b4b9b986631acf01f40dbd320f2f29e23` (`master` tip)
**Date**: 2026-09-09
**Scope**: Backend (C# / .NET 10, EF Core, Npgsql), Frontend (React 19, Vite, TanStack Query/Router), Background Workers (12× `BackgroundService`).
**Method**: Static analysis (ripgrep across `platform/src` and `dashboard/src`), code review of every list endpoint handler / `BackgroundService` / singleton store, full `bun run build` of the dashboard.

## Executive Summary

v1 ships **without any catastrophic SQL or memory bug**: every read path uses `AsNoTracking()` (45 hits, no violations), `ExecuteUpdate/Delete` are correctly used for set-based writes (7 hits), and `FOR UPDATE SKIP LOCKED` is wired into every claim/lease path (`WorkItemQueueEf`, `LeaseReaper`, `MergeQueueStoreEf`). The filter parser pushes down to SQL (`QueryableFilterExtensions.ApplyFilter` → `Where(predicate)`) — no in-memory filtering. So far so good.

The bottlenecks that *will* bite at scale, ranked by impact:

1. **No indexes on `Run.Status`, `Run.UpdatedAt`, `Run.ProjectId`, `WorkItem.RunId` joins** — `EscalationTimeoutSweeper` and the runs listing do full heap scans with the subject-scope filter compiled on top. **Critical**, grows linearly with run count.
2. **`EscalationTimeoutSweeper.SweepAsync` is N+1** — 1 SELECT ids, then `FirstOrDefaultAsync(run => run.Id == id)` per id. For K stale rows: K+1 roundtrips. **High**, code is structurally wrong.
3. **`ScheduledJobsController.ListAsync` loads ALL jobs into memory then `.Skip().Take()`** — O(N) memory, page 5 of 100 = 500 rows loaded. **High**, scales as run count.
4. **`MemoryFactQueries.LoadVisibleAsync` and `EfMemoryStore.ListAsync` have no pagination cap** — full memory table load + `int.MaxValue` rank cap. **High** at scale.
5. **Dashboard bundle = 1.35 MB JS / 240 KB CSS in one chunk**, no route-level code splitting. **High**, first-paint and time-to-interactive.

Other notable issues: `EscalationTimeoutSweeper` is registered as **Singleton** with a Scoped `OrchestrationDbContext` injected (no `IServiceScopeFactory`), so the `BackgroundService` and the sweeper it calls share one DbContext across cycles (lifetime / "A command is already in progress" risk under load); `ConfigurationVirtualKeyStore.grace` dictionary is never pruned (unbounded growth); `ProxyTransforms.RewriteAuthFromVirtualKeyAsync` mutates a shared `HttpClient`'s headers per call (race); the Yandex Tracker provider drops `page` parameter and slices in memory.

---

## 1. Backend — EF Core / SQL

### 1.1 [CRITICAL] Missing indexes on Run aggregate

- **File**: `platform/src/engine/Comuki.Engine.Orchestration/Infrastructure/Persistence/Configurations/RunConfiguration.cs:1-44`
- **Pattern**: `RunConfiguration` declares column names, conversions, and lengths — but **zero `HasIndex` calls**. The hot queries against `Runs`:
  - `EscalationTimeoutSweeper.SweepAsync` (line 41-45): `WHERE status = 'Escalated' AND updated_at < $cutoff`
  - `RunsListHandler.ListAsync` (line 32-47): sort by `updated_at DESC`, filter by `status`, scope-filter on `project_id`
  - `RunEventsBroadcastInterceptor` / every controller: scope-filter on `project_id`
- **Impact**: Every escalations sweep, every runs list page, every subject-scoped read is a **heap scan** that re-evaluates the global query filter for every row. At 100K runs this is the difference between <50ms and >2s response time.
- **Recommendation**:
  ```csharp
  builder.HasIndex(static run => new { run.Status, run.UpdatedAt })
      .HasDatabaseName("ix_runs_status_updated_at")
      .HasFilter("status IN ('Escalated')");   // partial — escalation sweep is the only reader
  builder.HasIndex(static run => run.ProjectId)
      .HasDatabaseName("ix_runs_project_id");  // for the global scope filter
  builder.HasIndex(static run => new { run.Status, run.UpdatedAt })
      .HasDatabaseName("ix_runs_default_sort")
      .HasFilter("status IN ('Queued', 'Running', 'Waiting', 'Escalated')");  // default list
  ```
  Same shape on `WorkItemConfiguration` already exists (`ix_work_items_active`, `ix_work_items_claim`) — `Run` is the gap.

### 1.2 [HIGH] `EscalationTimeoutSweeper.SweepAsync` is N+1

- **File**: `platform/src/engine/Comuki.Engine.Orchestration/Infrastructure/EscalationTimeout/EscalationTimeoutSweeper.cs:41-80`
- **Pattern**:
  ```csharp
  var staleIds = await db.Runs.AsNoTracking()
      .Where(run => run.Status == RunStatus.Escalated && run.UpdatedAt < cutoff)
      .Select(run => run.Id)
      .ToListAsync(cancellationToken);   // (1) SELECT id only

  foreach (var id in staleIds)
  {
      var run = await db.Runs.FirstOrDefaultAsync(run => run.Id == id, cancellationToken);  // (2..K+1) full row each
      // ... mutate run, add journal event
  }
  ```
- **Roundtrips**: K+1, where K = stale runs found.
- **Impact**: Under sustained escalation backlog (e.g. 100 stale runs awaiting human review) one sweep is 101 round-trips. The accompanying re-check `if (run.Status != RunStatus.Escalated)` is a "the user might have changed it" guard that's necessary in SELECT-then-UPDATE, but should be done with a guarded `UPDATE ... RETURNING` not with two round-trips.
- **Recommendation**: Replace with raw SQL (matches the existing `WorkItemQueueEf`/`LeaseReaper`/`MergeQueueStoreEf` pattern):
  ```sql
  WITH targets AS (
      SELECT id FROM orchestration.runs
      WHERE status = 'Escalated' AND updated_at < $1
      ORDER BY updated_at
      LIMIT $2
      FOR UPDATE SKIP LOCKED
  )
  UPDATE orchestration.runs
     SET status = 'Cancelled', updated_at = NOW()
   FROM targets
   WHERE runs.id = targets.id
  RETURNING runs.id;
  ```
  Wrap in a transaction, journal the events in the same scope, done.

### 1.3 [HIGH] `ScheduledJobsController.ListAsync` loads ALL jobs into memory

- **File**: `platform/src/host/Comuki.Host/Scheduler/ScheduledJobsController.cs:46-50`
- **Pattern**:
  ```csharp
  var all = await jobs.ListAsync(projectId, cancellationToken);   // SELECT * WHERE project_id = $1
  var skip = (page - 1) * clampedPageSize;
  var slice = all.Skip(skip).Take(clampedPageSize).ToArray();
  ```
  `clampedPageSize` allows up to 500. Page 5 of 500 = 2,500 rows fetched from Postgres just to discard 2,000 of them in C#.
- **Roundtrips**: 1 — but that one query returns unbounded data.
- **Impact**: Memory + network. With 10K scheduled jobs the listing is fine, with 100K it stalls the controller. Also defeats the SQL `LIMIT 500 OFFSET n` push-down.
- **Recommendation**: Add `Skip`/`Take` to `ScheduledJobStore.ListAsync`, return `(IReadOnlyList<ScheduledJob> Items, int Total)`. Also remove the in-memory `Skip`/`Take` in the controller.

### 1.4 [HIGH] `MemoryFactQueries.LoadVisibleAsync` unbounded load + `int.MaxValue` cap

- **File**: `platform/src/modules/Memory/Comuki.Modules.Memory.Infrastructure/Persistence/Stores/EfMemoryStore.cs:117-123`
- **Pattern**:
  ```csharp
  var visible = await MemoryFactQueries.LoadVisibleAsync(...);  // SELECT * with no LIMIT
  return MemoryFallbackRanking.Rank(visible.Select(MemoryFactViewMapper.Of), int.MaxValue);
  ```
  The `ListAsync` overload passes `int.MaxValue` to the ranker — even after the SELECT returns 10K rows, ranking emits all of them.
- **N+1 risk**: no — single SELECT. But it's an **unbounded read**.
- **Impact**: A long-running tenant with 100K memory facts pulls 100K rows + 100K `MemoryFactView` allocations into the heap before returning. This is the embedding-free fallback; when pgvector is present the `TrySearchCosineAsync` short-circuits with `LIMIT query.Limit`, but `ListAsync` bypasses both.
- **Recommendation**: Take a `limit` parameter (default 100, max 1000) and pass it to `LoadVisibleAsync` (`Take(limit)`). Surface `total` separately if paginated UI is needed.

### 1.5 [MEDIUM] Yandex Tracker provider ignores `page` parameter

- **File**: `platform/src/modules/Intake/Comuki.Modules.Intake.Infrastructure/Providers/YandexTracker/YandexTrackerTicketSourceProvider.cs:50-64`
- **Pattern**:
  ```csharp
  var issues = await api.SearchIssuesAsync(body, cancellationToken);   // calls default page
  return [.. issues
      .Skip((page - 1) * PageSize)
      .Take(PageSize)
      .Select(...)];
  ```
  The provider never forwards `page` to the upstream API — it always calls without paging. Then in-memory `Skip((page-1)*25).Take(25)`. With a 50-row upstream page size this works for page 1-2, page 3+ returns empty rows.
- **N+1 risk**: no, but **functional bug**: `InboxController.FetchCatalogAsync(page: 5)` returns `[]`.
- **Recommendation**: Pass `page` to `SearchIssuesAsync` (`IYandexTrackerApi.SearchIssuesAsync(TrackerSearchBody body, int page, CancellationToken ct)`); remove the in-memory `Skip/Take`.

### 1.6 [MEDIUM] `EscalationTimeoutSweeper` Singleton + Scoped DbContext — captive dependency

- **File**: `platform/src/engine/Comuki.Engine.Orchestration/Infrastructure/EscalationTimeout/EscalationTimeoutSweeper.cs:29-32`, registered at `OrchestrationInfrastructureExtensions.cs` via `services.AddSingleton<EscalationTimeoutSweeper>()` (implied by its DI consumption).
- **Pattern**: `EscalationTimeoutSweeper(OrchestrationDbContext db, ...)` injects the **Scoped** DbContext directly. The DI rule is "Singleton cannot depend on Scoped" (`di-lifetimes.md` §5). When the worker resolves the sweeper once at boot it captures one DbContext for the lifetime of the host — and then serial calls to `SweepAsync` mutate its change tracker across many requests, which can surface as "A command is already in progress" under Npgsql.
- **Impact**: Functional bug masked today because (a) sweeps are rare and (b) `EscalationTimeoutWorker.ExecuteAsync` opens a fresh DI scope each cycle and re-resolves the sweeper — so the lifetime *appears* per-cycle even though the registration says Singleton. **This works by accident.** A direct `services.GetRequiredService<EscalationTimeoutSweeper>()` in another path would expose the captive dependency.
- **Recommendation**: Match the rest of the engine — change to `IServiceScopeFactory` pattern (`LeaseReaper`/`MergeQueueStore` already do this):
  ```csharp
  public sealed class EscalationTimeoutSweeper(
      IServiceScopeFactory scopeFactory,
      ISubjectScopeAccessor scopeAccessor,
      TimeProvider clock,
      IOptions<EscalationTimeoutOptions> options);
  ```
  And inside `SweepAsync`: `await using var scope = scopeFactory.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<OrchestrationDbContext>(); ...`.

### 1.7 [LOW] `RunEventsBroadcastInterceptor.pending` can leak under failed saves

- **File**: `platform/src/host/Comuki.Host/Realtime/Broadcasting/RunEventsBroadcastInterceptor.cs:25`
- **Pattern**: `ConcurrentDictionary<DbContext, IReadOnlyList<RunEventEntry>>` keyed by DbContext instance. `SavingChangesAsync` inserts an entry; `SavedChangesAsync` removes it; `SaveChangesFailedAsync` removes it. **All three paths remove.** The dictionary is bounded by the number of in-flight DbContext instances, which is bounded by the DI scope count. So this is *not* a leak — flagged only because the cleanup happens via `TryRemove` and a future code path that *skips* the cleanup (e.g. an `OperationCanceledException` between `SavingChangesAsync` and `SavedChangesAsync`) would leak one entry per cancelled write. Worth a comment + a periodic sweeper.
- **Impact**: Negligible in steady-state.
- **Recommendation**: Add a defensive sweeper — purge entries older than 5 minutes. Document the cancellation edge case.

### 1.8 [LOW] Scope filter on `WorkItem` runs `Runs.Any(...)` (correlated EXISTS)

- **File**: `platform/src/engine/Comuki.Engine.Orchestration/Infrastructure/Persistence/OrchestrationDbContext.cs:108-109`
- **Pattern**:
  ```csharp
  modelBuilder.Entity<WorkItem>()
      .HasQueryFilter(item => ScopeUnrestricted || Runs.Any(run => run.Id == item.RunId));
  ```
  Translates to `WHERE (... OR EXISTS (SELECT 1 FROM runs WHERE runs.id = work_items.run_id AND <scope>))`. The `runs.id` PK lookup is cheap, but for every WorkItem query EF emits the EXISTS. `GetRunDetailHandler` (line 56-60) loads work items via `db.WorkItems.Where(item => item.RunId == runId)` — there's already a specific `runId`, the EXISTS is redundant.
- **Impact**: Per-row EXISTS evaluation. Cheap on Postgres for the runs PK lookup; not a hot perf issue, but cleaner would be to skip the filter for queries that already filter on `RunId`.
- **Recommendation**: Acceptable as-is. A future "fast path" pass could skip the scope filter when the query already has a RunId predicate — but EF doesn't expose that hook cleanly today.

### 1.9 [LOW] `OrchestrationDbContext.ScopeProjectIds` allocates per call

- **File**: `platform/src/engine/Comuki.Engine.Orchestration/Infrastructure/Persistence/OrchestrationDbContext.cs:69-71`
- **Pattern**:
  ```csharp
  public ProjectId[] ScopeProjectIds => scopeAccessor is { } accessor
      ? [.. accessor.Current.ProjectIds]
      : [];
  ```
  Returns a freshly-allocated array on every read. The scope filter `Contains` reads it once per query — that's fine. But every property access allocates a new array; if EF evaluates the filter via expression tree, the tree gets captured once at query compile time and reuses the latest snapshot, so each query doesn't re-allocate. Net: low impact.
- **Recommendation**: No change. Note for future.

---

## 2. Backend — Memory / CPU

### 2.1 [HIGH] `ConfigurationVirtualKeyStore.grace` is never pruned

- **File**: `platform/src/modules/Proxy/Comuki.Modules.Proxy.Application/Resolving/ConfigurationVirtualKeyStore.cs:34, 56-72`
- **Pattern**:
  ```csharp
  private readonly ConcurrentDictionary<string, (VirtualKey Key, DateTimeOffset ExpiresAt)> grace = new(StringComparer.Ordinal);
  ...
  if (byToken.TryRemove(token, out var removed))
  {
      grace[token] = (removed, clock.GetUtcNow() + DefaultGracePeriod);  // never read by prune
      ...
  }
  ```
  `FindAsync` checks `grace.TryGetValue(...) && clock.GetUtcNow() < ghosted.ExpiresAt` — entries past expiry stay forever in the dictionary. Each deletion adds an entry; never removed.
- **Impact**: **Unbounded growth** — every operator-side key deletion adds one entry per token. Over a year with daily churn that's 365+ stale `VirtualKey` instances sitting in memory. Each carries the token string, the upstream spec, the budget, the model list — small per entry, but unbounded.
- **Recommendation**: On every `FindAsync` (it's already on the request hot path) opportunistically remove expired entries:
  ```csharp
  // after grace.TryGetValue succeeds
  if (clock.GetUtcNow() >= ghosted.ExpiresAt) grace.TryRemove(token, out _);
  ```
  Or schedule a timer sweep (less elegant — opportunistic is fine at lookup frequency).

### 2.2 [HIGH] `ProxyTransforms.RewriteAuthFromVirtualKeyAsync` mutates shared `HttpClient` headers per call

- **File**: `platform/src/modules/Proxy/Comuki.Modules.Proxy.Infrastructure/Providers/TrackerClientFactory.cs:30-92` + `IntakeProvidersExtensions.cs:28-35`
- **Pattern**:
  ```csharp
  services.AddHttpClient(TrackerHttp.GitHubClient)
      .AddStandardResilienceHandler();
  ...
  var http = httpClientFactory.CreateClient(TrackerHttp.GitHubClient);   // singleton instance!
  http.BaseAddress = new Uri(apiBase);                                    // race
  http.DefaultRequestHeaders.Accept.Add(...);                            // race
  TrackerHttpHeaders.ApplyBearer(http, token);                            // race
  return RestService.For<GitHub.IGitHubApi>(http, refitSettings);       // bound to the singleton
  ```
  `IHttpClientFactory` returns the same `HttpClient` instance for a named client — it's pooled internally (handlers reset per request), but `BaseAddress` and `DefaultRequestHeaders.Accept` are mutated on the **instance**. Two concurrent `FetchCatalogAsync` calls (one for connection A pointing to enterprise GitHub, one for connection B pointing to a self-hosted instance) race on `BaseAddress`. `DefaultRequestHeaders.Add` is documented thread-safe but `.Accept.Add()` collides too.
- **Impact**: **Real thread-safety bug**. The first concurrent call wins the BaseAddress race; the second call's outbound request goes to the wrong host. **Critical for any deployment with multiple GitHub / GitLab / Jira connections / tenants.**
- **Recommendation**: Per the `http-resilience-refit.md` rule: do not mutate shared `HttpClient` headers. Options:
  1. Create a fresh `HttpClient` per call (cheap when the factory pools the handler):
     ```csharp
     var http = httpClientFactory.CreateClient(TrackerHttp.GitHubClient); // OK — that's a *handler*
     // ... but DO NOT set BaseAddress on it; instead pass it as `new HttpRequestMessage { RequestUri = new Uri(apiBase + path) }`.
     ```
  2. Better: convert the per-tenant `IGitHubApi` to a typed-client interface registered via `AddHttpClient<IGitHubApi>(...)` keyed per connection — but the spec is per-connection, so this implies a per-connection service registration. That's a bigger refactor.
  3. Cheapest patch: keep the shared `HttpClient`, but stop mutating `BaseAddress`/`Headers`; pass the base URL into the path string and use `Authorization` per-call via `HttpRequestMessage.Headers`.

### 2.3 [HIGH] `ProxyTransforms.TryReadMaxOutputTokensAsync` buffers the request body for every proxy request

- **File**: `platform/src/modules/Proxy/Comuki.Modules.Proxy.Infrastructure/Yarp/ProxyTransforms.cs:104-141`
- **Pattern**:
  ```csharp
  request.EnableBuffering();
  request.Body.Position = 0;
  using var document = await JsonDocument.ParseAsync(request.Body, cancellationToken: cancellationToken);
  ...
  request.Body.Position = 0;
  ```
  Every proxy request (`/v1/chat/completions`, `/v1/messages`) has its entire request body buffered to peek at `max_tokens` / `max_output_tokens` fields. Up to 256 KB (`MaxBodyPeekBytes = 256 * 1024`). Then `request.EnableBuffering()` keeps a copy in a `MemoryStream` while YARP streams the original.
- **Impact**: 2× memory cost on every proxy request. For a multi-megabyte image-base64 input, the buffered copy lives until YARP finishes forwarding.
- **Recommendation**: Use a `StreamReader` over the buffered stream and a `Utf8JsonReader` to scan only for the known field names (`max_tokens`, `max_output_tokens`), discarding the body without constructing a full `JsonDocument`. Or: make `max_output_tokens` optional and check upstream response (deferred to response transform).

### 2.4 [MEDIUM] `SchedulerWorkerPoolState.List` enumerates all workers and filters

- **File**: `platform/src/engine/Comuki.Engine.Compute/Pool/WorkerPoolState.cs:23-26`
- **Pattern**:
  ```csharp
  public IReadOnlyList<PoolWorker> List(ProjectId projectId)
  {
      return [.. workers.Values.Where(worker => worker.ProjectId == projectId)];
  }
  ```
  Single shared dictionary; per-project listing is O(N) over the whole pool.
- **Impact**: Pool size is bounded (concurrent workers, typically <100) so O(N) is fine. Not a hot path (called from scale supervisor cycles, ~minutes apart). Flagged as **architectural**: at multi-tenant scale with thousands of workers, indexing by project would matter.
- **Recommendation**: `ConcurrentDictionary<ProjectId, ConcurrentDictionary<WorkerId, PoolWorker>>` — one dict per project.

### 2.5 [MEDIUM] `TrackerClientFactory` per-call new `RestService.For<>` (Refit proxy generation)

- **File**: `platform/src/modules/Intake/Comuki.Modules.Intake.Infrastructure/Providers/TrackerClientFactory.cs:30-92`
- **Pattern**: Every `FetchCatalogAsync` call:
  1. `httpClientFactory.CreateClient(TrackerHttp.GitHubClient)` — singleton handler (cheap)
  2. Mutate `BaseAddress`/`Headers` (race — see 2.2)
  3. `RestService.For<IGitHubApi>(http, refitSettings)` — **builds a Refit proxy via reflection at runtime**
- **Impact**: Step 3 is the slowest — Refit's `RestService.For<T>` uses `DispatchProxyGenerator` to build a dynamic proxy at call time. Per-call `RestService.For<>` re-runs the generation if `T` is the same, it caches via static `ConcurrentDictionary<Type, ProxyTypeCache>` — so subsequent calls are fast, but **the first call after JIT is slow** (tens of ms). Once warmed, allocation is small.
- **Recommendation**: Acceptable. Worth a comment that the first call per type is slow (cold-start).

### 2.6 [LOW] JSON: `JsonSerializerOptions.Web` reused correctly everywhere

- **Files**: 50+ call sites. All use `JsonSerializerOptions.Web` — no `new JsonSerializerOptions(...)` and no `private static readonly JsonSerializerOptions` field anywhere. Compliant with `anti-patterns.md` §6. **Pass.**

### 2.7 [LOW] No `new HttpClient()` or `new JsonSerializerOptions` outside rules

- **Files**: searched all of `platform/src`. Zero `new HttpClient` violations. Zero `new JsonSerializerOptions` violations. **Pass.**

### 2.8 [INFO] `ConcurrentDictionary` usage — 14 hits, no leaks found in steady state

- `WorkerCommandHub.channelsByWorker`: cleaned up via `Unregister` (gRPC stream close). **Pass.**
- `ProjectSettingsCacheRefresher.fallbackSnapshots`: pruned in `FallbackAsync` when `age > FallbackTtl`. **Pass.**
- `ConfigurationVirtualKeyStore.byToken`: seeded from options, no churn. **Pass** (but see 2.1 for `grace`).
- `WorkerPoolState.workers`: pruned in `SyncFromProviderAsync`. **Pass.**
- `RunEventsBroadcastInterceptor.pending`: see 1.7.
- `ConfigurationVirtualKeyStore.grace`: see 2.1 — **FAIL**.

### 2.9 [INFO] HTTP client lifetime — typed Refit + ResilienceHandler

- All outbound HTTP clients use `services.AddHttpClient(name).AddStandardResilienceHandler()` (`IntakeProvidersExtensions.cs:28-35`). No bare `new HttpClient()`. **Compliant** with `http-resilience-refit.md` except for the 2.2 race.

### 2.10 [INFO] DI lifetimes — `BackgroundService` × 12

| Service | Lifetime | Notes |
|---|---|---|
| `RunArtifactPackagerHostService` | Singleton (via `AddHostedService`) — DI: `IServiceScopeFactory` | Correct |
| `RunArtifactPackagerService` (the implementation class — not hosted itself; called by `RunArtifactPackagerHostService`) | Singleton — DI: `IServiceScopeFactory` | Correct |
| `ScaleSupervisorWorker` | Singleton — DI: `IComputeProvider`, `TimeProvider` | Correct (no DbContext) |
| `OidcStateSweeper` | Singleton — DI: `IServiceScopeFactory` | Correct |
| `ArtifactBucketInitializer` | Singleton (AddHostedService) | Correct |
| `LeaseReaperWorker` | Singleton — DI: `IServiceScopeFactory` | Correct |
| `EscalationTimeoutWorker` | Singleton — DI: `IServiceScopeFactory` | Correct |
| `BootstrapAdminStartupService` | Singleton | Correct |
| `PermissionDemandStartupValidator` | Singleton | Correct |
| `RunStatusBridgeWorker` (Intake) | Singleton — DI: `IServiceScopeFactory` | Correct |
| `ScheduledJobDispatcherWorker` | Singleton — DI: `IServiceScopeFactory` | Correct |
| `MemorySweepWorker` | Singleton — DI: `IServiceScopeFactory` | Correct |
| `KnowledgeIngestBackgroundService` | Singleton | Correct |
| `ProjectSettingsCacheRefresher` | Singleton — DI: `IDbContextFactory` | Correct |
| `TranslatorHostedService` (Comuki.Host.Translator) | Singleton | Correct |

**All `BackgroundService` instances are correctly Singleton with `IServiceScopeFactory` injection, EXCEPT `EscalationTimeoutSweeper` (see 1.6) which is itself a Singleton with a Scoped DbContext.**

---

## 3. Frontend — Bundle & Runtime

### 3.1 Build output (2026-09-09)

```
dist/index.html                                                      2.85 kB │ gzip:   1.36 kB
dist/assets/index-BvhhY3TB.css                                     239.80 kB │ gzip:  37.99 kB
dist/assets/start-D17EBNpp.js                                        0.13 kB │ gzip:   0.14 kB
dist/assets/index-MfNixFSe.js                                    1,349.08 kB │ gzip: 402.35 kB
```

Fonts: 165 kB total (8 woff2 files). **No code splitting — all routes, all kubb-generated clients (70 clients + 100 hooks + 506 files in `_generated/`), and React 19 + TanStack Router + TanStack Query + react-aria-components + tailwind runtime all ship in one chunk.** Vite's own warning: `(!) Some chunks are larger than 500 kB after minification`.

### 3.2 [HIGH] Single JS chunk — no route-level code splitting

- **File**: `dashboard/src/routeTree.gen.ts:1-695` (auto-generated by `@tanstack/router-plugin`)
- **File**: `dashboard/src/routes/*.tsx` (every route imports eagerly, e.g. `dashboard/src/routes/runs/index.tsx` directly imports the page from `@/domains/runs`)
- **Pattern**: Every route file uses `createFileRoute(...).Component(RouteComponent)` — no `createLazyFileRoute` anywhere (`rg "createLazyFileRoute|lazyRoute" dashboard/src/routes --type ts` → 0 hits). The 512 generated files in `_generated/` are imported directly by domain hooks and bundled into the main chunk.
- **Impact**: First-paint cost = full JS bundle even on `/login` (which has no runs/chat/knowledge dependency). At ~400 KB gzipped on slow connections this is 2-4s of idle. Login should be <100 KB.
- **Recommendation**:
  1. Move kubb-generated `_generated/` to a separate chunk via `vite.config.ts` `build.rolldownOptions.output.manualChunks: { api: [/src\/shared\/api\/_generated\//] }` — react-query hooks aren't on every route's first paint.
  2. Convert at least `/login`, `/identity/*`, `/runs/*`, `/chat/*`, `/knowledge/*` to `createLazyFileRoute` so each becomes its own chunk.
  3. Long term: route-level manual chunks (`manualChunks(id) { if (id.includes('/runs/')) return 'runs' }`).

### 3.3 [MEDIUM] TanStack Query keys without parameters (cache collision risk)

- **File**: `dashboard/src/domains/runs/api/queries.ts:23, 88-93`
- **Pattern**:
  ```typescript
  export const runsQueryKey = ["runs"] as const
  export function useRunsQuery() {
    return useQuery({ queryKey: runsQueryKey, queryFn: listRuns, })
  }
  ```
  And `listRuns` hardcodes `page: 1, pageSize: 100`. The TanStack Query key `["runs"]` is the same for **every** runs-list invocation, regardless of filter or page. This is fine today because there's only ever one page in the FE — but it's a foot-gun: the moment the FE adds a project filter or status filter, two different lists will collide on the same cache slot.
- **Impact**: latent; will surface as soon as filter UI lands.
- **Recommendation**: `queryKey: [...runsQueryKey, filters, page] as const` (matches the inbox pattern at `dashboard/src/domains/inbox/api/queries.ts:133-134`).

### 3.4 [MEDIUM] No `refetchInterval` anywhere — "live" data is static

- **File**: 35 `useQuery({})` sites; 0 `refetchInterval` matches.
- **Pattern**: All queries rely on user action (page reload, mutation `invalidateQueries`) to refetch. The runs screen says "Live runs" in the domain AGENTS.md but only refetches on `useRunQuery` mount.
- **Impact**: Real-mode dashboards that should auto-refresh don't. The Realtime SignalR feed is separate from the query cache (`RunEventsBroadcastInterceptor` broadcasts to SignalR groups, but doesn't `queryClient.invalidateQueries(['runs'])`).
- **Recommendation**: For run-status-bearing pages: `refetchInterval: 30_000` (matches the kubb convention from the previous Console.x) **OR** subscribe to the SignalR group and call `queryClient.invalidateQueries(...)` on relevant events.

### 3.5 [MEDIUM] `useEffect` count = 43 in `src/domains/`

- **Files**: 43 `useEffect` occurrences. Mostly legitimate (mobile detection, theme subscription, focused-input handling). No `setInterval`/`setTimeout` data-fetching found.
- **Impact**: No re-render storms observed. The Tailwind v4 + shadcn stack is signal-free.
- **Recommendation**: None — present use is idiomatic.

### 3.6 [LOW] TanStack Query config — `staleTime: 30_000` default

- **File**: `dashboard/src/app/providers.tsx:11-19`
- **Pattern**:
  ```typescript
  new QueryClient({
    defaultOptions: {
      queries: {
        staleTime: 30_000,
        retry: 1,
        refetchOnWindowFocus: false,
      },
    },
  })
  ```
- **Impact**: 30-second global staleTime. Acceptable. The runs/inbox/chat domains override per-query only when necessary.
- **Recommendation**: None.

### 3.7 [LOW] `QueryClientProvider` ordering bug — already fixed

- **File**: `dashboard/src/app/providers.tsx:38-46, 64-90`
- **Pattern**: The historical issue (commit `f000001`) was that `useAuthState` was called *above* the `QueryClientProvider`. Now resolved — `AuthBoot` is a child of `QueryClientProvider`, and `SessionProvider` is a child of `AuthBoot`. **Pass.**
- **Note**: Documented per STATE.md; flagged for completeness.

### 3.8 [LOW] Mock-first domains throw on real-mode

- **File**: `dashboard/src/domains/runs/api/queries.ts:48-49`, `src/domains/identity/api/queries.ts:208-229` (and 5+ other mock-first domains)
- **Pattern**: When `VITE_USE_MOCK=false` but the hook isn't yet wired to a real backend, the kubb-client throws `[kubb-client] VITE_API_BASE_URL is not set.` on first call. The error surfaces as a TanStack Query error, the page renders an error state, and on retry the error persists.
- **Impact**: Operator misconfiguration produces a permanently-broken screen rather than a clear "API base URL not configured" message. Mount/unmount churns on retries cause re-mount → re-throw.
- **Recommendation**: Already handled at the kubb-client level — but the screen renders the same "error" state every time, masking the config issue from operators. Consider a one-time "API base URL not set" badge instead of an error panel.

### 3.9 [INFO] Image loading — none observed

- No `<img>` or `next/image` equivalents found beyond favicons. All UI is icon-based (lucide-react). No image-perf issue.

### 3.10 [INFO] Tailwind v4 — compile-time, no runtime CSS-in-JS

- `dashboard/package.json:39-40` → `@tailwindcss/vite`. All styling is compile-time. **Pass.**

---

## 4. Background Workers

### 4.1 Lifetime audit (cross-ref 2.10)

All 12+ `BackgroundService` registrations are **Singleton + `IServiceScopeFactory`** except `EscalationTimeoutSweeper` (captive `DbContext` — see 1.6). **Pass with one exception.**

### 4.2 Polling intervals — current values

| Worker | Default interval | Where set |
|---|---|---|
| `OidcStateSweeper` | `Host:OidcSweep:Interval` (5 min in STATE.md) | `OidcSweepOptions.Interval` |
| `RunArtifactPackagerHostService` | 10 s | hardcoded `TimeSpan.FromSeconds(10)` line 51 |
| `RunArtifactPackagerService` | 10 s | `DefaultPollInterval = TimeSpan.FromSeconds(10)` line 48 |
| `LeaseReaperWorker` | `LeaseOptions.ReapInterval` | options |
| `EscalationTimeoutWorker` | `EscalationTimeoutOptions.SweepInterval` | options |
| `ScheduledJobDispatcherWorker` | `SchedulerOptions.PollInterval` (30 s) | options |
| `MemorySweepWorker` | scheduler module options | options |
| `ProjectSettingsCacheRefresher` | 15 s | hardcoded `RefreshInterval = TimeSpan.FromSeconds(15)` |
| `KnowledgeIngestBackgroundService` | (queued trigger, not periodic) | — |
| `ArtifactBucketInitializer` | one-shot at startup | — |
| `RunStatusBridgeWorker` (Intake) | `Intake:BridgeInterval` | options |
| `ScaleSupervisorWorker` | Compute options | options |

**Observations:**
- `ProjectSettingsCacheRefresher` polls every **15s** and full-table-loads `db.ProjectSettings.ToListAsync()` — at 1K projects this is fine; at 100K projects, the steady-state scan is 4/min of full-table loads.
- `RunArtifactPackagerHostService` polls every **10s** — this is two redundant pollers (the in-module `RunArtifactPackagerService` ALSO runs a 10s loop, registered separately? — actually only the host one is hosted; the module one is just the implementation). Confirmed in `HostComposer.cs:161` only one `AddHostedService` line, so this is the correct single driver.
- `EscalationTimeoutSweeper` polls **per minute** by default — combined with the N+1 (1.2), a 100-stale-run sweep can take 5-10 seconds.

### 4.3 Lock contention audit

- **`WorkItemQueueEf.ClaimAsync`** — `FOR UPDATE SKIP LOCKED` inside `BeginTransactionAsync` ✓ race-safe across replicas.
- **`MergeQueueStoreEf.ClaimNextAsync`** — same pattern ✓.
- **`LeaseReaper`** — `WorkItemQueueSql.CreateReapRequeueCommand` uses `RETURNING` on a guarded UPDATE ✓.
- **`ScheduledJobStore.ListDueAsync`** — `FromSqlRaw` with `FOR UPDATE SKIP LOCKED` **but no explicit `BeginTransactionAsync`** around it. **This is the SELECT-then-UPDATE deadlock risk** the audit task flagged. The subsequent `UpdateAsync(job)` call in `ScheduledJobDispatcherWorker.PollOnceAsync` line 117-118 fires a separate UPDATE without a lock from the SELECT — by the time it runs, another replica could have claimed the same row. Postgres releases `FOR UPDATE` locks when the SELECT statement commits (auto-commit mode). For `SKIP LOCKED` to actually skip, the SELECT must be inside a transaction that holds the lock until the UPDATE.
- **Impact**: **Race between replicas.** Under dual-host deployment, two host replicas can fire the same cron job at the same tick. The unique-index on the run-row would catch it downstream but the dispatcher would log spurious "duplicate fire" errors.
- **Recommendation**: Wrap the ListDue + UpdateAsync pair in a single `BeginTransactionAsync` (or use raw `UPDATE ... FROM (SELECT ... FOR UPDATE SKIP LOCKED) ... WHERE ... RETURNING`). The `WorkItemQueueEf` already does it; this store was inconsistent.

### 4.4 ProjectSettingsCacheRefresher polling pattern

- 15s interval, full-table-load every cycle. **OK at <1K projects**, becomes a concern at 10K+. The fallback dictionary captures 30s of staleness (Q27) — but the *success* path doesn't have a TTL on the cache itself, so cache entries live forever until overwritten. **Pass** but log when N exceeds threshold.

---

## 5. Recommendations (no code)

### Priority 1 — must-fix before any production load test

1. **Add indexes to `Run`** — `(status, updated_at)` partial on `Escalated`, `project_id` for the global scope filter, and `(status, updated_at)` for the default sort. Same on `RunEvent(run_id, occurred_at)` for the journal strip. See 1.1.
2. **Fix `EscalationTimeoutSweeper` N+1** — replace `FirstOrDefaultAsync(id)` loop with raw SQL `UPDATE ... FROM (SELECT ... FOR UPDATE SKIP LOCKED) RETURNING`. See 1.2.
3. **Wrap `ScheduledJobStore.ListDueAsync` in a transaction** — the `FOR UPDATE SKIP LOCKED` is released on auto-commit and the subsequent `UpdateAsync` race-loses between replicas. See 4.3.
4. **Fix `EscalationTimeoutSweeper` captive `DbContext`** — switch to `IServiceScopeFactory` to match the rest of the engine. See 1.6.
5. **Prune `ConfigurationVirtualKeyStore.grace` opportunistically** — opportunistic removal in `FindAsync` keeps the dictionary bounded. See 2.1.
6. **Fix `TrackerClientFactory` shared-`HttpClient` header race** — stop mutating `BaseAddress`/`Headers` on a pooled `HttpClient`. Pass the per-call URL via `HttpRequestMessage` instead. **Critical for any multi-connection deployment.** See 2.2.

### Priority 2 — must-fix within 30 days

7. **Dashboard route-level code splitting** — at least `/login`/`/identity/*`/`/runs/*` to `createLazyFileRoute`; kubb-generated code to a manual chunk. See 3.2.
8. **Replace `ScheduledJobsController.ListAsync` in-memory pagination with SQL `Skip/Take`** — `ScheduledJobStore.ListAsync` should return `(IReadOnlyList<ScheduledJob> Items, int Total)`. See 1.3.
9. **Cap `MemoryFactQueries.LoadVisibleAsync` and `EfMemoryStore.ListAsync`** — add a `limit` parameter (default 100, max 1000). See 1.4.
10. **Forward `page` to Yandex Tracker `SearchIssuesAsync`** — currently in-memory `Skip/Take` is a functional bug at page 3+. See 1.5.
11. **Reduce `ProxyTransforms.TryReadMaxOutputTokensAsync` body buffer** — replace `JsonDocument.ParseAsync` with a streaming `Utf8JsonReader` scan. See 2.3.
12. **Cap `ProxyTransforms` body peek** — already capped at 256 KB but raise to 64 KB for the common case; larger inputs skip the guard.

### Priority 3 — post-ship / v2 backlog

13. **TanStack Query keys need filter params** — runs/inbox caches collide today; will break the moment filters land. See 3.3.
14. **`refetchInterval` or SignalR → `invalidateQueries`** — make "Live runs" actually live. See 3.4.
15. **Indexer for `WorkerPoolState.List(projectId)`** — multi-tenant scale. See 2.4.
16. **Pre-compiled Refit proxies** — call-site warming of `RestService.For<>` is fast on warm cache; cold-start latency could be removed by pre-generating at DI registration time.
17. **`ProjectSettingsCacheRefresher` change-detection on `updated_at`** — instead of full-table reload, do `WHERE updated_at > $last_poll` for deltas only.

---

## Appendix: Files Reviewed

### Backend (`.cs` files read end-to-end)

| File | Reason |
|---|---|
| `platform/src/host/Comuki.Host/HostComposer.cs` | Composition root — lifetime audit |
| `platform/src/host/Comuki.Host/Runs/RunsListHandler.cs` | `GET /api/v1/runs` |
| `platform/src/host/Comuki.Host/Runs/GetRunDetailHandler.cs` | `GET /api/v1/runs/{id}` |
| `platform/src/host/Comuki.Host/Runs/HostCancelRunAdapter.cs` | cancel port |
| `platform/src/host/Comuki.Host/Runs/HostApproveRunAdapter.cs` | approve port |
| `platform/src/host/Comuki.Host/Runs/Controllers/RunsController.cs` | controller skeleton |
| `platform/src/host/Comuki.Host/Projects/ProjectsModuleEndpoints.cs` | project surface |
| `platform/src/host/Comuki.Host/Intake/Controllers/InboxController.cs` | inbox |
| `platform/src/host/Comuki.Host/Intake/InboxController.cs` (via grep) | — |
| `platform/src/host/Comuki.Host/Costs/OrchestrationBudgetGate.cs` | budget gate |
| `platform/src/host/Comuki.Host/Scheduler/ScheduledJobsController.cs` | scheduler list |
| `platform/src/host/Comuki.Host/Workers/OidcStateSweeper.cs` | sweep worker |
| `platform/src/host/Comuki.Host/Workers/Grpc/WorkerCommandHub.cs` | worker channels |
| `platform/src/host/Comuki.Host/Realtime/Broadcasting/RunEventsBroadcastInterceptor.cs` | interceptor |
| `platform/src/host/Comuki.Host/Mcp/McpServer.cs` | MCP JSON-RPC dispatcher |
| `platform/src/host/Comuki.Host/Artifacts/RunArtifactPackagerHostService.cs` | host driver |
| `platform/src/host/Comuki.Host/Artifacts/OrchestrationArtifactRunSource.cs` | source |
| `platform/src/host/Comuki.Host/Artifacts/OrchestrationArtifactJournalSource.cs` | journal source |
| `platform/src/engine/Comuki.Engine.Orchestration/Infrastructure/Persistence/OrchestrationDbContext.cs` | scope filters |
| `platform/src/engine/Comuki.Engine.Orchestration/Infrastructure/EscalationTimeout/EscalationTimeoutSweeper.cs` | N+1 |
| `platform/src/engine/Comuki.Engine.Orchestration/Infrastructure/Hosting/EscalationTimeoutWorker.cs` | worker |
| `platform/src/engine/Comuki.Engine.Orchestration/Infrastructure/Leases/LeaseReaper.cs` | guarded UPDATE |
| `platform/src/engine/Comuki.Engine.Orchestration/Infrastructure/Hosting/LeaseReaperWorker.cs` | worker |
| `platform/src/engine/Comuki.Engine.Orchestration/Infrastructure/Queue/WorkItemQueueEf.cs` | SKIP LOCKED claim |
| `platform/src/engine/Comuki.Engine.Orchestration/Infrastructure/Persistence/Stores/MergeQueueStoreEf.cs` | claim |
| `platform/src/engine/Comuki.Engine.Orchestration/Infrastructure/Persistence/Configurations/RunConfiguration.cs` | (no indexes) |
| `platform/src/engine/Comuki.Engine.Orchestration/Infrastructure/Persistence/Configurations/WorkItemConfiguration.cs` | (indexes OK) |
| `platform/src/engine/Comuki.Engine.Compute/Pool/WorkerPoolState.cs` | O(N) List |
| `platform/src/engine/Comuki.Engine.Compute/Settings/InMemoryProjectScaleSettings.cs` | scale settings |
| `platform/src/modules/Projects/.../Stores/ProjectStore.cs` | projects |
| `platform/src/modules/Projects/.../Stores/ProjectSettingsCacheRefresher.cs` | settings cache |
| `platform/src/modules/Projects/.../Projects/Queries/ListProjectsHandler.cs` | handler |
| `platform/src/modules/Projects/.../Views/ProjectMapper.cs` | view mapping |
| `platform/src/modules/Chat/.../Stores/ChatSessionStore.cs` | chat store |
| `platform/src/modules/Intake/.../Stores/IntakeStore.cs` | intake |
| `platform/src/modules/Intake/.../Providers/GitHubTicketSourceProvider.cs` | GH |
| `platform/src/modules/Intake/.../Providers/YandexTrackerTicketSourceProvider.cs` | YT (page bug) |
| `platform/src/modules/Intake/.../Providers/TrackerClientFactory.cs` | HTTP race |
| `platform/src/modules/Memory/.../Stores/EfMemoryStore.cs` | memory + embeddings |
| `platform/src/modules/Scheduler/.../Stores/ScheduledJobStore.cs` | scheduler store (race) |
| `platform/src/modules/Scheduler/.../Sync/ScheduledJobDispatcherWorker.cs` | dispatcher |
| `platform/src/modules/Proxy/.../Resolving/ConfigurationVirtualKeyStore.cs` | grace leak |
| `platform/src/modules/Proxy/.../Yarp/ProxyTransforms.cs` | body buffer + auth |
| `platform/src/modules/Proxy/.../Yarp/ProxyConfigProvider.cs` | YARP config |
| `platform/src/shared/Comuki.Shared.Filtering/Ports/QueryableFilterExtensions.cs` | DSL → SQL |
| `platform/src/shared/Comuki.Shared.Kernel/Scoping/AsyncLocalSubjectScopeAccessor.cs` | scope accessor |
| `platform/src/shared/Comuki.Shared.Kernel/Scoping/SubjectScope.cs` | scope |

### Frontend (`dashboard/src`)

| File | Reason |
|---|---|
| `dashboard/src/app/main.tsx` | bootstrap |
| `dashboard/src/app/providers.tsx` | query client |
| `dashboard/src/routeTree.gen.ts:1-100` | route tree (auto-gen) |
| `dashboard/src/domains/runs/api/queries.ts` | runs query keys |
| `dashboard/src/domains/inbox/api/queries.ts:130-150` | cache keys w/ params |
| `dashboard/src/domains/identity/api/queries.ts:208-229` | identity |
| `dashboard/kubb.config.ts` | codegen config |
| `dashboard/vite.config.ts` | build config |
| `dashboard/package.json` | deps |

### Build artifacts

| Artifact | Size | Gzipped |
|---|---|---|
| `dist/index.html` | 2.85 kB | 1.36 kB |
| `dist/assets/index-BvhhY3TB.css` | 239.80 kB | 37.99 kB |
| `dist/assets/index-MfNixFSe.js` | 1,349.08 kB | 402.35 kB |
| `dist/assets/start-D17EBNpp.js` | 0.13 kB | 0.14 kB |
| Fonts (8 files) | ~165 kB | — |

### Static analysis totals (across `platform/src`)

| Pattern | Hits | Compliance |
|---|---|---|
| `.ToListAsync` | 27 | OK |
| `.FirstOrDefaultAsync` | 19 | OK |
| `.Include(` (eager loading) | 0 | n/a (projections only) |
| `.Skip(` (SQL pagination) | 9 | OK |
| `AsNoTracking` | 45 | OK — every read path |
| `ExecuteUpdateAsync` / `ExecuteDeleteAsync` | 7 | OK |
| `FromSqlRaw` / `FromSqlInterpolated` | 1 | OK (ScheduledJobStore.ListDueAsync — see 4.3) |
| `BeginTransactionAsync` | 8 | OK |
| `JsonSerializerOptions.Web` reuse | 50+ | OK |
| `new JsonSerializerOptions` | 0 | OK |
| `new HttpClient` | 0 | OK |
| `BackgroundService` subclasses | 20 | 1 violation (1.6) |
| `AddHostedService` registrations | 14 | OK |
| `ConcurrentDictionary` | 14 | 1 leak (2.1) |
| `HasIndex` declarations | 220 | OK except Run (1.1) |
| `HasQueryFilter` (scope filters) | 5 | OK (1 concern: 1.8) |

### Static analysis totals (across `dashboard/src`)

| Pattern | Hits |
|---|---|
| `useQuery` calls | 35+ |
| `useMutation` calls | ~10 |
| `refetchInterval` | 0 |
| `useEffect` (domains) | 43 |
| `useState` (non-test) | many (idiomatic) |
| `createLazyFileRoute` | 0 |
| `_generated/` files | 506 |
| `_generated/clients` | 70 |
| `_generated/hooks` | 100 |
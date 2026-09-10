# Comuki v1 Testing Quality Audit — 2026-09-09

**Branch tip:** `e3de735c1192f69a234e27f7e7b876ffd75a8c6f` (`fix/audit-2-test` ← `master`)
**Codebase size:** 852 production `.cs` files (869 public types) / 267 test `.cs` files
**Self-reported counts:** 1567 BE tests pass (`xUnit v3 + MTP`), 1560 FE tests pass, 28 architecture tests, 0 FE `Verify.Unit` (per STATE.md)
**Independent counts from grep:** 824 `[Fact]` + 64 `[Theory]` in `tests/unit/` (= 888 methods); 208 `[Fact]` in `tests/integration/`; 28 `[Fact]` in `tests/Comuki.Architecture.Tests`. Delta from STATE is `[InlineData]` expansion on `[Theory]`.

## Executive Summary

v1's test suite is **broad but inconsistent in discipline**, with two top-tier
issues that dwarf the rest:

1. **`_ =` discard ban is violated 401 times across 67 test files** —
   `[async-and-tasks.md] §6` calls this out as banned, and `[worker-audit.md]`
   lists it in the mandatory self-audit grep; the test base never runs the
   worker-audit regex against tests so the cleanup never trips.
2. **Per-test-class PostgreSQL containers in integration tests** — only 2 of
   21 host-flavoured integration projects use `[CollectionDefinition]` (Auth,
   Intake). The remaining 19 spin up a fresh `PostgreSqlBuilder("postgres:16-alpine")`
   + migrate 3-6 `DbContext`s per test class (~5-30s each, with no
   `WithReuse`, so the same container starts fresh every CI run). This makes
   integration test runtime ~6-10× longer than it needs to be and inflates
   CI cost.

The bottom-line shape:
- **Test coverage:** 369 production types have **no** test reference
  (out of 869 — but a large chunk are EF Configurations / wire-format DTOs
  / installer extensions / controllers covered indirectly via integration
  tests; the *logic-bearing* type coverage is much higher).
- **Test flakiness:** 6 known skipped tests (5 OIDC Keycloak + 1 OIDC state
  sweeper) due to WSL2 Docker DNS; `Task.Delay` used 6 times in 5 files
  (mostly as `WaitForAsync` polling helper, but 2 are bare timing assumptions).
- **Test anti-patterns:** 189 private methods in 95 test files (allowed by
  `class-layout-and-tooling.md` §1a carve-out — tests are exempt, but
  high counts suggest incomplete extraction), 216 `[Fact]` without
  `DisplayName` (~19% of unit tests), 401 `_ =` discards, 50 files using
  `DateTimeOffset.UtcNow` (instead of injected `TimeProvider`) — 170 hits.
- **Integration test brittleness:** 19 of 21 host integration projects
  use `[IClassFixture]` (per-class, fresh container); 0 use `[ICollectionFixture]`
  for shared Postgres. **0 use `Respawn`** for inter-test cleanup — they all
  rely on `MigrateAsync` which recreates the schema, which is heavy and
  throws away state between tests (so cross-test invariants aren't tested).

---

## 1. Coverage Gaps

### 1.1 Untested production types — by module

Searched production class declarations (regex over `public sealed class |public class |public abstract class |public static class |public sealed record |public record |public interface |public sealed partial class |public partial class`) against test-file contents (any word-boundary match).

| Module | Prod types | Tests per type | Unreferenced types |
|---|---|---:|---:|
| `platform/src/modules/Artifacts` | 22 | 0.73 | **14** |
| `platform/src/modules/Chat` | 45 | 0.53 | **22** |
| `platform/src/modules/Costs` | 19 | 1.47 | 12 |
| `platform/src/modules/Identity` | 110 | 1.02 | **31** |
| `platform/src/modules/Intake` | 115 | 1.05 | **48** |
| `platform/src/modules/Knowledge` | 27 | 0.33 | **15** |
| `platform/src/modules/Memory` | 30 | 1.43 | 11 |
| `platform/src/modules/Projects` | 44 | 1.68 | 12 |
| `platform/src/modules/Proxy` | 27 | 2.22 | 6 |
| `platform/src/modules/Scheduler` | 29 | 1.21 | 10 |
| `platform/src/modules/Verify` | 0 | 0.00 | 0 (empty module, see §1.2) |
| `platform/src/engine` (Compute + Orchestration) | 76 | 0.12* | **~80** (see §1.3) |
| `platform/src/host` (Host + Host.Brain + Host.Translator) | 226 | (covered by integration) | ~120 |
| `platform/src/shared` (Kernel, Contracts, Filtering, Telemetry) | 96 | (covered by integration or scoped) | ~30 |

\* engine "Tests per type" is misleading because most engine types are EF Configurations or extension classes that don't have tests by design.

**Important nuance:** "Unreferenced" in the table is a *string name* match, not a
*coverage* claim. A `McpServer` controller is "unreferenced" by name in unit
tests but is exercised end-to-end by `tests/integration/Comuki.Host.Integration.Smoke/McpShould.cs`.
The table is a *coverage smell* indicator, not a list of gaps to fill — see
§1.4 for the deduped gap list.

### 1.2 [Severity: Info] `Comuki.Modules.Verify.Unit` — empty project

`tests/unit/Comuki.Modules.Verify.Unit/Comuki.Modules.Verify.Unit.csproj` — the
*only* file. The Verify module itself has no production code
(`platform/src/modules/Verify/Comuki.Modules.Verify.Domain/Comuki.Modules.Verify.Domain.csproj` —
the only file, also empty). Confirms the audit-product-report's open
question (`audit-product-report.md:987`) is still accurate. Not a coverage
gap today; will be one when production code lands.

### 1.3 [Severity: Medium] Engine / Orchestration — under-tested by unit

The engine layer (`platform/src/engine/Comuki.Engine.Orchestration/`) has 13
test classes against ~50 logic-bearing classes. Examples of logic-bearing
unreferenced types:

- `EscalationTimeoutWorker` (`platform/src/engine/Comuki.Engine.Orchestration/Infrastructure/Hosting/EscalationTimeoutWorker.cs`)
- `LeaseReaperWorker` (`platform/src/engine/Comuki.Engine.Orchestration/Infrastructure/Hosting/LeaseReaperWorker.cs`)
- `MergeQueueStoreEf` (`platform/src/engine/Comuki.Engine.Orchestration/Infrastructure/Persistence/Stores/MergeQueueStoreEf.cs`) — store has no direct unit test
- `WorkItemQueueEf` (`platform/src/engine/Comuki.Engine.Orchestration/Infrastructure/Queue/WorkItemQueueEf.cs`) — same
- `EscalationTimeoutSwept` event (`platform/src/engine/Comuki.Engine.Orchestration/Infrastructure/EscalationTimeout/EscalationTimeoutSwept.cs`)
- `PoolWorker` (`platform/src/engine/Comuki.Engine.Compute/Pool/PoolWorker.cs`)
- `ScaleSupervisorWorker` (`platform/src/engine/Comuki.Engine.Compute/Supervisor/ScaleSupervisorWorker.cs`)

`WorkItemQueueEf` is exercised indirectly via `tests/integration/Comuki.Engine.Orchestration.Integration.Queue/WorkItemQueueShould.cs` (real Postgres) — that covers the `ClaimAsync` SKIP LOCKED race. `EscalationTimeoutSweeper` similarly has integration coverage (`tests/integration/Comuki.Host.Integration.Runs/EscalationTimeoutSweeperShould.cs`). The store-level classes still lack isolated unit tests against `UseInMemoryDatabase`, which means the LINQ-shaped paths can't be unit-tested (InMemory doesn't support `FOR UPDATE SKIP LOCKED`).

**Recommendation:** either (a) accept the integration-test-only coverage and document the trade-off, or (b) add unit tests that exercise the store via `InMemory` for the non-Postgres-specific paths and rely on integration for the SKIP LOCKED race. (a) is fine; (b) is the more complete answer.

### 1.4 [Severity: High] Untouched logic in `Comuki.Host` MCP module

The MCP JSON-RPC 2.0 server (`platform/src/host/Comuki.Host/Mcp/McpServer.cs`,
`JsonRpcEnvelope.cs`, `McpModuleEndpoints.cs`) is the **only place** that
parses wire-format JSON-RPC envelopes that `C#→TS codegen` does not yet
cover (per STATE.md "FE-side codegen еще не подключена — slice отложен до
v2"). Coverage today:

- `McpServer` is exercised indirectly via `McpShould.cs` integration test
  (`tests/integration/Comuki.Host.Integration.Smoke/McpShould.cs`)
- `JsonRpcEnvelope` types (`JsonRpcRequest`, `JsonRpcSuccess`, `JsonRpcError`,
  `ToolCallParams`, `ToolResult`, `ToolContentBlock`) have **zero direct
  tests** — only the integration smoke test calls into them
- Malformed JSON, missing fields, error code mapping — not directly tested

**Recommendation:** add a `tests/unit/Comuki.Host.Unit.Mcp/McpEnvelopeShould.cs`
that exercises the request/response parsing and the error-code mapping
without booting the host composition. The MCP server is a security boundary
(it accepts untrusted JSON-RPC input from the operator's tooling); unit-level
testing of the envelope layer is cheap and high-leverage.

### 1.5 [Severity: High] `Knowledge` module — ~0.33 tests/type

27 production types, 9 unit-test methods (`PgKnowledgeIngestorShould`).
The unit test `PgKnowledgeIngestorShould` is *itself* marked as a contract
test:

> `tests/unit/Comuki.Modules.Knowledge.Unit/PgKnowledgeIngestorShould.cs:52`
> `[Fact(DisplayName = "Integration contract — DB-bound paths covered by Comuki.Modules.Memory.Integration.Migrations under Testcontainers.PostgreSql")]`

That is, the test name literally states "this is not a real test, see the
integration suite". No integration suite exists for Knowledge either:

```
$ ls tests/integration | grep -i knowledge
(no output)
```

`OpenAIEmbeddingClient`, `PgKnowledgeSearcher`, `SourceDocumentConfiguration`,
`MemoryEmbeddingConfiguration` — all un-referenced. The "search" path the
MCP tool exposes (`mcp__search_knowledge`) has no integration test.

**Recommendation:** add `tests/integration/Comuki.Modules.Knowledge.Integration/PgKnowledgeSearcherShould.cs` against a `pgvector/pgvector:pg16` Testcontainer (the Memory module already proves the pattern).

### 1.6 [Severity: Medium] Proxy metering — gap continues from audit-product-report §2.9

The audit-product-report flagged: "every forwarded call writes a `usage_events` row with `source = 'proxy'` — **false** on the success path." The Proxy metering logic is covered by *unit* tests (60 test methods in `Comuki.Modules.Proxy.Unit` — the highest ratio in v1), but:

- `MeterUsageFromResponseAsync` is a **reserved no-op** in v1 — the only test (`ResponseTransformIsANoOpAsync`) asserts it does *nothing*. So the metering contract is untested.
- `UsageRecorder` (in Costs) is unit-tested but the proxy→costs flow on the success path has no integration assertion.

**Recommendation:** add an integration test that asserts "given a successful forwarded OpenAI call via the proxy, an `usage_events` row with `source='proxy'` and the right cost lands in the costs DB." Until that lands, the metering contract remains an unverified *intent* not a *fact*.

### 1.7 [Severity: Medium] Host endpoint contracts — zero direct test coverage

```
$ rg -l "ChatEndpointRunner|ChatSessionsController|IntakeEndpointRunner|AdmissionRulesController|InboxController|SourcesController|TicketsController|WebhooksController|RunsController|RunArtifactsController|AdmissionRulesController|SchedulerEndpointRunner|McpServer|ProjectsModuleEndpoints|CostsModuleEndpoints|ProxyModuleEndpoints|McpModuleEndpoints|KnowledgeModuleEndpoints|IntakeRunLauncher|SchedulerRunLauncher|ChatRunStarter" tests/ --type cs
\tests\integration\Comuki.Host.Integration.Auth\HostAuthServer.cs
\tests\integration\Comuki.Host.Integration.Chat\HostChatServer.cs
\tests\integration\Comuki.Host.Integration.Intake\HostIntakeServer.cs
\tests\integration\Comuki.Host.Integration.Oidc\HostOidcServer.cs
\tests\integration\Comuki.Host.Integration.Proxy\HostProxyServer.cs
\tests\integration\Comuki.Host.Integration.Realtime\HostRealtimeServer.cs
\tests\integration\Comuki.Host.Integration.Runs\RunDecisionsEndpointShould.cs
\tests\integration\Comuki.Host.Integration.Smoke\AdminShould.cs
\tests\integration\Comuki.Host.Integration.Smoke\KnowledgeShould.cs
\tests\integration\Comuki.Host.Integration.Smoke\McpShould.cs
```

10 host endpoints are covered indirectly by the integration smoke suite. The
risk: if `ChatEndpointRunner` has a regression that breaks `/api/v1/chat/*` for
a specific HTTP status mapping, only the smoke test fires (and only for the
happy path). Per-endpoint contract tests for status codes are sparse.

**Recommendation:** prioritise the 12 admin endpoints added in the post-1.0
slice (issue #31-#42) — these are operator-facing, security-relevant, and
already integrated but lack unit-level coverage of their validation logic
and authorization attributes.

### 1.8 [Severity: Low] Memory module — `UseInMemoryDatabase` instead of Testcontainers

`tests/unit/Comuki.Modules.Memory.Unit/EfMemoryStoreShould.cs` uses
`UseInMemoryDatabase` (line 185). InMemory cannot reproduce pgvector's exact
behaviour for similarity search; the store has an integration test in
`tests/integration/Comuki.Modules.Memory.Integration.Migrations/PlainPostgresMemoryShould.cs`
that uses Testcontainers + real pgvector. The unit test's "memory store CRUD"
coverage is fine, but similarity-search semantics are integration-only.

**Recommendation:** document the InMemory-vs-Testcontainers split on the test
class header so future contributors don't try to extend the unit test into
similarity-search territory.

---

## 2. Flakiness Risks

### 2.1 [Severity: Medium] `Task.Delay` in tests — 6 calls across 5 files

| File:line | Pattern | Risk |
|---|---|---|
| `tests/integration/Comuki.Host.Integration.Intake/HostIntakeServer.cs:173` | `await Task.Delay(TimeSpan.FromMilliseconds(100));` | inside `WaitForAsync` poll loop — **acceptable** (poll helper) |
| `tests/integration/Comuki.Host.Translator.Integration.PiCli/WorkerGrpcServerShould.cs:112` | `await Task.Delay(50, TestContext.Current.CancellationToken);` | **bare timing assumption** — relies on a 50 ms round-trip on a CI box |
| `tests/integration/Comuki.Modules.Memory.Integration.Migrations/MemorySweepWorkerShould.cs:113,142` | `await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);` | timing-based — could miss sweep on slow runners |
| `tests/unit/Comuki.Host.Unit.OidcSweeper/OidcStateSweeperShould.cs:123` | `await Task.Delay(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);` | **bare timing assumption** — relies on a 100 ms sweep tick |
| `tests/unit/Comuki.Modules.Projects.Unit/ProjectSettingsCacheRefresherShould.cs:81` | `await Task.Delay(TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);` | **bare timing assumption** — relies on a 50 ms cache refresh |

No `Thread.Sleep` anywhere (good — banned by the rules).

**Recommendation:** convert all 5 sites to either:
- **FakeTimeProvider** (already in widespread use across Engine.Compute + StatusMachine unit tests) and assert on `clock.Advance(...)` followed by the worker's reaction
- **`WaitForAsync(predicate, timeout)`** with a high-enough timeout (the pattern HostIntakeServer already uses)

Both remove the assumption that "50 ms is enough" on a CI box.

### 2.2 [Severity: Low] `DateTimeOffset.UtcNow` instead of `TimeProvider` — 170 hits in 50 files

Per `[time-and-wire-format.md]` §3 the wire is `DateTimeOffset` but the
*clock* should be an injected `TimeProvider` for testability. In production
code this is enforced via `TimeProvider` injection; in **tests**, `UtcNow`
shows up because tests use *real* time when seeding data:

```
$ rg -n "DateTimeOffset\.UtcNow" tests/ --type cs | wc -l
170
```

The hits are mostly in test setup (`var now = DateTimeOffset.UtcNow;`
followed by `Run.Create(..., now)`). This is *fine for tests that don't
assert on time-of-day semantics* — the unit is a wall-clock anchor, not a
behavioural clock. But tests that *do* assert on time progression (lease
expiry, sweeper windows) need to use the injected `TimeProvider` to be
deterministic. Most do — `ScheduledJobDispatcherWorkerShould` has
`private static readonly DateTimeOffset anchorTime = DateTimeOffset.Parse("2026-09-06T00:00:00Z", CultureInfo.InvariantCulture);`
and a `FixedClock` that wraps it. Some do not:

- `tests/unit/Comuki.Modules.Scheduler.Unit/CronExpressionShould.cs:97,113` — uses `DateTimeOffset.UtcNow` for "next fire" assertions. Wall-clock dependent.
- `tests/integration/Comuki.Host.Integration.Realtime/RunsHubShould.cs:179` — seeds `RunEvent.Create(..., DateTimeOffset.UtcNow)`. Fine (just a wall-clock anchor).

**Recommendation:** not a P0 — the existing pattern is mostly OK. The
~10 cases that *do* assert on time-of-day (lease expiry, sweeper intervals,
"ageSeconds" calculations) should be swept to use `FakeTimeProvider` for
determinism.

### 2.3 [Severity: Critical] Known skipped tests — 6 of 213 integration `[Fact]`s

5 OIDC Keycloak tests + 1 OIDC state sweeper test, all marked `[Fact(Skip = ...)]`
on the WSL2 dev sandbox:

```
tests/integration/Comuki.Host.Integration.Oidc/OidcKeycloakShould.cs:58,72,91,104,116
tests/integration/Comuki.Host.Integration.Oidc/OidcStateSweeperShould.cs:31
```

Skip reason (verbatim, `OidcKeycloakShould.cs:17`):

> Skip reason (2026-09-08): fixture requires a Testcontainers Postgres
> reachable from WSL2 within 30s. The host bootstrap fails with a Postgres
> connection timeout in this sandbox (no Docker DNS, only localhost bridge).
> Re-enable when run on a host with a properly configured Docker network or
> against a real Postgres.

**Impact:** 6 integration tests are dead in the WSL2 dev sandbox. The
WPF / SignalR / OIDC linker paths are *not* exercised end-to-end on any
developer machine that runs the suite via WSL2. Robustness of the OIDC
state-sweeper timeout logic specifically is unknown.

**Recommendation:** see §5 P1.

### 2.4 [Severity: Medium] `Respawn`-free cleanup → tests are not hermetic

There is **0** use of `Respawn` (or any equivalent TRUNCATE-based cleanup)
across the test suite:

```
$ rg -l "Respawn" tests/ --type cs
(no output)
```

`MigrateAsync` is called on every `InitializeAsync` (24+ integration test
fixtures, ~74 total calls). On `dbContext.Database.MigrateAsync`, EF Core
1) checks `__EFMigrationsHistory` and applies only the pending ones,
which means *if the schema already exists* the test reuses it (no
TRUNCATE, so test 1's rows persist into test 2's view). This is the
exact bug fixed in the audit-product-report's pre-existing-flakes list
(`Runs EscalationTimeoutSweeper (test seed cross-pollution)`,
`Scheduler.CreateAsync exception type mismatch`). Both have been *patched*
in the specific failing tests, but the underlying *flakiness pattern*
— no inter-test cleanup — remains.

**Recommendation:** add `Respawn` for the shared `ICollectionFixture<PostgresFixture>` (see §4.1) and call `Respawner.ResetAsync(...)` between every test. ~5ms per reset vs ~5s per Migrate = 1000× faster.

### 2.5 [Severity: Low] Hard-coded ports outside the 17000-17200 pool

`tests/integration/Comuki.Host.Translator.Integration.PiCli/PiRunnerShould.cs:27,57`
hard-code `OrchestratorGrpcUrl = new Uri("http://localhost:5051")`. The
project rules (`AGENTS.md` §9) require the port pool 17000-17200. Port
5051 is *not* a current pool conflict in the test environment, but it
*is* a violation of the documented policy.

**Recommendation:** port the suite to use `FreeTcpPort()` (the same helper
`HostAuthServer.cs:305` and 4 other integration fixtures use) and inject
the resolved port into the `TranslatorOptions`.

### 2.6 [Severity: Low] `Guid.NewGuid()` everywhere — no determinism

`Guid.NewGuid()` shows up 141 times in 55 files. This is *fine for the
test's own purposes* (each test gets a unique id, no test depends on
another test's id) but makes failure-diagnosis painful: a failing test's
log lines reference guids that won't reproduce on the next run. The
unit-test pattern is acceptable here because tests are isolated; the
*integration* tests that dump seed-row ids in error messages are not.

**Recommendation:** no program-wide change needed; if a specific test
becomes a flake magnet, freeze its seed (use `Guid.Parse("...")`).

---

## 3. Test Anti-patterns

### 3.1 [Severity: High] `_ =` discard pattern — 401 occurrences in 67 test files

The `[async-and-tasks.md] §6` and `[worker-audit.md] §2b` self-audit greps
both flag `_ =` as banned:

```csharp
// ❌ Wrong — discard поверх await (await уже ждёт синхронно)
_ = await db.Users.AddAsync(user, ct);
_ = await db.SaveChangesAsync(ct);
_ = await transaction.CommitAsync(ct);

// ❌ Wrong — discard на fluent-chain return
_ = services.AddSingleton<IFoo, Foo>();
```

`Worker-audit.md` claims this is *caught by the self-audit regex*, but
the regex `rg -n "^\\s*_\\s*=\\s" tests/ -type cs` is only run by the
*author* — the test projects are not part of any `[SelfAuditReport]` MSBuild
target scan. As a result, **401** discard sites shipped to master.

Top offenders:

| File | Discards |
|---|---:|
| `tests/unit/Comuki.Modules.Artifacts.Unit/RunArtifactPackagerShould.cs` | 23 |
| `tests/integration/Comuki.Engine.Orchestration.Integration.Queue/WorkItemQueueShould.cs` | 22 |
| `tests/integration/Comuki.Modules.Identity.Integration.Migrations/IdentityMigrationsShould.cs` | 21 |
| `tests/unit/Comuki.Modules.Identity.Unit/PermissionEvaluatorShould.cs` | 19 |
| `tests/unit/Comuki.Modules.Identity.Unit/OidcCallbackHandlerShould.cs` | 17 |
| `tests/unit/Comuki.Engine.Compute.Unit/DockerComputeProviderShould.cs` | 17 |
| `tests/unit/Comuki.Host.Unit.Realtime/SignalRRunEventsBroadcasterShould.cs` | 14 |
| `tests/unit/Comuki.Engine.Compute.Unit/KubernetesComputeProviderShould.cs` | 14 |
| `tests/unit/Comuki.Modules.Identity.Unit/OidcAccountLinkerShould.cs` | 14 |
| `tests/integration/Comuki.Modules.Projects.Integration.Migrations/ProjectsMigrationsShould.cs` | 14 |

**Recommendation:** the cleanest fix is *project-wide search-and-rewrite*
(run in a single PR — across 67 files it's a script's work, not 67
individual edits). After the cleanup, wire the `[SelfAuditReport]` MSBuild
target to also scan `tests/` so future regressions fail the build.

### 3.2 [Severity: Low] `[Fact]` without `DisplayName` — 216 of 1126 unit-test methods

```
$ rg -n "^\s*\[Fact\]\s*$" tests/unit --type cs | wc -l
216
```

19% of unit tests are missing the `Given X, when Y, then Z` `DisplayName`.
By file:

| File | Tests | Missing DisplayName |
|---|---:|---:|
| `tests/unit/Comuki.Shared.Filtering.Unit/FilterSortShould.cs` | 46 | 46 |
| `tests/unit/Comuki.Shared.Filtering.Unit/FilterOperatorsShould.cs` | 44 | 44 |
| `tests/unit/Comuki.Shared.Filtering.Unit/FilterExpressionParserAdversarialShould.cs` | 38 | 38 |
| `tests/unit/Comuki.Shared.Filtering.Unit/FilterableFieldRegistryShould.cs` | 21 | 21 |
| `tests/unit/Comuki.Engine.Compute.Unit/ScaleSupervisorCycleShould.cs` | 12 | 12 |
| `tests/unit/Comuki.Engine.Compute.Unit/WorkerPoolStateShould.cs` | 9 | 9 |
| `tests/unit/Comuki.Host.Translator.Unit.StreamJson/StreamJsonParserShould.cs` | 9 | 9 |
| `tests/unit/Comuki.Engine.Compute.Unit/DockerComputeProviderShould.cs` | 8 | 8 |
| `tests/unit/Comuki.Engine.Compute.Unit/WorkerTokenIssuerShould.cs` | 8 | 8 |
| `tests/unit/Comuki.Host.Translator.Unit.Runtime/PiEventToWorkerEventShould.cs` | 6 | 6 |
| `tests/unit/Comuki.Modules.Knowledge.DomainLayerTests/KnowledgeDomainLayerTests.cs` | 3 | 3 |
| `tests/unit/Comuki.Migrator.Unit/PostgresHelpers.cs` | 1 | 1 |

`[testing-unit.md] §2` says: "DisplayName обязателен для integration и
сложных unit. Для простых — опционален." Most of these 216 are *simple*
tests (sort direction, pagination boundaries, parser tokenization) and
they all carry XML `<summary>` doc-comments that serve as test
documentation in lieu of `DisplayName`. So this finding is **lower
severity** than it first looks — most are allowed by the rule.

The Filtering suite's `FilterExpressionParserAdversarialShould.cs`
(adversarial cases, including malformed input) is the one file where
`DisplayName` would meaningfully aid diagnostics — those tests' names
like `RejectUnterminatedStringLiteral` are technically the *method name*
but the failure mode (what kind of malformed input?) is opaque without
the DisplayName.

**Recommendation:** add `DisplayName` to the 38 tests in
`FilterExpressionParserAdversarialShould.cs` and the 6 tests in
`PiEventToWorkerEventShould.cs` (pi wire-format semantics are complex
enough that the test name alone doesn't convey intent). The other 172
are fine as-is.

### 3.3 [Severity: Low] Private methods in test classes — 189 across 95 files

`[class-layout-and-tooling.md] §1a` bans private methods in **production**
code; tests are explicitly exempt ("framework-mandated overrides only"
carve-out). 189 private methods in tests is therefore *not a violation*,
but the high count — especially in:

- `tests/unit/Comuki.Host.Unit.Auth/SubjectScopeMiddlewareShould.cs` (6)
- `tests/unit/Comuki.Engine.Compute.Unit/KubernetesComputeProviderShould.cs` (6)
- `tests/integration/Comuki.Host.Integration.Runs/EscalationTimeoutSweeperShould.cs` (5)

— suggests *test-class bloat* that could be split into helpers.

**Recommendation:** no P0 action; when touching any of these files in a
follow-up, extract the private helper into a `file static class` and add
`internal` access. Don't prioritise.

### 3.4 [Severity: Low] `#pragma warning disable` — 1 occurrence

`tests/integration/Comuki.Host.Integration.Artifacts/ArtifactsEndToEndShould.cs:49,53`:

```csharp
#pragma warning disable CS0612
private readonly MinioContainer minio = new MinioBuilder("minio/minio:latest")...
#pragma warning restore CS0612
```

This is the only `#pragma` in the test suite. The rule from `process/build-verification.md` and `~/.agents/agents-notes` is "#pragma = forbidden, always fix structurally." CS0612 means "marked obsolete" — using `MinioBuilder` is using an obsolete API.

**Recommendation:** check whether the Testcontainers.Minio API still ships
`MinioBuilder` or has moved to a non-obsolete builder. If obsolete,
either pin a non-obsolete Testcontainers.Minio version, or accept the
suppression with a "// boundary:" comment.

### 3.5 [Severity: Low] Log assertions — 6 in 5 files (all `ShouldContain`/`ShouldBe` on error messages)

```
$ rg -n "rejected\.ShouldContain|output\.ShouldBe.*catalog" tests --type cs
tests/unit/Comuki.Host.Brain.Unit/BrainToolboxShould.cs:88:rejected.ShouldContain("'ghost-profile' is not in the profile catalog");
tests/unit/Comuki.Host.Brain.Unit/BrainToolboxShould.cs:137:output.ShouldBe("profile catalog is empty");
tests/unit/Comuki.Host.Unit.RateLimit/RateLimitInstallerShould.cs:75:RateLimitPolicies.Login.ShouldBe("comuki.ratelimit.login");
tests/unit/Comuki.Modules.Identity.Unit/OidcCallbackHandlerShould.cs:60:result.RedirectTarget.ShouldStartWith("/login?reason=oidc-failed");
```

These aren't *log-message* assertions (which would be brittle), they're
*return-value string assertions* on error paths. Reasonable.

### 3.6 [Severity: Low] `try`/`finally` for env cleanup — mostly OK

12 `try` blocks in tests, 6 of them with `finally` cleanup. The 6 with
cleanup are in `BrainDatabaseShould.cs`, `BrainHostOptionsShould.cs`,
`ProfilesProviderShould.cs`, `WorkerCommandHandlerShould.cs`,
`MemoryFactSqlShould.cs`, `ProxyTransformsShould.cs`, `FakeUpstreamServer.cs`,
`EvalRunner.cs`, `EvalRunnerGoldenShould.cs`, `EvalJsonTaskParser.cs`. The
6 *without* `finally` are intentional (assertion-only tests).

The one **unclean** `SetEnvironmentVariable` is `tests/unit/Comuki.Host.Unit.ProductionSecrets/ProductionSecretValidatorShould.cs:24` calls — these run inside the `[Collection("MigratorEnvSafe")]`
disable-parallelization gate, so cross-test pollution is bounded.

`tests/integration/Comuki.Host.Integration.Proxy/HostProxyServer.cs:70` sets
`FAKE_OPENAI_KEY` and never unsets it — but again, in a per-class fixture
lifecycle, this is OK.

### 3.7 [Severity: Info] `MigrateAsync` calls in unit tests — 2 hits (acceptable)

`tests/unit/Comuki.Host.Unit.Realtime/GetRunDetailHandlerShould.cs:165` and
`RunDecisionAdaptersShould.cs:114` use `await context.Database.EnsureCreatedAsync();`
against `UseInMemoryDatabase`. InMemory can use either EnsureCreated or
Migrate — these are fine. The bigger pattern (tests that *should* use
Testcontainers but use InMemory) is in §1.8 and §3.8.

### 3.8 [Severity: Medium] `UseInMemoryDatabase` instead of Testcontainers — 9 unit-test files

```
tests/unit/Comuki.Engine.Orchestration.Unit.DbContext/OrchestrationDbContextShould.cs:140
tests/unit/Comuki.Host.Unit.Realtime/GetRunDetailHandlerShould.cs:162
tests/unit/Comuki.Host.Unit.Realtime/RunDecisionAdaptersShould.cs:111
tests/unit/Comuki.Modules.Chat.Unit/ChatDbContextShould.cs:96
tests/unit/Comuki.Modules.Costs.Unit/CostsDbContextShould.cs:86
tests/unit/Comuki.Modules.Identity.Unit/OidcStateStoreShould.cs:24
tests/unit/Comuki.Modules.Memory.Unit/EfMemoryStoreShould.cs:185
tests/unit/Comuki.Modules.Scheduler.Unit/ScheduledJobStoreShould.cs:220
tests/unit/Comuki.Modules.Scheduler.Unit/SchedulerDbContextShould.cs:85
```

Each one is annotated as a "test that doesn't need real Postgres." The
issue: InMemory **silently diverges from Postgres in 3 known ways**:

1. `FOR UPDATE SKIP LOCKED` and concurrency semantics — untested
2. `ExecuteUpdate` / `ExecuteDelete` semantics — untested
3. Schema-level defaults (uuid defaults, generated columns) — not enforced

The scheduler unit test explicitly admits this:

> `ScheduledJobStoreShould.cs:14`: "The due query's `FOR UPDATE SKIP LOCKED`
> clause is Postgres-only and cannot run against the in-memory provider —
> that path is covered by the end-to-end integration test that boots a real
> Testcontainers Postgres."

So the discipline is *acknowledged* — the integration tests pick up the
slack. **The risk is regression of that discipline**: if someone deletes
or skips the integration test, the unit test's green doesn't reflect reality.

**Recommendation:** add a `[Trait("Category", "InMemoryBacked")]` attribute
to all 9 files and an `[Fact(Skip = "...")]` warning in `AssemblyInfo.cs`
that says "Integration coverage is in `tests/integration/...` — do not
extend these unit tests to claim SKIP LOCKED / ExecuteUpdate semantics."

---

## 4. Integration Test Brittleness

### 4.1 [Severity: Critical] 19 of 21 host integration projects spin up per-class PostgreSQL containers

The integration test suite uses testcontainers.PostgreSql in **31** files
(`rg -l "Testcontainers" tests/integration --type cs | wc -l`). Of those,
only **2** use `ICollectionFixture` + `[CollectionDefinition]` to share a
single Postgres across test classes:

```
tests/integration/Comuki.Host.Integration.Auth/AuthIntegrationCollection.cs:13
tests/integration/Comuki.Host.Integration.Intake/IntakeHostCollection.cs:21
```

The other 19+ integration test projects each:
- Build a fresh `PostgreSqlContainer` in `IAsyncLifetime.InitializeAsync`
- Call `await container.StartAsync(cancellationToken)` (~5-15s cold start)
- Migrate 3-6 `DbContext`s sequentially (`OrchestrationDbContext`,
  `IdentityDbContext`, `ProjectsDbContext`, `ChatDbContext`, `IntakeDbContext`,
  `ArtifactsDbContext`, etc. — 5-30s)
- Bind on `http://127.0.0.1:{FreeTcpPort()}` (~0.5s)
- Tear down at `DisposeAsync` (~2s)

That's **~10-50 seconds per test class** before any test runs. The full
integration suite (with 30+ test classes across these 19 projects)
incurs **~5-30 minutes** of pure container-startup overhead, on top of
test execution. On CI runners this is the difference between a 10-min and
a 60-min build.

**Recommendation (P1):**

1. Extract a shared `Comuki.Host.Integration.Testing.PostgresFixture` (in
   `tests/testing/` per `[testing-integration.md] §6`) that builds ONE
   container with `WithReuse(true)` and migrates ALL 9 module DbContexts
   ONCE per `IClassFixture<>` lifetime.
2. Add a `Respawn` instance shared by that fixture; expose a
   `ResetAsync()` method that the per-test setup runs in its
   `IAsyncLifetime.InitializeAsync` between tests.
3. Convert each existing test class to use `IClassFixture<PostgresFixture>`
   instead of building its own container. Same host composition per
   test class (Testcontainers containers don't share state across
   classes, but the host itself can be constructed cheaply against the
   shared Postgres).
4. For OIDC + Realtime + Chat where the test class needs full host
   composition (12 classes), the cost goes from "fresh Postgres + migrate"
   to "fresh host composition against shared Postgres" — saves 30-50s per
   class.

This is *the single biggest CI cost reduction* available today.

### 4.2 [Severity: High] `Respawn` not used

0 occurrences across `tests/`. Cleanup is `MigrateAsync` (heavy; doesn't
truncate existing data; tests share rows).

```
$ rg -l "Respawn" tests/
(no output)
```

Per `[testing-integration.md] §2` and §4:

> Respawn — `TRUNCATE` all tables, keeps structure. ~100ms/50 tables. Reset
> at the start of each test: `InitializeAsync() => factory.ResetDatabaseAsync();`.

**Recommendation:** add `Respawn` 6.x to a shared testing project and use it
between every test in the shared `PostgresFixture` (§4.1).

### 4.3 [Severity: Medium] `WithReuse(true)` not used

```
$ rg -l "WithReuse" tests/
(no output)
```

Cold-start cost: 5-15s per container. With `WithReuse`, the same container
is reused across test runs (only the schema is migrated). Combined with §4.1,
this saves another 5-15s per test class on local dev runs.

### 4.4 [Severity: Medium] Hard-coded timeouts in integration helpers

```
tests/integration/Comuki.Host.Integration.Intake/SyncBackShould.cs:52  TimeSpan.FromSeconds(20)
tests/integration/Comuki.Host.Integration.Intake/SyncBackShould.cs:66  TimeSpan.FromSeconds(10)
tests/integration/Comuki.Host.Integration.Realtime/HostRealtimeServer.cs:55 TimeSpan.FromSeconds(30) HubTimeout
tests/integration/Comuki.Host.Translator.Integration.PiCli/TranslatorE2EShould.cs:97,123 TimeSpan.FromSeconds(60)
tests/integration/Comuki.Host.Translator.Integration.PiCli/WorkerGrpcServerShould.cs:118 TimeSpan.FromSeconds(10)
tests/integration/Comuki.Modules.Intake.Integration.Migrations/IntakeMigrationsShould.cs:207 TimeSpan.FromSeconds(30)
tests/integration/Comuki.Modules.Memory.Integration.Migrations/MemorySweepWorkerShould.cs:115,147 TimeSpan.FromSeconds(10)
tests/integration/Comuki.Engine.Orchestration.Integration.Queue/WorkItemQueueShould.cs:99,143,178 TimeSpan.FromMinutes(2).Add(TimeSpan.FromSeconds(31))
```

`TimeSpan.FromMinutes(2).Add(TimeSpan.FromSeconds(31))` is a curious value
— 2:31. This is `lease_timeout + 30s grace` for the reaper test. It's
correct but undocumented; should be a named constant.

The 30s `HubTimeout` (Realtime) and 60s (Translator) are CI-flake-prone.
Hub-timeout on a CI box with cold Docker can hit this.

**Recommendation:** extract named constants:
```csharp
private static readonly TimeSpan RealtimeHubTimeout = TimeSpan.FromSeconds(30);
private static readonly TimeSpan TranslatorBootTimeout = TimeSpan.FromSeconds(60);
```
…and document each one. Don't change the values — they were picked for a
reason.

### 4.5 [Severity: Low] `MigrateAsync` called inside `InitializeAsync` per test class — total 74 occurrences

Each call is ~5s of `__EFMigrationsHistory` check + applying migrations.
After §4.1 §4.2 conversion, the shared fixture migrates ONCE per process
(not per class).

### 4.6 [Severity: Info] `IAsyncLifetime` adoption — 29 of 30+ integration test classes use it correctly

`rg -l "IAsyncLifetime" tests/integration --type cs` returns 29 matches,
covering nearly every integration test fixture. Only the smoke test that
uses `WebApplicationFactory`-style composition doesn't (and that's OK
because it runs against the live compose stack).

### 4.7 [Severity: Medium] `docker-compose` Postgres missing from CI lanes

The smoke tests (`tests/integration/Comuki.Host.Integration.Smoke`) need a
real `comuki-compose` stack to run. Per STATE.md they run against the
launched compose in `deploy/`. There is no evidence in the test code of
testcontainers-based Compose-equivalent for the smoke lane. If the CI
runner doesn't have `comuki-compose` running, smoke tests fail.

**Recommendation:** either (a) document the CI dependency, or (b) extract
the smoke tests' prerequisites (Postgres + MinIO + the host) into a
testcontainers-based fixture so smoke runs anywhere.

### 4.8 [Severity: Info] No shared `*.Testing` library project

`[testing-integration.md] §6` prescribes `tests/<App>.Testing/` — a
library project (no `[Fact]`, CI builds but doesn't `dotnet test` it) that
holds shared `Fixtures/`, `Factories/`, `Bases/`, `Extensions/`,
`Builders/`. The current tree has **no such project**:

```
$ ls tests/
Comuki.Architecture.Tests/
integration/
load/
tools/
unit/
```

No `tests/Comuki.*.Testing/` directory. `FakeTimeProvider` (4 copies),
`FixedClock` (3 copies), `WaitForAsync` (1 copy), `TempControlPlaneRoot`
(2 copies), `FakeGithubSyncPort` (1 copy), `FakeUpstreamServer` (1 copy)
are all duplicated per test file. There is a `Comuki.TestFakePi` project
but that's a *worker fakes executable* for the Translator pipeline, not
a shared library for tests.

**Recommendation:** create `tests/Comuki.Platform.IntegrationTesting/` (a
class library, not a test project) with `PostgresFixture`,
`HostComposerFixture`, `Clocks/FakeTimeProvider.cs`, `Builders/UserBuilder.cs`,
`Extensions/WaitForAsync.cs`. Move the 9 duplicates. After: shared
infrastructure lives in one place and adding the shared
`PostgresFixture` (§4.1) is a smaller diff.

### 4.9 [Severity: Info] `WebApplicationFactory<Program>` not used — by project choice

`[testing-integration.md] §3, §6` prescribes `WebApplicationFactory<Program>`
for HTTP access. Comuki uses a different pattern: each integration test
boots the host manually:

```csharp
var builder = WebApplication.CreateBuilder(
    new WebApplicationOptions { ApplicationName = typeof(HostComposer).Assembly.GetName().Name, EnvironmentName = Environments.Development });
builder.Host.UseDefaultServiceProvider(static options => { options.ValidateOnBuild = false; options.ValidateScopes = false; });
builder.WebHost.UseUrls($"http://127.0.0.1:{FreeTcpPort()}");
...
application = HostComposer.Compose(builder, HostDatabase.Explicit(connectionString), validateOnBuild: false);
await application.StartAsync(cancellationToken);
```

This is **by design** per `AGENTS.md §Critical Non-Obvious Patterns`:

> Program.cs top-level, **без** `public partial class Program` — тесты через
> `internal HostComposer.Compose` + IVT

The trade-off: each integration test pays the cost of `CreateBuilder +
StartAsync` (~0.5-1s) instead of using WAF's optimised re-use path.
Combined with the per-class container (§4.1), this adds up.

**Recommendation:** document the trade-off in the testing-integration.md
or accept the cost. Switching to `WebApplicationFactory<>` would require
making `Program` partial-public, which the project has explicitly
chosen not to do.

---

## 5. Recommendations (no code)

### Priority 1 — must-fix before any further shipping

1. **Add `ICollectionFixture` + Respawn + WithReuse** to a shared
   `tests/testing/Comuki.Host.Integration.Testing/PostgresFixture.cs`.
   Convert the 19 host integration projects to use it. *Single biggest CI
   cost reduction available.* (4.1, 4.2, 4.3)

2. **Re-enable the 6 skipped OIDC tests** by either:
   - Pointing the `HostOidcServer` at a CI-managed Postgres (not
     Testcontainers) with a long-enough startup timeout
   - OR: rewrite `HostOidcServer` to use a Testcontainers Postgres with
     `WithNetworkAliases("comuki-host-postgres")` and have the host resolve
     the alias instead of localhost
   Until this lands, the OIDC linker + state-sweeper paths have *no*
   end-to-end coverage on the WSL2 dev sandbox — that's a security gap.

3. **Add a `MCP envelope` unit test suite** (`tests/unit/Comuki.Host.Unit.Mcp/McpEnvelopeShould.cs`).
   The MCP server is the boundary that parses untrusted JSON-RPC input —
   zero direct coverage today. The 9 JSON-RPC types in
   `platform/src/host/Comuki.Host/Mcp/JsonRpcEnvelope.cs` should round-trip
   through parsing, error mapping, and malformed-input rejection without
   booting the host.

4. **Add a Knowledge integration test suite.** No `tests/integration/Comuki.Modules.Knowledge.*`
   directory exists. The `pgvector`-backed search path that the MCP
   `search_knowledge` tool exposes is uncovered end-to-end.

5. **Run a project-wide `_ =` discard cleanup.** 401 sites in 67 files,
   all in violation of `[async-and-tasks.md] §6` and
   `[worker-audit.md] §2b`. Single PR, scripted find-and-replace. After:
   wire `[SelfAuditReport]` MSBuild target to also scan `tests/**` so
   future regressions fail the build.

### Priority 2 — must-fix within 30 days

6. **Bring DisplayName coverage on `[Fact]` to 100%.** 216 unit tests
   (19%) are missing it. Concentrated in `Comuki.Shared.Filtering.Unit`
   (149/165) and `Comuki.Engine.Compute.Unit` (51/88). Add a temporary
   `[Fact(Skip = "needs DisplayName")]` to gate the rollout, fix all
   sites, remove the gate.

7. **Add integration coverage for proxy success-path metering.** Per
   `audit-product-report.md:929`, the success path doesn't write
   `usage_events` rows. An integration test
   ("Given a successful forwarded OpenAI call via the proxy, when the
   response arrives, then an `usage_events` row with `source='proxy'`
   and the right cost lands in the costs DB") is the right gate.

8. **Convert `Task.Delay` sites in `MemorySweepWorkerShould.cs:113,142`,
   `WorkerGrpcServerShould.cs:112`, `OidcStateSweeperShould.cs:123`,
   `ProjectSettingsCacheRefresherShould.cs:81`** to `WaitForAsync` or
   `FakeTimeProvider`. Each is a 50-200 ms bare timing assumption.

9. **Add Knowledge + Identity cross-module coverage tests** in the
   existing `tests/integration/Comuki.Host.Integration.Projects` (already
   covers ProjectScopeShould + IdentityAdminEndpointsShould). The
   Project × Identity × PermissionEvaluator combinations are the
   security-relevant surfaces.

10. **Move the 9 `UseInMemoryDatabase` files** behind a `[Trait("Category", "InMemoryBacked")]`
    + AssemblyInfo gate that asserts they don't accidentally claim
    Postgres-specific semantics.

11. **Add per-endpoint contract tests** for the 12 admin endpoints
    (issues #31–#42, post-1.0 slice). Today they're only covered by the
    smoke suite happy-path. Each endpoint has unique validation +
    permission requirements that warrant direct unit coverage.

12. **Replace the `#pragma warning disable CS0612`** at
    `ArtifactsEndToEndShould.cs:49,53` with either a non-obsolete
    Testcontainers.Minio API call, or a documented boundary-comment.

### Priority 3 — post-ship / v2 backlog

13. **Document the InMemory-vs-Testcontainers split on each `UseInMemoryDatabase`
    unit test's XML doc** so future contributors don't extend into
    Postgres-specific territory.

14. **Extract private methods in test files** into `file static class`
    helpers when those files are touched next (189 sites across 95
    files; low priority, but `SubjectScopeMiddlewareShould.cs` (6
    privates) and `KubernetesComputeProviderShould.cs` (6) are the
    biggest smells).

15. **Add a TagBasedTestRunner** so unit-only (`--filter
    "FullyQualifiedName~Unit"`) runs without booting Docker. Today the
    full `dotnet run --project <integration>` suite requires a Docker
    daemon, even for tests that don't use it.

16. **Port `PiRunnerShould.cs` to `FreeTcpPort()`** instead of hard-coding
    `:5051`. The AGENTS.md §9 port-pool policy is being violated.

17. **Centralise the 9 `FakeTimeProvider` declarations** (currently
    duplicated in `Comuki.Engine.Compute.Unit.FakeTimeProvider`,
    `MergeBatchServiceShould.MergeBatchFakeTimeProvider`,
    `MergeQueueServiceShould.MergeQueueFakeTimeProvider`,
    `ScheduledJobDispatcherWorkerShould.FixedClock`,
    `RunArtifactPackagerServicePollOnceShould.FixedClock`,
    `ScheduledJobServiceShould.FixedClock`,
    `BrainHostOptionsShould.SetEnvironment`). Move into a shared
    `tests/testing/Comuki.Platform.Testing/Clocks/` project.

18. **Add `[SelfAuditReport]` MSBuild target coverage for tests/**.**
    Today the target only scans `src/`; if it also scanned `tests/**`
    the `_ =` discard ban and the `[Fact(DisplayName = "Given X, when Y, then Z")]`
    convention could be enforced at build time.

19. **Track the 369 "unreferenced" types as a coverage signal**, not a
    checklist. Most are EF Configurations / DTOs / installer extensions
    that don't need direct tests. Tag them with
    `[Trait("CoverageReason", "ConfigurationOrDto")]` so they don't
    pollute coverage reports.

---

## Appendix: Files Reviewed

### Test infrastructure (read fully or skimmed)

- `tests/integration/Comuki.Host.Integration.Auth/HostAuthServer.cs` (322 LOC)
- `tests/integration/Comuki.Host.Integration.Auth/AuthIntegrationCollection.cs` (15 LOC)
- `tests/integration/Comuki.Host.Integration.Intake/HostIntakeServer.cs` (261 LOC)
- `tests/integration/Comuki.Host.Integration.Intake/SyncBackShould.cs` (79 LOC)
- `tests/integration/Comuki.Host.Integration.Artifacts/ArtifactsEndToEndShould.cs` (361 LOC)
- `tests/integration/Comuki.Host.Integration.Oidc/OidcKeycloakShould.cs` (132 LOC)
- `tests/integration/Comuki.Host.Integration.Oidc/OidcStateSweeperShould.cs` (74 LOC)
- `tests/integration/Comuki.Host.Integration.Realtime/HostRealtimeServer.cs` (356 LOC)
- `tests/integration/Comuki.Host.Integration.Proxy/HostProxyServer.cs` (162 LOC)
- `tests/integration/Comuki.Host.Integration.Runs/EscalationTimeoutSweeperShould.cs` (268 LOC)
- `tests/integration/Comuki.Host.Translator.Integration.PiCli/PiRunnerShould.cs` (83 LOC)
- `tests/integration/Comuki.Engine.Orchestration.Integration.Queue/WorkItemQueueShould.cs` (333 LOC)

### Unit test suites (read fully or skimmed)

- `tests/unit/Comuki.Modules.Intake.Unit/WebhookVerifiersShould.cs` (75 LOC)
- `tests/unit/Comuki.Engine.Orchestration.Unit.StatusMachine/RunTrustClassShould.cs` (122 LOC)
- `tests/unit/Comuki.Engine.Orchestration.Unit.StatusMachine/WorkItemStatusMachineShould.cs` (177 LOC)
- `tests/unit/Comuki.Engine.Orchestration.Unit.Eval/EvalRunnerShould.cs` (194 LOC)
- `tests/unit/Comuki.Modules.Chat.Unit/ChatGraphShould.cs` (206 LOC)
- `tests/unit/Comuki.Modules.Scheduler.Unit/ScheduledJobStoreShould.cs` (224 LOC)
- `tests/unit/Comuki.Modules.Scheduler.Unit/ScheduledJobDispatcherWorkerShould.cs` (243 LOC)
- `tests/unit/Comuki.Modules.Scheduler.Unit/ScheduledJobServiceShould.cs` (157 LOC)
- `tests/unit/Comuki.Modules.Proxy.Unit/ProxyTransformsShould.cs` (207 LOC)
- `tests/unit/Comuki.Engine.Compute.Unit/WorkerTokenIssuerShould.cs` (123 LOC)
- `tests/unit/Comuki.Engine.Compute.Unit/WorkerPoolStateShould.cs` (176 LOC)
- `tests/unit/Comuki.Modules.Artifacts.Unit/RunArtifactPackagerServicePollOnceShould.cs` (380 LOC)
- `tests/unit/Comuki.Modules.Memory.Unit/MemoryFactSqlShould.cs` (37 LOC)
- `tests/unit/Comuki.Host.Translator.Unit.Runtime/WorkerCommandHandlerShould.cs` (133 LOC)

### Production code spot-checked (named in coverage gap findings)

- `platform/src/shared/Comuki.Shared.Filtering/Parser/FilterNode.cs` and related
- `platform/src/host/Comuki.Host/Mcp/McpServer.cs`, `JsonRpcEnvelope.cs`
- `platform/src/modules/Knowledge/Comuki.Modules.Knowledge.Application/IKnowledgeSearcher.cs`
- `platform/src/engine/Comuki.Engine.Orchestration/Infrastructure/Hosting/{EscalationTimeoutWorker,LeaseReaperWorker}.cs`
- `platform/src/modules/Verify/Comuki.Modules.Verify.Domain/`

### Counts summary

- Production: 852 `.cs` files, 869 public type declarations
- Tests: 267 `.cs` files (205 unit + 52 integration + 9 architecture + 1 load JS + 1 tools)
- Test methods: 1126 unit `[Fact]`/`[Theory]`, 208 integration `[Fact]`, 28 architecture `[Fact]`
- Test files with private methods: 95 (189 private methods total)
- Test files using `_ =` discard: 67 (401 sites total)
- Test files using `DateTimeOffset.UtcNow`: 50 (170 sites total)
- Test files using `Task.Delay`: 5 (6 sites total)
- Test files using `Environment.SetEnvironmentVariable`: 11 (48 sites total)
- Test files using `UseInMemoryDatabase`: 9
- Test files using `Testcontainers`: 31
- Test files using `ICollectionFixture`: 2 (`AuthIntegrationCollection`, `IntakeHostCollection`)
- Test files using `IClassFixture`: 13
- Test files using `Respawn`: 0
- Test files using `WithReuse`: 0
- Test files with `[Fact(Skip = ...)]`: 2 (`OidcKeycloakShould.cs`, `OidcStateSweeperShould.cs`)
- `[Fact]` without `DisplayName` in `tests/unit/`: 216
- Public types without a word-boundary test reference: 369 (filtered: 49 EF Configurations, 110 wire-format DTOs, 31 installer extensions, 24 source-gen attributes, ≈155 controllers/endpoint-runnables covered by integration tests)
- Coverage gaps by module (logic-bearing types, post-filter): see §1.3 + §1.4 + §1.5 + §1.6

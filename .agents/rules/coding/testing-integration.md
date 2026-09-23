---
description: integration tests — real Postgres via Testcontainers, HostComposer.Compose (not WebApplicationFactory), Comuki.Host.Testing shared fixture library, xUnit v3 ValueTask lifetime, target end-state ICollectionFixture + Respawn + WithReuse (WS2)
globs: ["tests/integration/**/*.cs", "tests/integration/**/*.csproj"]
always: true
---

# Integration tests (comuki)

Project-specific overlay, not a copy of the canon — read
`~/.agents/rules/csharp/testing-integration.md` first for the general
shape (fixture/factory/Respawn vocabulary); this file documents where
comuki's real pattern differs, and marks clearly what is **current
practice** vs the **target end-state** this same OpenSpec change (WS2)
builds toward. Unit tests: `testing-unit.md` (this directory).

## 1. HTTP surface — `HostComposer.Compose`, NOT `WebApplicationFactory<Program>`

The canon template assumes `WebApplicationFactory<Program>`. Comuki does
**not** use it, by explicit project decision
(`AGENTS.md` — `Program.cs` is top-level, no `public partial class
Program`). Every integration suite boots the real host manually:

```csharp
var builder = TestHostBuilder.Create(connectionString);   // WebApplication.CreateBuilder + host-wide test flags
builder.Configuration["ControlPlane:Root"] = controlPlane.Root;
// ...suite-specific configuration...

application = HostComposer.Compose(builder, HostDatabase.Explicit(connectionString), validateOnBuild: false);
baseAddress = await TestHostBuilder.StartAsync(application, cancellationToken);
```

`TestHostBuilder` (`tests/integration/Comuki.Host.Testing/TestHostBuilder.cs`)
is the shared piece: random loopback port via `FreeTcpPort`, DI validation
off, `Environments.Development` (deliberate — the production-secret
validator short-circuits on non-`Production`), console logging cleared,
`auth:publicHost:publicUrl` pre-set to the loopback address (the
`AuthPublicHostOptions` binder validates on start). Trade-off accepted:
each suite pays `CreateBuilder + StartAsync` (~0.5–1s) instead of WAF's
optimised reuse path — documented, not a bug to fix locally.

## 2. xUnit v3 `IAsyncLifetime` returns `ValueTask`, not `Task`

Load-bearing deviation from the canon example and from xUnit v2 muscle
memory: in xUnit v3, `IAsyncLifetime.InitializeAsync()` /
`.DisposeAsync()` are `ValueTask`, not `Task`.

```csharp
public sealed class HostFooServer : IAsyncLifetime
{
    public async ValueTask InitializeAsync() { /* ... */ }
    public async ValueTask DisposeAsync() { /* ... */ }
}
```

Neither override takes a `CancellationToken` (fixed interface signature —
the "CancellationToken last" rule doesn't apply to interface overrides,
canon `testing-stack-and-pyramid.md`). Use the ambient
`TestContext.Current.CancellationToken` inside the body for any call that
needs one (container start, migrations, HTTP calls) instead of
`CancellationToken.None`.

## 3. Real DB — Testcontainers, pgvector image

```csharp
private readonly PostgreSqlContainer container = new PostgreSqlBuilder("pgvector/pgvector:pg16").Build();
```

Comuki uses `pgvector/pgvector:pg16`, not the plain `postgres:16-alpine`
the canon example shows — the Memory/Knowledge modules need the `vector`
extension, and every suite migrates every module's `DbContext` through the
same image (`HostDatabaseMigrator.MigrateAllAsync(connectionString, ct)`),
so one image is used everywhere rather than picking per-suite.

## 4. Current pattern (as of this audit) vs target end-state (WS2)

**Current — per-class container, no reuse, no Respawn.** 19 of 21 host
integration projects each build their own `PostgreSqlContainer` in
`IAsyncLifetime.InitializeAsync`, migrate 3–6 `DbContext`s, and tear down
in `DisposeAsync` (`IClassFixture<HostFooServer>`, 1:1 with the test
class). No `WithReuse(true)`, no `Respawn` — cleanup between tests relies
on `MigrateAsync` re-checking `__EFMigrationsHistory`, which does **not**
truncate existing rows. Cost: ~10–50s of container/migrate overhead per
test class. This is real, current comuki code — copy it for a new suite
today, not the target shape below, unless WS2 has already landed (check
`tests/integration/Comuki.Host.Testing/Fixtures/` for
`PostgresCollectionFixture.cs` before deciding).

```csharp
[CollectionDefinition(nameof(AuthIntegrationCollection), DisableParallelization = true)]
public sealed class AuthIntegrationCollection : ICollectionFixture<HostAuthServer>;
```

Only **2** of 21 projects share a host across classes via
`[ICollectionFixture]` + `[CollectionDefinition]` today: `Auth`
(`AuthIntegrationCollection`) and `Intake` (`IntakeHostCollection`). Most
projects are still plain `IClassFixture<HostFooServer>`, one container per
class — that is the common case to follow, not the exception.

**Target (WS2 of this OpenSpec change — not yet landed)**: a
`PostgresCollectionFixture` in `Comuki.Host.Testing`
(`ICollectionFixture` + `[CollectionDefinition(..., DisableParallelization
= true)]`, `WithReuse(true)`) that migrates all module `DbContext`s once
per test-run process, plus a `Respawn`-based `ResetAsync()` called from
each test's own setup:

```csharp
public async ValueTask InitializeAsync() => await fixture.ResetDatabaseAsync();
```

Neither `Respawn` nor any `WithReuse(true)` call exists in this repo
today — `Directory.Packages.props` has no `Respawn` package reference, and
`rg -l "WithReuse" tests/` returns nothing. WS2 adds the package and the
fixture. **Don't invent a bespoke shared-fixture pattern ahead of WS2** —
one canonical shared fixture, not several competing ones; if you need
faster integration tests before WS2 lands, either accept the current
per-class cost or coordinate with WS2 directly rather than partially
reimplementing it.

## 5. Shared test infrastructure — `Comuki.Host.Testing` is the real `*.Testing` project

The canon's `tests/<App>.Testing/` prescription already exists, for real,
at `tests/integration/Comuki.Host.Testing/` — a class library (**no**
`[Fact]`), explicitly skipped by CI's integration glob loop
(`.github/workflows/ci.yml`: `if [ "$name" = "Comuki.Host.Testing" ]; then continue; fi`).
Current contents: `TestHostBuilder`, `HostDatabaseMigrator`, `FreeTcpPort`,
`TempControlPlaneRoot`, `TestBootstrapAdmin`, `TestArtifactsSecrets`,
`MinioImage`. WS2 adds `Fixtures/PostgresCollectionFixture.cs` here and
consolidates test-double duplicates that currently exist per-file
(`FakeTimeProvider` ×4, `FixedClock` ×3, `TempControlPlaneRoot` ×2) —
`WaitForAsync`, `FakeGithubSyncPort`, `FakeUpstreamServer` are already
single-copy and stay where they are.

## 6. Not in an integration test

- `Thread.Sleep` — never; `Task.Delay` only inside a `WaitForAsync`
  poll-with-timeout helper, never as a bare timing assumption (the audit
  found several bare 50–200ms `Task.Delay` calls that are flake risks on a
  slow CI box — don't add more; convert to `WaitForAsync` or
  `FakeTimeProvider` when you touch that file).
- Hard-coded ports outside the 17000–17200 pool (`AGENTS.md` §9). Use
  `FreeTcpPort.Next()`. One known, tracked violation exists today
  (`PiRunnerShould.cs` hard-codes `:5051`) — a documented gap, not a
  pattern to copy.
- Inter-test dependencies — each test/class starts from its own migrated
  schema today, or a Respawn-reset shared one post-WS2. Never rely on row
  state a previous test left behind.
- Manual `[Trait("Category", "Integration")]` — the project name already
  says integration (`tests/integration/**`).

## 7. Local runtime — Podman

This repo runs Testcontainers against **Podman**, not Docker Desktop, on
local dev/CI. Full setup (`DOCKER_HOST`, `TESTCONTAINERS_RYUK_DISABLED`,
why Ryuk needs disabling under rootless Podman) is documented in
`.agents/rules/process/local-test-runtime.md` — authored in parallel by a
different workstream of this same change; if that file doesn't exist yet
when you read this, it's still in flight, not abandoned.

## 8. Coverage

Same 70% line floor as unit tests (`testing-unit.md` §5), via
`Directory.Build.props`. No separate integration-specific threshold.

## Related

- `testing-unit.md` (this directory) — unit tests.
- Canon: `~/.agents/rules/csharp/testing-integration.md` (fixture/Respawn
  vocabulary — its `WebApplicationFactory` examples don't apply here, see
  §1), `~/.agents/rules/csharp/testing-stack-and-pyramid.md`.
- `.agents/docs/audits/testing-audit-report.md` §4 — source of the
  per-class-container / no-Respawn / no-WithReuse findings in §4 above;
  re-verify before citing further, audits age.
- `.agents/rules/process/local-test-runtime.md` — Podman local runtime
  (parallel workstream, may not exist yet).

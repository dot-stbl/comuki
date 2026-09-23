---
description: unit tests — xUnit v3/MTP runner, dotnet run --project, <Subject>Should naming, Shouldly/NSubstitute as actually used, comuki-specific anti-patterns (discard ban, TimeProvider, coverage floor)
globs: ["tests/unit/**/*.cs", "tests/unit/**/*.csproj"]
always: true
---

# Unit tests (comuki)

Project-specific overlay, not a copy of the canon. Full AAA / naming /
`[Theory]` / Shouldly / NSubstitute syntax reference lives in the
user-global canon — **read `~/.agents/rules/csharp/testing-unit.md` first**;
this file only documents where comuki's real runner, packages, and
practice diverge or need grounding in actual code. (`881ce7fe` dropped the
in-repo canon *copies* — `CODING-RULES.md`, the old `TESTING-RULES.md` —
because a duplicate under a mismatched path silently shadowed the real
rule. This file is not that: everything below is verified against comuki's
own `tests/unit/**`, not restated boilerplate.)

Stack + unit-vs-integration decision tree: canon
`~/.agents/rules/csharp/testing-stack-and-pyramid.md` — **its example
tables are for a different app** (xUnit v2/VSTest, `Bogus`,
`WebApplicationFactory`); comuki's actual numbers are below. Integration
tests: `testing-integration.md` (this directory).

## 1. Runner — xUnit v3 on Microsoft Testing Platform (MTP)

```bash
dotnet run --project tests/unit/Comuki.Engine.Orchestration.Unit.StatusMachine -c Debug
```

**Never `dotnet test`** — VSTest cannot discover xUnit v3/MTP tests; CI
(`.github/workflows/ci.yml`) and every local invocation use `dotnet run
--project <csproj>`. Exit code 8 means "zero tests ran" (e.g. every case in
the suite is `[Fact(Skip = ...)]`), not a runner crash — CI's own log-parse
step treats it separately from a real failure; don't assume non-zero exit
is always a bug.

Csproj shape (central package management is on — no explicit `Version` on
`<PackageReference>`, versions live in root `Directory.Packages.props`):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>Comuki.Modules.Foo.Unit</RootNamespace>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit.v3" />
    <PackageReference Include="Shouldly" />
    <PackageReference Include="NSubstitute" />
    <PackageReference Include="coverlet.collector" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\..\platform\src\modules\Foo\Comuki.Modules.Foo\Comuki.Modules.Foo.csproj" />
  </ItemGroup>
</Project>
```

Current pinned versions (`Directory.Packages.props`): `xunit.v3` 3.2.2,
`Shouldly` 4.3.0, `NSubstitute` 5.3.0, `coverlet.collector` 10.0.1. No
`Bogus` package exists in the dependency graph today — don't reach for it
in new tests without adding it to `Directory.Packages.props` first (see §4).

## 2. Naming, DisplayName, `[Theory]`

Matches the canon exactly, verified against real suites — no comuki
deviation: `<Subject>Should` class, PascalCase method, Given/When/Then
`DisplayName`, `TheoryData<T1, T2, ...>` for `[MemberData]`. Real example
(`tests/unit/Comuki.Engine.Orchestration.Unit.StatusMachine/WorkItemStatusMachineShould.cs`):

```csharp
public sealed class WorkItemStatusMachineShould
{
    public static TheoryData<WorkItemStatus, WorkItemStatus, bool> Matrix { get { /* ... */ } }

    [Theory(DisplayName = "Given work item statuses from/to, when CanTransition is called, then it matches the transition table")]
    [MemberData(nameof(Matrix))]
    public void MatchTransitionTable(WorkItemStatus from, WorkItemStatus to, bool expected)
    {
        var machine = new WorkItemStatusMachine();

        machine.CanTransition(from, to).ShouldBe(expected);
    }
}
```

**DisplayName policy in practice**: mandatory for integration and for
complex/adversarial unit tests; optional for simple ones **that carry an
XML `<summary>`** doc-comment instead (the audit found 216/1126 unit
`[Fact]`s without `DisplayName` — 19% — and judged most of them
acceptable under this carve-out; only tests whose method name alone
doesn't convey *what kind* of input/scenario failed — e.g. adversarial
parser cases — actually need it). Don't chase 100% DisplayName coverage as
a goal in itself; add it where the failure message would otherwise be
opaque.

## 3. Assertions & mocking — as actually used

**Shouldly**, not `Should()`/FluentAssertions (banned, same as canon).
**NSubstitute**, not Moq. Syntax reference: canon `testing-unit.md` §6–7 —
not restated here, it's identical in practice.

Comuki-specific mocking boundary (verified against `tests/unit/**`):

- Don't mock `DbContext` — use `tests/integration/**` against real
  Postgres (`testing-integration.md`). A handful of unit suites use
  `UseInMemoryDatabase` for CRUD-shaped coverage where Postgres-specific
  semantics (`FOR UPDATE SKIP LOCKED`, `ExecuteUpdate`/`ExecuteDelete`,
  generated-column defaults) don't apply — those files say so in an XML
  `<summary>` ("Integration contract — DB-bound paths covered by
  `Comuki.Modules.X.Integration...`"); copy that disclosure pattern if you
  add a new `UseInMemoryDatabase` suite, don't silently extend one into
  Postgres-specific territory.
- `ILogger<T>` → `NullLogger<T>.Instance`, not a substitute.
- `IOptions<T>` / `IConfiguration` → construct directly, not a mock.

## 4. Test data — builders, not Bogus (today)

No `Bogus` package reference exists in `Directory.Packages.props`. Current
practice is a hand-rolled builder (`WithX(...)` fluent setters + `Build()`)
when an object has 5+ fields and 3+ call sites, otherwise inline
construction in the test. If a suite genuinely needs mass fake-data
generation, add `Bogus` to `Directory.Packages.props` as part of that
change — don't assume it's already available.

## 5. Coverage floor — 70% line, not 80%+

Enforced through `Directory.Build.props`, activated by `IsTestProject=true`:

```xml
<CollectCoverage>true</CollectCoverage>
<CoverletOutputFormat>cobertura,opencover</CoverletOutputFormat>
<Threshold>70</Threshold>
<ThresholdType>line</ThresholdType>
<ThresholdStat>total</ThresholdStat>
```

70%, deliberately — not 80%+ (chases meaningless tests). Same floor on the
frontend (`bun run test` under `dashboard/`).

## 6. Anti-patterns — comuki's actual, audited state

Canon anti-patterns (mock-testing-mocks, one-test-many-cases via if/switch,
magic numbers, private-method testing via reflection, bare `Task.Delay`,
unexplained `[Fact(Skip = ...)]`) all apply unchanged — see canon
`testing-unit.md` §9. Below is what the **2026-09-09 audit**
(`.agents/docs/audits/testing-audit-report.md`) found in this repo's actual
`tests/unit/**`, so you know the current baseline, not just the target:

- **`_ =` discard is banned** (canon `~/.agents/rules/csharp/async-and-tasks.md`
  §6 — applies to test code too, not just `src/`) but was **violated 401
  times across 67 test files** as of the last audit, because no MSBuild
  `[SelfAuditReport]` target scans `tests/**` (only `src/**` is gated).
  Don't add new discards; a project-wide cleanup is tracked separately —
  this rule doesn't fix the backlog, it stops it growing.
- `DateTimeOffset.UtcNow` is fine as a wall-clock **anchor** in test setup
  (`var now = DateTimeOffset.UtcNow; Order.Create(..., now)`). It is
  **not** fine when the test asserts on time *progression* — lease expiry,
  sweeper windows, "age" calculations — those need an injected
  `TimeProvider` / a `FakeTimeProvider`/`FixedClock` test double. Several
  suites duplicate their own `FakeTimeProvider`/`FixedClock` today (audit
  §5.17); a shared one lands in `Comuki.Host.Testing` as part of this
  change's WS2 — until then, follow whatever the file you're editing
  already uses rather than inventing a fourth copy.
- `[Fact(Skip = "...")]` always needs a reason + an issue reference, e.g.
  `Skip = "blocked on harden-pi-worker-sandbox 6.1, gh-issue #125"` — never
  a bare `Skip = "Broken"`.
- Private helper methods in test classes are allowed (framework carve-out,
  canon `~/.agents/rules/csharp/class-layout-and-tooling.md` §1a) but a
  high count in one file (6+) is a signal to extract a `file static class`
  helper, not a hard rule.

## Related

- `testing-integration.md` (this directory) — integration tests.
- Canon: `~/.agents/rules/csharp/testing-unit.md` (syntax reference),
  `~/.agents/rules/csharp/testing-stack-and-pyramid.md` (stack + decision
  tree — numbers there are for a different app),
  `~/.agents/rules/csharp/async-and-tasks.md` §6 (`_ =` discard ban).
- `.agents/docs/audits/testing-audit-report.md` — source of the audited
  numbers in §2 and §6; re-check before citing it further, audits age.

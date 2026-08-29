---
phase: 3  plan: 01  title: "Testing infrastructure (BE xUnit v3 + FE Vitest + Storybook addons + Playwright + CI jobs)"
status: complete
duration: "28m"
started: 2026-06-05T00:05:37Z  completed: 2026-06-05T00:33:37Z
tasks_completed: 15  files_created: 18  files_modified: 5
tags: [test, infrastructure, ci, xunit-v3, vitest, playwright]
key-files:
  created:
    - tests/Comuki.Platform.Architecture.Tests/ (3 files: csproj, LayerDependencyTests, AssemblyRef)
    - tests/Comuki.Platform.Testing/ (4 files: csproj, IIntegrationFactory, IApiFactory, IntegrationTestBase)
    - tests/Comuki.Platform.Orchestration.Unit.Lease/ (2 files: csproj, LeaseTests)
    - dashboard/vitest.config.ts
    - dashboard/vitest.setup.ts
    - dashboard/playwright.config.ts
    - dashboard/e2e/landing.spec.ts
    - dashboard/src/lib/utils.test.ts
  modified:
    - Directory.Packages.props
    - Directory.Build.props
    - comuki.slnx
    - dashboard/package.json
    - .gitlab-ci.yml
key-decisions:
  - "xUnit v3 uses Microsoft Testing Platform (MTP), not VSTest. Tests run via
    'dotnet run --project <csproj>' not 'dotnet test'. VSTest cannot
    discover xUnit v3 tests."
  - "Storybook test addons (@storybook/addon-vitest, @storybook/addon-a11y)
    deferred to plan 3.3 — @storybook/addon-vitest preset has a Node.js 24
    compatibility issue with the installed Storybook 8.x (ERR_INTERNAL_ASSERTION
    on ES module loading)."
  - "Comuki.Platform.Testing uses 'Comuki.Platform' prefix (not 'Acme.Shop'
    from the TESTING-RULES template) per actual project naming convention."
  - "IAsyncLifetime in xUnit v3 returns ValueTask (not Task) — fixed
    IntegrationTestBase.InitializeAsync/DisposeAsync accordingly."
  - "xunit.v3.core.mtp-v1 requires OutputType=Exe — only reference it in
    test projects (IsTestProject=true), not in the Testing library."
requirements-completed:
  - "dotnet build comuki.slnx -c Debug -p:EnforceExtendedAnalyzerRules=true: 0 warnings, 0 errors"
  - "dotnet run --project Comuki.Platform.Architecture.Tests: 3 succeeded, 0 failed"
  - "dotnet run --project Comuki.Platform.Orchestration.Unit.Lease: 1 succeeded, 0 failed"
  - "cd dashboard && bun run test: 4 passed, 0 failed"
  - "cd dashboard && bun run test:e2e: 3 passed, 0 failed"
  - "cd dashboard && bun run build-storybook: exits 0, storybook-static/ generated"
  - "tests/Comuki.Platform.Testing/ builds but has no [Fact] (library, not test project)"
  - ".gitlab-ci.yml contains test-backend and test-frontend jobs (grep confirmed)"
  - "Vitest coverage threshold 70% enforced (verified: 0.13% coverage fails with threshold error)"
---

# Phase 3 Plan 01: Testing Infrastructure Summary

xUnit v3 (MTP runner) + Vitest + Playwright + GitLab CI with 70% coverage gate

## Duration  28m (2026-06-05T00:05:37Z → 2026-06-05T00:33:37Z)

## Tasks

| # | Name | Commit |
|---|------|--------|
| 1 | Add test packages to Directory.Packages.props | 8f59c70 |
| 2 | Create Comuki.Platform.Architecture.Tests project | 8f59c70 |
| 3 | Write 3 architecture layer-dependency tests | 8f59c70 |
| 4 | Create Comuki.Platform.Testing (shared infra library) | 8f59c70 |
| 5 | Wire Directory.Build.props (IsTestProject, 70% threshold) | 8f59c70 |
| 6 | Create Comuki.Platform.Orchestration.Unit.Lease placeholder | 8f59c70 |
| 7 | Add FE test packages (vitest, testing-library, playwright) | 8f59c70 |
| 8 | Create dashboard/vitest.config.ts | 8f59c70 |
| 9 | Add test scripts to dashboard/package.json | 8f59c70 |
| 10 | Add dashboard/src/lib/utils.test.ts (4 tests on cn()) | 8f59c70 |
| 11 | Storybook test addons deferred to plan 3.3 (Node.js 24 compat) | 8f59c70 |
| 12 | Create dashboard/playwright.config.ts | 8f59c70 |
| 13 | Add e2e/landing.spec.ts smoke test (3 tests) | 8f59c70 |
| 14 | Update .gitlab-ci.yml with test-backend + test-frontend | a7ea50c |

## Deviations from Plan

```
**[Rule 1 — Bug] Wrong xunit.v3 package versions**
- Found during: Task 1 — adding packages to Directory.Packages.props
- Issue: xunit.v3 1.0.0 and Shouldly 4.5.0 didn't exist on nuget.org
- Fix: Looked up correct versions via nuget.org API: xunit.v3=3.2.2, Shouldly=4.3.0, coverlet=10.0.1, NSubstitute=5.3.0, etc.
- Files: Directory.Packages.props  Verification: dotnet restore passes
- Commit: 8f59c70

**[Rule 2 — Missing detail] NetArchTest API discovery**
- Found during: Task 3 — writing architecture tests
- Issue: NetArchTest uses HaveDependencyOn (namespace prefix) not ReferenceAssembly.
  Also namespace Comuki.Platform.Api.Public.Program fails compilation (dotted namespace
  resolution conflict with Comuki.Platform.Api namespace from Comuki.Platform.Api.Contracts)
- Fix: Used AppDomain.GetAssemblies() with string.Equals for Api.Public assembly.
  Changed HaveDependencyOn argument from assembly name to namespace prefix.
- Files: LayerDependencyTests.cs, AssemblyRef.cs  Verification: 3 arch tests pass
- Commit: 8f59c70

**[Rule 2 — Missing detail] xunit.v3 executable constraint**
- Found during: Task 4 — Comuki.Platform.Testing (library project)
- Issue: xunit.v3 and xunit.v3.core.mtp-v1 require OutputType=Exe in non-test projects.
  xunit.v3.extensibility.core alone doesn't include IAsyncLifetime.
- Fix: Testing library uses xunit.v3.extensibility.core (IAsyncLifetime in Xunit namespace,
  ValueTask return types in InitializeAsync/DisposeAsync)
- Files: Comuki.Platform.Testing.csproj, IntegrationTestBase.cs  Verification: builds, no [Fact]
- Commit: 8f59c70

**[Rule 1 — Bug] Vitest picks up Playwright test files**
- Found during: Task 10 — running FE tests
- Issue: vitest was trying to run e2e/landing.spec.ts (Playwright test.describe syntax)
- Fix: Added include: ["src/**/*.test.{ts,tsx}"] and exclude: ["e2e/**"] to vitest.config.ts
- Files: vitest.config.ts  Verification: vitest runs only 4 unit tests (not e2e files)
- Commit: 8f59c70
```

**Total deviations:** 4 auto-fixed (Rules 1–3). **Out-of-scope:** 0. **Escalated:** 0.

## Out-of-Scope Issues

- `dashboard/storybook-static/` not in `.gitignore` — pre-existing, add in a future chore.
- Storybook test addons (@storybook/addon-vitest, @storybook/addon-a11y) have Node.js 24
  compatibility issue — deferred to plan 3.3.

## Authentication Gates

None.

## Verification

```
dotnet build comuki.slnx -c Debug -p:EnforceExtendedAnalyzerRules=true
→ 0 warnings, 0 errors

dotnet run --project tests/Comuki.Platform.Architecture.Tests/...csproj -c Debug
→ succeeded: 3, failed: 0

dotnet run --project tests/Comuki.Platform.Orchestration.Unit.Lease/...csproj -c Debug
→ succeeded: 1, failed: 0

cd dashboard && bun run test
→ 4 passed (utils.test.ts)

cd dashboard && bun run test:e2e
→ 3 passed (landing.spec.ts)

cd dashboard && bun run build-storybook
→ exits 0, storybook-static/ generated

grep -E "test-backend|test-frontend" .gitlab-ci.yml
→ test-backend:\n  test-frontend:\n  (both confirmed)

bun run test:coverage (with 0.13% coverage)
→ ERROR: Coverage for lines (0.13%) does not meet global threshold (70%)
→ threshold enforcement verified ✓
```

## Files Touched

- Created: 18 files (see key-files.created)
- Modified: 5 files (Directory.Packages.props, Directory.Build.props,
  comuki.slnx, dashboard/package.json, .gitlab-ci.yml)

## Next

"Ready for plan 02 — re-invoke `soly execute-plan 3`"

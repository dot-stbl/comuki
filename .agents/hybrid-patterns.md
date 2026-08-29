# Comuki build-quality patterns — audit for the anlytra comparison

> **Headline finding:** the user's premise holds for **1 of 3** patterns. Comuki does **not** have a separate build project, and does **not** have a test-specific `.editorconfig`. It does have strong analyzer+editorconfig rules — but so does anlytra, and anlytra is in some respects already further along. Both repos are structured the same way (`Directory.Build.props` + `Directory.Packages.props` + one root `.editorconfig`), so most of the "diff vs anlytra" is small.

Evidence base (comuki, this repo):
- `Directory.Build.props` (30 lines), `Directory.Packages.props` (49 lines), `.editorconfig` (376 lines / 18.7 KB), `comuki.slnx` (27 lines, 6 src + 6 test projects).
- `find` across the whole repo (excluding `bin/`/`obj/`/`node_modules/`/`.git/`) for `*.targets`, `*.ruleset`, `stylecop.json`, `*build*.csproj`, `tools/`, nested `.editorconfig`, and `git log --all` for the same — all empty.

---

## Pattern 1 — Separate "build" project

### What it is
**Not present.** There is no `Build.csproj`, no `*.targets` file, no `tools/` directory, and no build-coordination csproj anywhere in comuki — not in the working tree and not in any branch / git history (`git log --all -- '*.targets' '**/Build.csproj'` returns nothing).

The closest analogues that *do* exist:
- `Directory.Build.props` (`C:\Users\bradw\source\hybrid\comuki.orchestrator\Directory.Build.props`) — the single shared MSBuild file. 30 lines: `net10.0`, `Nullable`, `ImplicitUsings`, `LangVersion=latest`, `TreatWarningsAsErrors=true`, `EnforceCodeStyleInBuild=true`, `AnalysisLevel=latest`, `NoWarn CS1591`, and a conditional Coverlet block gated on `$(IsTestProject) == 'true'` (lines 22–28).
- `Directory.Packages.props` — Central Package Management (CPM): `<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>`, every version pinned here.
- `.gitlab-ci.yml` — CI orchestration (`build-backend`, `build-frontend`, `test-backend`, `test-frontend` stages). This is the closest thing to "coordinating builds," but it's a CI YAML, not a .csproj.
- `scripts/hooks/pre-commit` + `scripts/install-hooks.sh` — whitespace-only pre-commit hook (`dotnet format whitespace`).

The analyzer wiring that a "build project" would typically centralize is instead done **inline in `Directory.Build.props`** (lines 17–21), versionless, so it composes cleanly with CPM:
```xml
<ItemGroup>
  <PackageReference Include="Meziantou.Analyzer" PrivateAssets="all" />
  <PackageReference Include="Roslynator.Analyzers" PrivateAssets="all" />
  <PackageReference Include="Roslynator.Formatting.Analyzers" PrivateAssets="all" />
</ItemGroup>
```

### Why it exists (in this form)
Comuki avoids needing a build/analyzer project by using **CPM correctly**. Because analyzer `PackageReference`s carry no `Version`, they don't conflict with `ManagePackageVersionsCentrally=true`. One `Directory.Build.props` inherits to all 12 csproj, so there's nothing left for a dedicated build project to coordinate.

### Diff vs anlytra
**Anlytra is in the same situation but worse.** Anlytra has no build project either (`find` for `*build*.csproj` / `tools/` / `*.targets` is empty). The difference is that anlytra's `Directory.Build.props` (`C:\Users\bradw\.source\.stbl\anlytra\Directory.Build.props`) puts analyzer `PackageReference`s **with explicit `Version="..."`** directly inside it, which **breaks CPM**. As a result anlytra's `Directory.Packages.props` (lines 25–39) has **CPM disabled**:
```
ManagePackageVersionsCentrally = false   (anlytra, line 38)
```
and its own migration plan (lines 23–24 of `Directory.Packages.props`) literally proposes a shared `Analyzers.props` *"or a virtual Anlytra.Analyzers.csproj referenced by everyone"* as the fix.

So: **comuki is the reference for the pattern, but the pattern is "drop the analyzer Versions from `Directory.Build.props` and turn CPM on" — not "create a build project."** Comuki already does this; anlytra's own plan is to catch up to comuki.

### Cost to apply to anlytra
Small, and it's already anlytra's own documented plan:
1. Strip `Version="..."` from the 5 analyzer `PackageReference`s in anlytra's `Directory.Build.props`.
2. Pin the two wildcard versions (`Meziantou.Analyzer 3.0.*`, `Microsoft.VisualStudio.Threading.Analyzers 4.0.*`) to exact versions.
3. Flip `ManagePackageVersionsCentrally` to `true` and add `<PackageVersion>` entries for the analyzers.
4. Strip `Version=` from every `.csproj` `PackageReference`.

Effort: ~1 hour of mechanical edits + a clean `dotnet build`. **No new project needed.**

---

## Pattern 2 — Improved code-style rules (beyond a baseline .NET project)

### What it is
**Present and this is the one pattern that genuinely matches the user's description.**

Files:
- `C:\Users\bradw\source\hybrid\comuki.orchestrator\.editorconfig` — 376 lines, 45 `dotnet_diagnostic.*` severity overrides, full naming-rule block, plus a ~100-line ReSharper/Rider properties section.
- `Directory.Build.props` lines 9–11: `TreatWarningsAsErrors=true`, `EnforceCodeStyleInBuild=true`, `AnalysisLevel=latest`.
- `Directory.Build.props` lines 17–21 + `Directory.Packages.props` lines 31–37: analyzers wired globally with `PrivateAssets="all"` — `Meziantou.Analyzer 3.0.98`, `Roslynator.Analyzers 4.15.0`, `Roslynator.Formatting.Analyzers 4.15.0`. `Microsoft.CodeAnalysis.NetAnalyzers` (CA) and `Microsoft.VisualStudio.Threading.Analyzers` (VSTHRD) come from the SDK via `AnalysisLevel=latest` (no explicit reference needed).
- Agent-facing convention docs in `.agents/rules/coding/` (`CODING-RULES.md`, `FRAMEWORK-RULES.md`, `ANALYZERS.md`, etc.) describe the rules the editorconfig enforces. These are documentation, not build inputs.

What's tightened vs a stock `dotnet new` baseline (concrete, from the `.editorconfig`):
- **Naming enforced as `error`** (lines ~135–195): camelCase private/protected/internal fields with **no underscore prefix**, mandatory `I` interface prefix, mandatory `Async` suffix on async methods, PascalCase public members + constants.
- **`var` enforced** (`IDE0007 = error`, lines ~73–76); explicit-type preference (`IDE0008`) disabled.
- **File-scoped namespaces mandatory** (`IDE0161 = error`).
- **Braces mandatory everywhere** (`csharp_prefer_braces = true:error`, `IDE0011 = error`).
- **Modifier order enforced** (`IDE0036 = error`); **accessibility modifiers always required** (`IDE0040 = error`).
- **Async hygiene as `error`**: `VSTHRD200` (Async suffix), `VSTHRD002/103/104` (no `.Result`/`.Wait()`/`GetAwaiter().GetResult()`), `MA0042` (no blocking calls in async).
- **Structured logging**: `CA2254 = error` (no interpolated log templates), `CA1727 = warning` (PascalCase placeholders).
- **Roslynator minimalism** (`RCS1124/1173/1206/1208/1218/1238 = warning`): inline locals, ternary over if-else, reduce nesting.
- **Meziantou** (`MA0006/0011/0016/0040/0045/0080/0099`): `StringComparison`, `IFormatProvider`, forward `CancellationToken`, etc.
- A set of NetAnalyzers explicitly **relaxed** with documented reasons: `CA1031/1062/1303/1707/1716/1720/1812/2007 = none`, several to `warning`.

### Why it exists
The companion rule `ANALYZERS.md` states the philosophy: warnings are treated as code smells, `TreatWarningsAsErrors=true` globally, severity per-rule lives in `.editorconfig`, and the escape hatches (`#pragma`, baseline issue) are codified rather than ad-hoc. The intent is that LLM-written code conforms to project standards at compile time, not via review.

### Diff vs anlytra
**Anlytra is already on the same diet and is in places stricter.** Anlytra's `.editorconfig` is 485 lines with **82** `dotnet_diagnostic` overrides (vs comuki's 45). Anlytra's `Directory.Build.props` adds **two things comuki lacks**:
- `AnalysisMode=Recommended` and `EnableNETAnalyzers=true` explicitly.
- An extra analyzer package: `Roslynator.CodeAnalysis.Analyzers` (comuki does not reference it).
- `GenerateDocumentationFile=true` (comuki instead `NoWarn`s `CS1591` and defers docs).

The one real gap: anlytra's rules are **not CPM-clean** (see Pattern 1), so the wiring is uglier (explicit versions + `IncludeAssets` boilerplate repeated per analyzer, lines 47–73 of anlytra's `Directory.Build.props`). The rule *content* is comparable or stricter; the *packaging* is messier.

### Cost to apply to anlytra
**Near-zero for the rules themselves** — anlytra already has them. The only thing worth porting from comuki is the cleaner analyzer-packaging convention (versionless refs + CPM). If anything, the direction of travel could be comuki → anlytra only for `Roslynator.Formatting.Analyzers` if anlytra doesn't already enforce it (it does — it's in anlytra's `Directory.Build.props`). **Net: nothing new to adopt here.**

---

## Pattern 3 — Test-specific `.editorconfig`

### What it is
**Not present.** This is the pattern the user was most explicit about ("editorconfig для тестов специальный"), and it does **not** exist in comuki.

Evidence:
- `find . -name '.editorconfig'` (and case-insensitive `-iname`) returns exactly one file: the repo-root `.editorconfig`. There is no `tests/.editorconfig`, no per-test-project `.editorconfig`.
- `git log --all -- '.editorconfig' 'tests/.editorconfig' '**/.editorconfig'` shows the root file was added once in the initial commit `229d1dc` and never had a tests-scoped sibling.
- The root `.editorconfig` contains **no path-specific test glob** — there is no `[**/tests/**.cs]`, `[**/*Test*.cs]`, or `[**/*Tests*.cs]` section. Every `dotnet_diagnostic` override applies globally to all `*.cs`.
- The only test-aware thing in the whole build is the Coverlet coverage block in `Directory.Build.props` lines 22–28, gated on `$(IsTestProject) == 'true'` — that's MSBuild (coverage thresholds), not editorconfig rules.
- The one editorconfig line that *mentions* tests is global, not test-scoped: `.editorconfig:276` → `dotnet_diagnostic.CA1707.severity = none  # identifier should not contain underscore (test methods)`. It disables the no-underscores rule repo-wide with a comment that the reason is test-method names — i.e. comuki chose the coarse "disable globally" lever, not a test-scoped override.

### Why it exists (or doesn't)
Comuki opted for a single global ruleset. The companion `TESTING-RULES.md` describes test *project structure* and naming conventions but does not call for a separate editorconfig; the CA1707 case shows the project's preference is to relax a rule globally with a comment rather than carve out a tests scope.

### Diff vs anlytra
**Identical absence.** `find` on anlytra (`C:\Users\bradw\.source\.stbl\anlytra`) returns a single root `.editorconfig` and nothing under `tests/`. Neither project has a test-specific editorconfig. If the user wants this pattern, **neither repo is a source for it** — it would have to be created from scratch.

### Cost to apply to anlytra (if desired)
Trivial to create, the question is whether it's worth it:
- Add `tests/.editorconfig` (or per-project) overriding what test code legitimately needs, e.g.:
  - `CA1707 = none` already global in both — no test override needed.
  - `CA1051` / `CA1812` (DI-instantiated, never-instantiated classes) — often noisy in test fixtures; comuki sets `CA1812 = none` globally already.
  - `CA1852` (sealed) — test classes are rarely sealed; could relax to `none` for tests only.
  - `IDE0058`/`IDE0059` (unused expression value) — common with `Should.Throw(...)`; a tests-only relaxation is the canonical reason these files exist.
- Effort: ~20 lines, ~15 minutes. But the value is low unless test code is actively fighting global rules.

---

## Summary — what's worth adopting

| Pattern | Exists in comuki? | anlytra status | Worth adopting in anlytra? |
|---|---|---|---|
| 1. Separate build project | ❌ No | Same (no build project) | **No.** The real fix for anlytra is comuki's CPM-clean analyzer wiring — not a build project. That's anlytra's own deferred plan. |
| 2. Improved rules | ✅ Yes | Already comparable/stricter (82 vs 45 overrides) | **No new work.** anlytra is already there; only the *packaging* is uglier. |
| 3. Test-specific editorconfig | ❌ No | Same (none) | **Overkill for now.** Only worth ~15 min if test code starts fighting `IDE0058/IDE0059` or `CA1852`. |

**Bottom line:** of the three patterns the user named, only "improved rules" actually exists in comuki — and anlytra already has an equivalent (in places stronger) setup. The genuinely valuable thing comuki demonstrates that anlytra lacks is **Pattern 1's underlying technique done without a build project**: versionless analyzer `PackageReference`s in `Directory.Build.props` so that Central Package Management can stay enabled. That single change resolves anlytra's documented CPM-is-disabled problem and removes the temptation to invent a build/analyzer project at all.

The "separate build project" and "test editorconfig" ideas are **overkill** for a 30-csproj solution: `Directory.Build.props` + CPM already covers the build-coordination job, and a test editorconfig only earns its keep once specific test-only relaxations are actually needed.

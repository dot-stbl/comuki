## Context

See proposal.md for why. Current state and constraints that shape the how:

- **Hand-mapper inventory (verified 2026-09-25, whole `platform/src`):**

  | Module | Hand-mapping code | Projections | Canon verdict |
  |---|---|---|---|
  | Projects | `Views/ProjectMapper.cs` (static class) | 2 — `Project→ProjectView` (13 fields), `ProjectSettings→ProjectSettingsView` (14 fields) | Mapperly material — this change |
  | Identity | `Views/AccountMapper.cs` + `OidcLinkView.Of` + `ApiKeyView` factory | 4 | Mapperly material, wave 1 (needs user-mapping methods: `RoleKeys.Key`, `ScopeLevelKeys.Key`, id→string) |
  | Scheduler | `ScheduledJobView.Of/OfAll` + page composition in `ScheduledJobService` | 1 + page | Mapperly material, wave — **blocked on `add-scheduled-jobs` (in progress on the same module)** |
  | Intake | `IntakeTicketView.Of`, `SourceConnectionView.Of`, `AdmissionRuleView.Of` | 3 | Mapperly material, wave 2 (keys, enum→string); the four `*PayloadMapper`s in Infrastructure/Providers are wire parsing and stay |
  | Memory | `MemoryFactViewMapper` (file-static in `EfMemoryStore`), `LearningCandidateView.Of` | 2 | Wave 2 (one construction site is raw-SQL row shaping in `MemoryFactSql` — stays hand) |
  | Costs | `UsageEventMapper` (contract `UsageEventSummary`→view) + aggregation views in two handlers | 1 + 2 | **Not Mapperly material** — contract read model, not a domain entity (spec: "Contract-to-view projection is not force-migrated") |
  | Host | ~10 inline view constructions (Runs, Workers, Knowledge, Compute, Settings, Proxy, Me, Realtime) | many | **Not in scope** — composition-root shaping over query results/options, not the module entity→view pattern |

- `ProjectView` is a positional record (`record ProjectView(ProjectId Id, …)`)
  carrying `ProjectId` (typed id), nullable strings, `string[] Tags` — all
  same-type pass-throughs: the Projects mapper needs **zero** user-mapping
  methods, which is exactly what makes it the right reference implementation.
- `ProjectMapper` has 7 handler call-sites (Create/Update/Archive/Get/List ×
  projects, Get/Update × settings) and a fidelity test
  (`tests/unit/Comuki.Modules.Projects.Unit/ProjectMapperShould.cs`, 5 facts).
- **CPM is ON in this repo** (`Directory.Packages.props` at root,
  `ManagePackageVersionsCentrally=true`; csproj `PackageReference`s carry no
  versions). The session brief assumed "CPM off, version in csproj" — the
  repo state wins: the pin lands in `Directory.Packages.props`, matching the
  canon's own stack table. Recorded here as a deliberate correction, not a
  contradiction needing escalation.
- The OpenAPI document reads XML `///` docs through generated `.xml` files;
  `dashboard/package.json` has `generate-api` (dotnet build + kubb +
  prettier). `ProjectView` currently carries only a class-level summary (no
  per-field `<param>` docs) → converting it to properties cannot shift
  OpenAPI descriptions. Later waves with per-field `<param>` docs
  (e.g. `ScheduledJobView`) must carry the doc text onto the properties.
- `add-project-visuals` is complete (26/26) but uncommitted — its
  Icon/Color/Tags `ProjectView` is the starting shape this change builds on.
- `domain-error-contract` (in progress) re-parents module exceptions and
  rewrites Host error paths — different files in the same csproj; no overlap
  with views/mappers/handlers' mapping lines. Either landing order works.

## Goals / Non-Goals

**Goals:**

- Land the canon's full stack in one module end-to-end: pinned package,
  singular interface, `[Mapper]` implementation with
  `RequiredMappingStrategy.Target`, singleton DI, init-property views,
  generated-mapper tests — so every later wave is a copy of a proven shape.
- Prove wire invariance mechanically (serialization pin + zero-diff kubb
  regen), not by assertion.
- Freeze the convention against regression via architecture tests.

**Non-Goals:** (design-level)

- Migrating any module other than Projects (see the wave table above; the
  brief's "waves vs Projects-only" question is settled by sizing —
  D9).
- Touching the hand-written boundary (payload mappers, cache-entry
  round-trips, host request→command shaping) — D4.
- Migrating Host inline projections or Costs contract→view — D4 records the
  boundary; revisit only on owner request.
- Any FE code change; the kubb regen is a verification gate, not an edit.

## Decisions

### D1. Package wiring — CPM pin + analyzer-style reference

`Directory.Packages.props` gains (with the repo's customary one-line
rationale comment):

```xml
<PackageVersion Include="Riok.Mapperly" Version="4.3.1" />
```

4.3.1 is the line present in this environment's NuGet cache (the same
"latest line available locally" convention the YamlDotNet pin recorded).
`Comuki.Modules.Projects.Application.csproj` references it the way source
generators are wired (`source-generators.md` §2 — analyzer assets, no
runtime flow, no transitive leak):

```xml
<PackageReference Include="Riok.Mapperly">
  <PrivateAssets>all</PrivateAssets>
  <ExcludeAssets>runtime</ExcludeAssets>
</PackageReference>
```

**Alternative:** version-in-csproj (per the session brief) — rejected: CPM
is enabled repo-wide and every other pin lives centrally. **Alternative:**
shared `Directory.Build.props` reference for all projects — rejected: only
modules with mappers pay for the generator; per-csproj keeps the build graph
honest. Later waves copy the same three lines.

### D2. Interface naming and placement — singular, Views-adjacent

`IProjectsMapper` + `partial sealed class ProjectsMapper : IProjectsMapper`
in `Comuki.Modules.Projects.Application/Views/` (same folder as the views —
the module has no `Mappers/` folder and one mapper does not justify one).
Methods named by destination: `ToView(Project source)` → `ProjectView`,
`ToView(ProjectSettings source)` → `ProjectSettingsView`. The overload pair
reads better here than `ToProjectView`/`ToSettingsView` because both targets
are "the view" of their entity and the parameter disambiguates. Placement
follows the existing module shape; the canon's `Mappers/` suggestion yields
to repo layout.

### D3. DTO migration — both Projects views now, positional→init-property

`ProjectView` and `ProjectSettingsView` become brace-body records with
`required` init-properties (`public required ProjectId Id { get; init; }`,
collections default `= [];`). `required` (not just `init`) because the strict
mapper sets every property and nothing else may construct a view half-filled.
Wire equivalence: System.Text.Json serializes the same public properties
with the same names/casing/nullability in both record forms — positional
records already compile to init-only properties; the constructor merely
stops being part of the public surface. No `[JsonPropertyName]` exists on
either type to drift. In-scope types are exactly these two; wave tables
(listed in Context) enumerate the rest per module.

### D4. The hand-written boundary (canon `mapper.md` ↔ `mapping.md`)

Stays hand-written, by rule:

1. **Wire/format parsing** — Intake `GitHubPayloadMapper`, `GitLabPayloadMapper`,
   `JiraPayloadMapper`, `YandexTrackerPayloadMapper` (external JSON → domain);
   `ProjectSettingsCacheEntryMapper` (settings ↔ cache DTO + JSON
   serialization round-trip); `MemoryFactSql` row shaping (raw SQL → view).
2. **Request-DTO→command shaping in the host** — `ProjectsEndpointMapper`,
   `UploadArtifactResultMapper` (DTO→DTO direction; canon: mappers map
   entity→view only).
3. **Composition/aggregation shaping** — Costs handlers' slice views,
   Host inline projections (Runs/Workers/Settings/Compute/Knowledge/
   Realtime/Me): sources are query results and options bundles, not domain
   entities.
4. **Contract read-model→view** — `UsageEventMapper` (Costs): source is
   `Comuki.Shared.Contracts.Usage.UsageEventSummary`, not a Costs domain
   entity.

This boundary is encoded in the spec requirement "Hand-written mapping stays
at the format boundary" so later waves do not relitigate it.

### D5. DI — singleton in the module's application installer

`ProjectsApplicationExtensions.AddProjectsApplication` gains
`services.AddSingleton<IProjectsMapper, ProjectsMapper>();` — generated
bodies are stateless (canon). Handlers take `IProjectsMapper` via primary
constructor. The singleton sits beside the existing `TimeProvider`/
resolver registrations; no scoped dependency exists to capture.

### D6. Tests — construct the real mapper; add a wire pin

- `ProjectMapperShould` becomes `ProjectsMapperShould` (or keeps its name;
  file rename follows the type): five fidelity facts now call
  `new ProjectsMapper().ToView(...)` through the interface type — same
  assertions, zero mocks.
- New serialization pin: serialize a fully-populated `ProjectView` (and
  settings view) with `JsonSerializerOptions.Web` and assert the exact
  property-name set and order against a pinned literal list — the
  no-wire-change invariant made executable. Runs in the same unit project;
  no HTTP required.
- kubb gate: `cd dashboard && bun run generate-api` → `git diff --exit-code
  dashboard/src/shared/api/_generated` (zero diff) — proof the OpenAPI and
  client are untouched. If descriptions ever drift (they cannot for these
  two types — no per-field docs today), the drift is fixed at the source,
  not absorbed into the client.

### D7. Enforcement — architecture tests, module-list driven

New `MappingConventionTests` in `tests/Comuki.Architecture.Tests`: a
hard-coded adopted-modules list (starts with
`Comuki.Modules.Projects.Application`) drives two assertions:
(a) every public type in the module's `Views` namespace has no public
constructor with parameters (positional-record ban — reflection check);
(b) no static class named `*Mapper` exists in that namespace (the deleted
`ProjectMapper` shape cannot return). Payload mappers live under
`Infrastructure/Providers` — outside the asserted namespace, no false
positives. Each wave appends its module to the list. **Alternative:** a
Roslyn analyzer — rejected for now: an arch test is the repo's established
enforcement vehicle and costs one file.

### D8. User-mapping methods inside mapper classes are the generator's
extension point

Later waves (Identity, Intake) need conversions (`RoleKeys.Key(role)`,
enum→string, typed-id→`Guid`). Mapperly's sanctioned mechanism is
user-mapping methods declared inside the partial mapper class, which may be
`private`. This is a generator-discovery contract analogous to the EF
parameterless-ctor exception in the unified private-method list — recorded
here so wave implementers do not fight §1a on it. Projects (wave 0) needs
none.

### D9. Scope — Projects-only reference + planned waves (sizing-driven)

The brief offered "reference + waves" vs "Projects-only with follow-ups".
Chosen: **Projects-only implementation in this change, waves as named
follow-up changes**, because: (1) the reference must prove the pattern on
the simplest shape (zero conversions) before Identity/Intake complicate it;
(2) `add-scheduled-jobs` is actively editing the Scheduler module — a
Scheduler wave now would collide; (3) `domain-error-contract` is editing
module exception surfaces — one shared csproj is enough overlap for one
change; (4) per-wave changes keep each merge wire-pinned and reviewable.
Wave sizing: Identity ≈ 4 projections / 1 static class + 2 factories;
Intake ≈ 3 views; Memory ≈ 2; Scheduler ≈ 1 + page (after its in-flight
change lands).

### D10. Interaction with in-flight changes

- `add-project-visuals` (complete, uncommitted): baseline — this change
  starts from its 13-field `ProjectView`; no rebase gymnastics expected.
- `domain-error-contract`: disjoint files (exceptions/host error paths vs
  views/mappers/handlers); either order; if it lands first, trivial rebase.
- `add-scheduled-jobs`: no file overlap with Projects; the reason the
  Scheduler wave is deferred, not a blocker for this change.
- `dashboard-i18n`: FE-only; the kubb zero-diff gate composes with it.

## Risks / Trade-offs

- **[Risk] Mapperly diagnostics under `TreatWarningsAsErrors`** — RMG
  warnings become build errors, which is the desired strictness but can
  surprise a wave implementer → the exact-build-error behaviour is pinned as
  a spec scenario; D8 documents the escape valve (user-mapping methods),
  never `RequiredMappingStrategy.None`.
- **[Risk] `required` + future EF LINQ projections** —
  `Select(x => new ProjectView { … })` still compiles (object-initializer
  satisfies `required`); no store code projects these views today →
  noted, not mitigated further.
- **[Risk] Version 4.3.1 pinned from the local cache may lag upstream** →
  same posture as every other pin in `Directory.Packages.props`; bump is a
  one-line change.
- **[Trade-off] One more package per adopted module** vs a central
  `Directory.Build.props` reference → chosen for graph honesty (D1).
- **[Trade-off] Waves stay hand-written until their change lands** — the
  canon calls hand field-shuffling a finding, so the window is deliberate
  but finite: waves are named and sized (Context table), not open-ended.

## Migration Plan

Single-commit-slice backend change, no data or deploy impact: pin package →
convert two view records → add mapper + interface + DI → rewrite 7 handler
epilogues → delete `ProjectMapper` → migrate tests → add wire pin → add arch
tests → gates (`dotnet format comuki.slnx --severity hidden`;
`dotnet build comuki.slnx -c Debug`;
`dotnet run --project tests/unit/Comuki.Modules.Projects.Unit`;
`dotnet run --project tests/Comuki.Architecture.Tests --no-build`;
kubb zero-diff). Rollback = revert the commits; no persisted state, no wire
delta to unwind.

## Open Questions

None blocking. Owner may later ask for Host-inline-projection or Costs
migration — D4 records both as explicit boundary decisions, revisitable
without touching this change's artifacts.

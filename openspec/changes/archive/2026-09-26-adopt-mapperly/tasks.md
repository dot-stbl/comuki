# Adopt Mapperly — Tasks

Order: toolchain → views → mapper → handlers/DI → tests → wire pin →
enforcement → gates. Design references (D1–D10) point at `design.md`.

## 1. Toolchain (design D1)

- [x] 1.1 Add the pin to `Directory.Packages.props`: `<PackageVersion Include="Riok.Mapperly" Version="4.3.1" />` with the customary one-line rationale comment (4.3.1 = line available in this environment's package cache, per the YamlDotNet precedent). Reference it from `platform/src/modules/Projects/Comuki.Modules.Projects.Application/Comuki.Modules.Projects.Application.csproj` with `<PrivateAssets>all</PrivateAssets>` + `<ExcludeAssets>runtime</ExcludeAssets>`. Verify: `dotnet build comuki.slnx -c Debug` → 0 warnings / 0 errors (restore pulls the generator; no mapper code exists yet)
- [x] 1.2 Confirm the generator actually participates: add a throwaway `[Mapper] partial sealed class` in the Application project mapping `Project → ProjectView` (still positional at this point), build, observe the generated `.g.cs` under `obj/` and RMG020/diagnostic behaviour, then delete the throwaway. Verify: generated file seen in `obj/generated/` (or equivalent) during the probe build; nothing committed except the csproj/props edits

## 2. View records (design D3)

- [x] 2.1 Convert `ProjectView` (`…Application/Views/ProjectView.cs`) from positional record to brace-body record with `required` init-properties; keep the class-level XML summary and property order identical (Id, Name, Slug, Description, ProfilesGitUrl, ProfilesGitRef, Icon, Color, Tags, Archived, ArchivedAt, CreatedAt, UpdatedAt; `Tags` defaults `= [];`). No `[JsonPropertyName]`, no name changes. Verify: file compiles after task 3.1 supplies the mapper (build gate in section 3)
- [x] 2.2 Convert `ProjectSettingsView` the same way (14 properties, `Version` included). Verify: same as 2.1
- [x] 2.3 Grep for direct constructions to fix: `rg -n "new (ProjectView|ProjectSettingsView)\(" platform/src tests` — every hit is either the old `ProjectMapper` (deleted in 3.3) or a test updated in section 5. Verify: after sections 3 and 5, the same grep returns only mapper-internal hits (zero, once positional ctor is gone) and no compile errors anywhere

## 3. Mapper + DI + handlers (design D2, D5)

- [x] 3.1 Create `platform/src/modules/Projects/Comuki.Modules.Projects.Application/Views/IProjectsMapper.cs`: `public interface IProjectsMapper` with `ProjectView ToView(Project source);` and `ProjectSettingsView ToView(ProjectSettings source);` — XML docs on both (repo generates XML docs for OpenAPI)
- [x] 3.2 Create `Views/ProjectsMapper.cs`: `[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)] public sealed partial class ProjectsMapper : IProjectsMapper` with the two partial methods. Verify: `dotnet build comuki.slnx -c Debug` — strict target mapping holds with zero `[MapperIgnoreTarget]` (every view property has a same-name same-type source); if RMG errors name a property, fix the source/property mismatch, never the strategy
- [x] 3.3 Delete `Views/ProjectMapper.cs`. Rewire the seven call-sites to inject `IProjectsMapper` and call `mapper.ToView(...)`: `CreateProjectHandler`, `UpdateProjectHandler`, `ArchiveProjectHandler`, `GetProjectHandler`, `ListProjectsHandler` (the `Select(static project => ProjectMapper.ToView(project))` becomes a mapper-instance lambda), `GetProjectSettingsHandler`, `UpdateSettingsHandler`. Verify: `dotnet build comuki.slnx -c Debug` → 0 warnings / 0 errors; `rg -n "ProjectMapper" platform/src tests` returns nothing
- [x] 3.4 Register the singleton in `ProjectsApplicationExtensions.AddProjectsApplication`: `services.AddSingleton<IProjectsMapper, ProjectsMapper>();`. Verify: `dotnet run --project tests/unit/Comuki.Host.Unit.DiComposition -c Debug` → green (composition still resolves; add a resolution assertion for `IProjectsMapper` if the project asserts individual services)

## 4. Tests — mapper fidelity (design D6)

- [x] 4.1 Migrate `tests/unit/Comuki.Modules.Projects.Unit/ProjectMapperShould.cs` → `ProjectsMapperShould.cs`: construct `new ProjectsMapper()`, type the field as `IProjectsMapper`, keep all five facts and their assertions unchanged (full-fields mapping incl. Icon/Color/Tags, nullability pass-through, settings version pass-through). Verify: `dotnet run --project tests/unit/Comuki.Modules.Projects.Unit -c Debug` → all green, zero mocks introduced
- [x] 4.2 Extend the fidelity class (or add a sibling) with the missing-target proof: temporarily comment one property's mapping is NOT possible (strict strategy) — instead assert the convention by adding one view property in a scratch build and observing the RMG build error, then revert. Record the observed diagnostic id in the PR description. Verify: scratch build fails with the Mapperly diagnostic naming the property; final tree builds clean. (Observed diagnostic: **RMG023** "Required member `ScratchNoSource` on mapping target type `ProjectView` was not found", plus CS9035 — strictness fails the build as specified.)

## 5. Wire invariance pin (design D6)

- [x] 5.1 Add `ProjectViewWireShapeShould` to the Projects unit project: build a fully-populated `Project` via `Project.Create`, map it, serialize the view with `JsonSerializerOptions.Web`, and assert the parsed property-name set and their order equal the pinned literal list (`id, name, slug, description, profilesGitUrl, profilesGitRef, icon, color, tags, archived, archivedAt, createdAt, updatedAt`); same for `ProjectSettingsView`. Verify: the new facts pass; flipping any name/order in the pinned list fails the test
- [x] 5.2 kubb zero-diff gate: `cd dashboard && bun run generate-api`, then `git status --porcelain dashboard/src/shared/api/_generated` and `git diff --exit-code -- dashboard/src/shared/api/_generated`. Verify: zero diff — the generated client does not change (OpenAPI shape untouched by the record-form migration). (Proven as regen idempotency: 727 files hashed, second `generate-api` run changed 0. The `_generated` tree was stale against the baseline slices' host-side edits; the regen wrote exactly the baseline's own request-DTO content — no view-related bytes exist in the client, response schemas are `z.any()`.)

## 6. Enforcement (design D7)

- [x] 6.1 Add `tests/Comuki.Architecture.Tests/MappingConventionTests.cs`: hard-coded adopted-module list (initially `Comuki.Modules.Projects.Application`); assert (a) every public type in the module's `.Views` namespace exposes no public constructor with parameters (positional-record ban, reflection), (b) no static class named `*Mapper` in that namespace. Payload mappers live in `Intake.Infrastructure/Providers` — outside the asserted namespace, no false positives expected. Verify: `dotnet run --project tests/Comuki.Architecture.Tests -c Debug` → green; mutation-probe once (temporarily re-add a positional view or static mapper class) to prove the test fails, then revert. (Mutation probe: both facts failed naming `MutationProbeView` / `MutationProbeMapper`, then green after revert.)
- [x] 6.2 Run the full architecture project. Verify: `dotnet run --project tests/Comuki.Architecture.Tests -c Debug --no-build` → green (no layer violations introduced by the new mapper/interface)

## 7. Gates

- [x] 7.1 `dotnet format comuki.slnx --severity hidden` → then `dotnet build comuki.slnx -c Debug` → 0 warnings / 0 errors (format gate included)
- [x] 7.2 `dotnet run --project tests/unit/Comuki.Modules.Projects.Unit -c Debug` → green (fidelity + wire-shape pins)
- [x] 7.3 `dotnet run --project tests/Comuki.Architecture.Tests -c Debug --no-build` → green (convention guard)
- [x] 7.4 `cd dashboard && bun run typecheck && bun run lint && bun run test` → all exit 0 (FE untouched; guards the zero-diff regen claim from 5.2) — **verified by the parent** after the parallel FE worker's slice landed: typecheck/lint clean, 2084/2084, exit 0 (one load-flake re-run documented); regen byte-stable on repeat runs.
- [x] 7.5 Self-audit per `worker-audit.md` §2b over the changed files (naming, private methods — the partial mapper has none beyond generator-discovered members, `_ =` discards, `List<T>` surfaces); findings fixed or justified in the commit
- [x] 7.6 Follow-up waves filed as named stubs referencing this change's design table: `adopt-mapperly-identity`, `adopt-mapperly-intake` (+ memory), `adopt-mapperly-scheduler` (explicitly gated on `add-scheduled-jobs` landing). Verify: stub changes exist under `openspec/changes/` with proposal-only skeletons or an issue reference — no code — **filed 2026-09-26 (worker V2; the earlier DEFERRED note superseded by the filing instruction)**: each stub has `.openspec.yaml` (`skip_specs: true` — proposal-only, no deltas yet) + `README.md` + `proposal.md` scoped from the design table and pointing at `design.md` D1–D10 as the binding pattern; `openspec validate <name> --strict` → valid for all three (strict passes only with `skip_specs: true`; without it both strict and non-strict reject delta-free changes). The intake stub records the memory-fold resolution (one change, two per-module mappers); the scheduler stub carries the gate in its proposal's first line.

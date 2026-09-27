## Why

The global canon (`~/.agents/rules/csharp/mapping.md`) makes source-generated
Mapperly the default for entity→DTO mapping, yet every module in this repo
maps entities to views by hand: `ProjectMapper`, `AccountMapper`,
`ScheduledJobView.Of`, `IntakeTicketView.Of`, `MemoryFactViewMapper` and
friends are 1:1 field-shuffle classes that must be edited field-by-field on
every spine widening (add-project-visuals just paid that tax). The owner
decided: «да на mapperly делаем по правилу». This change adopts the canon:
toolchain, reference implementation, enforcement, and a wave plan for the
rest — with wire bytes pinned unchanged throughout.

## What Changes

- Riok.Mapperly (source generator) pinned in `Directory.Packages.props`
  and referenced per-module with `PrivateAssets="all" ExcludeAssets="runtime"`.
- Projects module becomes the reference implementation:
  `IProjectsMapper` + `[Mapper] partial sealed class ProjectsMapper`
  (`RequiredMappingStrategy.Target`), registered as a singleton in
  `AddProjectsApplication`; `ProjectMapper` (static) is deleted; the seven
  handlers map through the interface.
- `ProjectView` and `ProjectSettingsView` migrate from positional records to
  init-property records — **no wire change**: identical property names and
  JSON serialization; kubb client regen must be a zero-diff pure regen.
- `ProjectMapperShould` migrates to constructing the real generated mapper.
- A wire-invariance pin: a serialization test asserting the exact JSON
  property set of the views, plus the zero-diff kubb gate.
- Architecture tests extend: migrated modules' view types carry no positional
  constructors, and no static `*Mapper` classes appear under their
  `Application/Views`.
- Follow-up waves (separate changes, sized in design.md): Identity, Intake,
  Memory; Scheduler deferred until `add-scheduled-jobs` lands.

## Capabilities

### New Capabilities

- `backend-mapping`: the entity→view mapping convention — source-generated
  mappers per module (strict target mapping, singular `I{Module}Mapper`
  interface, singleton DI), init-property view records, the hand-written
  boundary (wire/format parsing stays hand-written per `mapper.md`), and the
  no-wire-change invariant for mapping migrations.

### Modified Capabilities

(none — view wire shapes are pinned unchanged; the projects REST surface
requirements are untouched)

## Impact

- Backend: `Directory.Packages.props` (one pin),
  `Comuki.Modules.Projects.Application` (views, new mapper + interface, DI
  installer, seven handlers), `tests/unit/Comuki.Modules.Projects.Unit`
  (mapper tests), `tests/Comuki.Architecture.Tests` (convention guard).
- FE: none (kubb regen is a zero-diff verification, not a change).
- No new endpoints, no permissions change, no breaking wire change.
- Builds on the uncommitted `add-project-visuals` baseline (its
  Icon/Color/Tags view shape is the starting point).

## Non-goals

- Migrating Identity / Intake / Memory / Scheduler in this change — sized
  waves recorded in design.md as follow-up changes.
- Touching wire/format-parsing mappers (Intake payload mappers ×4,
  `ProjectSettingsCacheEntryMapper`, host `ProjectsEndpointMapper`,
  `UploadArtifactResultMapper`) — they are the sanctioned hand-written
  boundary, not Mapperly material.
- Host-internal view shaping (RunsListHandler, WorkersReadQuery, Settings,
  Compute, Realtime projections) — composition-root shaping over query
  results, not the module entity→view pattern; recorded as a boundary
  decision, revisitable on request.
- EF `ExecuteUpdate`/LINQ-projection changes, DTO→DTO mappers (Costs
  contract→view), or any `RequiredMappingStrategy.None` usage.

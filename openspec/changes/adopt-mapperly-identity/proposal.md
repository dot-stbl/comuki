## Why

Wave 1 of the Mapperly adoption planned in `adopt-mapperly` (filed per its
task 7.6). The Identity module still maps entities to views by hand —
`Views/AccountMapper.cs` (a static class), `OidcLinkView.Of` and the
`ApiKeyView` factory — ≈ 4 projections of pure field-shuffle that must be
edited field-by-field on every spine widening. `adopt-mapperly` proved the
generated-mapper pattern on Projects (the zero-conversion reference); Identity
is the first wave to exercise the pattern's real extension point: value
conversions (`RoleKeys.Key`, `ScopeLevelKeys.Key`, typed-id→string) expressed
as user-mapping methods inside the partial mapper class.

This is a proposal-only stub: no specs, design or tasks yet. The binding
pattern is `openspec/changes/adopt-mapperly/design.md` (decisions D1–D10 and
the Context wave table) — this wave copies that shape and does not
relitigate it. Scope comes from the design table's Identity row: "4
projections / 1 static class + 2 factories".

## What Changes

- `Comuki.Modules.Identity.Application.csproj` references the already-pinned
  `Riok.Mapperly` (CPM pin landed in `adopt-mapperly`) with the same
  generator wiring: `<PrivateAssets>all</PrivateAssets>` +
  `<ExcludeAssets>runtime</ExcludeAssets>`.
- A singular `IIdentityMapper` + `[Mapper(RequiredMappingStrategy =
  RequiredMappingStrategy.Target)] partial sealed class IdentityMapper` in
  the module's Views layer, registered as a singleton in the Identity
  application installer. Note the namespace gotcha: `[Mapper]` and
  `RequiredMappingStrategy` live in `Riok.Mapperly.Abstractions`, not
  `Riok.Mapperly`.
- `AccountMapper` (static) is deleted; the `OidcLinkView.Of` and `ApiKeyView`
  factory constructions migrate to mapper methods (`ToView` overloads);
  handlers inject the interface. Exact method enumeration is done at wave
  planning against the landed code, not fixed here.
- Conversions (`RoleKeys.Key`, `ScopeLevelKeys.Key`, typed-id→string) are
  user-mapping methods declared inside the partial mapper class — the
  generator's sanctioned extension point (design.md D8; they may be
  `private`, exempt from the general private-method rule the same way the
  EF parameterless ctor is).
- Identity view records migrate positional→init-property `required` records
  with **no wire change**: serialization pin test (exact JSON property set)
  + zero-diff kubb regen, exactly as Projects did.
- `MappingConventionTests` adopted-module list appends
  `Comuki.Modules.Identity.Application`.

## Capabilities

### New Capabilities

(none)

### Modified Capabilities

- `backend-mapping` (created by `adopt-mapperly`): the adoption list widens
  to include Identity. No requirement text changes — the spec already says
  each follow-up wave appends its module.

## Impact

- Backend only: `Comuki.Modules.Identity.Application` (views, mapper +
  interface, DI installer, handlers), `tests/unit/
  Comuki.Modules.Identity.Unit` (mapper fidelity + wire pin),
  `tests/Comuki.Architecture.Tests` (one list entry).
- FE: none (kubb regen is a zero-diff verification gate, not an edit).
- No new endpoints, no permissions change, no breaking wire change.

## Non-goals

- Reopening any decision recorded in `adopt-mapperly/design.md` (D1–D10) —
  this wave executes, it does not redesign.
- Touching host request→command shaping or any Infrastructure mapping —
  the D4 hand-written boundary stands.
- `RequiredMappingStrategy.None` or `[MapperIgnoreTarget]` escapes for
  properties that merely need a user-mapping method.
- Migrating other modules — each wave is module-scoped (backend-mapping
  spec: "A wave ships alone").

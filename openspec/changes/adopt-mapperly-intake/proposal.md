## Why

Wave 2 of the Mapperly adoption planned in `adopt-mapperly` (filed per its
task 7.6). The Intake and Memory modules still map entities to views by
hand: Intake's `IntakeTicketView.Of`, `SourceConnectionView.Of` and
`AdmissionRuleView.Of` (3 view factories); Memory's `MemoryFactViewMapper`
(file-static inside `EfMemoryStore`) and `LearningCandidateView.Of` (2
sites). Both design-table rows are wave 2, so they ship as one change
with one mapper per module — the resolution of the design table listing
them as separate rows is recorded below.

This is a proposal-only stub: no specs, design or tasks yet. The binding
pattern is `openspec/changes/adopt-mapperly/design.md` (decisions D1–D10
and the Context wave table) — this wave copies that shape and does not
relitigate it.

## What Changes

- Intake: a singular `IIntakeMapper` + `[Mapper] partial sealed class
  IntakeMapper` (`RequiredMappingStrategy.Target`, singleton in the Intake
  application installer); the three `.Of` factories migrate to `ToView`
  methods; handlers inject the interface. Note the namespace gotcha:
  `[Mapper]` and `RequiredMappingStrategy` live in
  `Riok.Mapperly.Abstractions`, not `Riok.Mapperly`.
- Memory: a singular `IMemoryMapper` + `MemoryMapper` in the Memory
  module's application layer; `MemoryFactViewMapper` and
  `LearningCandidateView.Of` migrate onto it. The construction site in
  `EfMemoryStore` (Infrastructure) consumes the interface.
- The four Intake `*PayloadMapper`s (`GitHubPayloadMapper`,
  `GitLabPayloadMapper`, `JiraPayloadMapper`, `YandexTrackerPayloadMapper`
  in `Infrastructure/Providers`) **stay hand-written** — external-JSON→
  domain wire parsing, the sanctioned D4 boundary. Memory's `MemoryFactSql`
  raw-SQL row shaping **stays hand-written** for the same reason.
- Conversions (keys, enum→string) are user-mapping methods inside the
  partial mapper classes (design.md D8).
- Intake and Memory view records migrate positional→init-property
  `required` records with **no wire change**: serialization pin test +
  zero-diff kubb regen, exactly as Projects did.
- `MappingConventionTests` adopted-module list appends
  `Comuki.Modules.Intake.Application` and
  `Comuki.Modules.Memory.Application`.
- Both module csprojs reference the already-pinned `Riok.Mapperly` (CPM pin
  landed in `adopt-mapperly`) with `<PrivateAssets>all</PrivateAssets>` +
  `<ExcludeAssets>runtime</ExcludeAssets>`.

## Capabilities

### New Capabilities

(none)

### Modified Capabilities

- `backend-mapping` (created by `adopt-mapperly`): the adoption list widens
  to include Intake and Memory. No requirement text changes — the spec
  already says each follow-up wave appends its module.

## Impact

- Backend only: `Comuki.Modules.Intake.Application` (views, mapper,
  handlers), `Comuki.Modules.Memory.Application` (views, mapper) +
  `Comuki.Modules.Memory.Infrastructure` (`EfMemoryStore` construction
  site), the two modules' unit test projects (mapper fidelity + wire
  pins), `tests/Comuki.Architecture.Tests` (two list entries).
- FE: none (kubb regen is a zero-diff verification gate, not an edit).
- No new endpoints, no permissions change, no breaking wire change.

## Non-goals

- Touching the four `*PayloadMapper`s or `MemoryFactSql` — wire/format
  parsing is the D4 hand-written boundary, encoded in the backend-mapping
  spec requirement "Hand-written mapping stays at the format boundary".
- Reopening any decision recorded in `adopt-mapperly/design.md` (D1–D10).
- `RequiredMappingStrategy.None` or `[MapperIgnoreTarget]` escapes.
- Migrating other modules — each wave is module-scoped (this change is two
  module-scoped migrations shipped together, each independently pinnable).

## Recorded ambiguity resolution

The design table lists Memory as its own row ("Wave 2", 2 sites) and D9
sizes "Intake ≈ 3 views; Memory ≈ 2" without saying whether they are one
change or two. The parent folded Memory into this change: both rows are
wave 2 and the pair is small. Resolution: **one change, two mappers** —
Intake and Memory each keep their own `I{Module}Mapper` (the convention is
per-module), are appended separately to the arch-test adoption list, and
carry separate wire pins, so either module can still be verified (or
reverted) independently within the single change.

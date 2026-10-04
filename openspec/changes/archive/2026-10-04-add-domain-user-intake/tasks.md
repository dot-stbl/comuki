# Domain-user intake — Custom domain types

## 1. Domain layer

- [x] 1.1 Add `ProjectDomainType` enum (`Standard = 0`, `Custom = 1`, `Hybrid = 2`) in `Comuki.Modules.Projects.Domain.Settings`
- [x] 1.2 Add `DomainType` and `CustomDomainTypesJson` properties on `ProjectSettings`; extend `CreateDefaults` (Standard + null JSON) and `Apply` (mutate both new fields, bump `Version`)

## 2. Application layer — resolver

- [x] 2.1 Add `IProjectDomainTypeResolver` port in `Comuki.Modules.Projects.Application.DomainTypes`
- [x] 2.2 Implement `ProjectDomainTypeResolver` (Standard → fixed key, Custom → JSON lookup, Hybrid → standard-then-JSON); add typed `ProjectDomainTypeNotMappedException`
- [x] 2.3 Register `IProjectDomainTypeResolver` as a singleton in `ProjectsApplicationExtensions`

## 3. Application layer — settings view / command

- [x] 3.1 Extend `ProjectSettingsView` with `DomainType` and `CustomDomainTypesJson`
- [x] 3.2 Extend `ProjectMapper.ToView(ProjectSettings)` to populate the new fields
- [x] 3.3 Extend `UpdateSettingsCommand` and `UpdateSettingsHandler` to read and pass through the new fields
- [x] 3.4 Extend `UpdateSettingsValidator` with FV rules: `Standard` requires no JSON, `Custom`/`Hybrid` JSON max length 8192 + JSON shape `{ "code": "implement" }`

## 4. Infrastructure layer

- [x] 4.1 Extend `ProjectSettingsConfiguration` with the two columns (`domain_type` text, `custom_domain_types_json` text NULL)
- [x] 4.2 Generate the EF migration via `dotnet ef migrations add AddProjectDomainRouting --context ProjectsDbContext --project ...\Comuki.Modules.Projects.Infrastructure --startup-project ...\Comuki.Migrator` (design-time factory picks up the connection string)
- [x] 4.3 Verify `ProjectsDbContextModelSnapshot` reflects the new columns

## 5. Tests

- [x] 5.1 Domain defaults test — new settings row has `Standard` and null JSON (extend `ProjectDomainShould`)
- [x] 5.2 Domain Apply test — `Apply` mutates `DomainType` and `CustomDomainTypesJson` (extend `ProjectDomainShould`)
- [x] 5.3 Mapper test — view carries the new fields (extend `ProjectsMapperShould`; actual file name is plural — `ProjectsMapperShould.cs`, see PR #173 follow-up)
- [x] 5.4 Handler test — `UpdateSettingsHandler` passes new fields through to the store (extend `ProjectHandlersShould`; new `UpdateSettingsPersistsAsync` asserts `view.DomainType = Custom` and `view.CustomDomainTypesJson = """{"code":"implement"}"""`, transitively proving the mapper pass-through + `settings.Received(1).SaveAsync`)
- [x] 5.5 Resolver tests — `ProjectDomainTypeResolverShould` covers Standard / Custom / Hybrid / unknown / malformed JSON
- [x] 5.6 Validator tests — `UpdateSettingsValidatorShould` covers valid JSON, oversized JSON, malformed JSON for Custom/Hybrid (`AcceptCustomWithValidJson` / `AcceptHybridWithValidJson` / `RefuseAbsentJsonForCustom` [Theory null/""/"   "] / `RefuseOversizedJson` [Theory Custom+Hybrid, cap 8192] / `RefuseMalformedJson` [Theory Custom+Hybrid])
- [x] 5.7 Architecture test — `Projects.Domain` still has no outer-layer refs (already covered); add an explicit assertion that `Projects.Application.DomainTypes` does not reference the engine or the migrator

## 6. Gates

- [ ] 6.1 `dotnet build comuki.slnx -c Debug` → 0/0 — **PRE-EXISTING BREAK on master**: 2× MSB3030 on `tests/unit/Comuki.Host.Brain.Unit/Comuki.Host.Brain.Unit.csproj` (lines 24–25 reference `deploy/hybrid/host.Dockerfile` and `deploy/hybrid/infra-dev.yaml`; the directory was deleted in 729a06b1 after the csproj was written in 67ac6e3d1). Out of scope for this change; recorded as inherited.
- [x] 6.2 `dotnet format comuki.slnx --verify-no-changes --severity warn` → exit 0
- [x] 6.3 `dotnet run --project tests/Comuki.Architecture.Tests -c Debug --no-build` → 55 total / 0 failed / 0 skipped (brief expected ~49; actual 55 from this run)
- [x] 6.4 `dotnet run --project tests/unit/Comuki.Modules.Projects.Unit -c Debug --no-build` → 200 total / 0 failed / 0 skipped (includes the new domain-type mapper/handler/validator cases)
- [x] 6.5 (if FE touched) `cd dashboard && bun run typecheck && bun run lint && bun run test` — slice 1 does NOT touch the FE; recorded as SKIPPED with rationale
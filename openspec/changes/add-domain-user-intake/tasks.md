# Domain-user intake — Custom domain types

## 1. Domain layer

- [ ] 1.1 Add `ProjectDomainType` enum (`Standard = 0`, `Custom = 1`, `Hybrid = 2`) in `Comuki.Modules.Projects.Domain.Settings`
- [ ] 1.2 Add `DomainType` and `CustomDomainTypesJson` properties on `ProjectSettings`; extend `CreateDefaults` (Standard + null JSON) and `Apply` (mutate both new fields, bump `Version`)

## 2. Application layer — resolver

- [ ] 2.1 Add `IProjectDomainTypeResolver` port in `Comuki.Modules.Projects.Application.DomainTypes`
- [ ] 2.2 Implement `ProjectDomainTypeResolver` (Standard → fixed key, Custom → JSON lookup, Hybrid → standard-then-JSON); add typed `ProjectDomainTypeNotMappedException`
- [ ] 2.3 Register `IProjectDomainTypeResolver` as a singleton in `ProjectsApplicationExtensions`

## 3. Application layer — settings view / command

- [ ] 3.1 Extend `ProjectSettingsView` with `DomainType` and `CustomDomainTypesJson`
- [ ] 3.2 Extend `ProjectMapper.ToView(ProjectSettings)` to populate the new fields
- [ ] 3.3 Extend `UpdateSettingsCommand` and `UpdateSettingsHandler` to read and pass through the new fields
- [ ] 3.4 Extend `UpdateSettingsValidator` with FV rules: `Standard` requires no JSON, `Custom`/`Hybrid` JSON max length 8192 + JSON shape `{ "code": "implement" }`

## 4. Infrastructure layer

- [ ] 4.1 Extend `ProjectSettingsConfiguration` with the two columns (`domain_type` text, `custom_domain_types_json` text NULL)
- [ ] 4.2 Generate the EF migration via `dotnet ef migrations add AddProjectDomainRouting --context ProjectsDbContext --project ...\Comuki.Modules.Projects.Infrastructure --startup-project ...\Comuki.Migrator` (design-time factory picks up the connection string)
- [ ] 4.3 Verify `ProjectsDbContextModelSnapshot` reflects the new columns

## 5. Tests

- [ ] 5.1 Domain defaults test — new settings row has `Standard` and null JSON (extend `ProjectDomainShould`)
- [ ] 5.2 Domain Apply test — `Apply` mutates `DomainType` and `CustomDomainTypesJson` (extend `ProjectDomainShould`)
- [ ] 5.3 Mapper test — view carries the new fields (extend `ProjectMapperShould`)
- [ ] 5.4 Handler test — `UpdateSettingsHandler` passes new fields through to the store (extend `ProjectHandlersShould`)
- [ ] 5.5 Resolver tests — `ProjectDomainTypeResolverShould` covers Standard / Custom / Hybrid / unknown / malformed JSON
- [ ] 5.6 Validator tests — `UpdateSettingsValidatorShould` covers valid JSON, oversized JSON, malformed JSON for Custom/Hybrid
- [ ] 5.7 Architecture test — `Projects.Domain` still has no outer-layer refs (already covered); add an explicit assertion that `Projects.Application.DomainTypes` does not reference the engine or the migrator

## 6. Gates

- [ ] 6.1 `dotnet build comuki.slnx -c Debug` → 0/0
- [ ] 6.2 `dotnet format comuki.slnx --verify-no-changes --severity warn` → exit 0
- [ ] 6.3 `dotnet run --project tests/Comuki.Architecture.Tests -c Debug --no-build` → 22+ green
- [ ] 6.4 `dotnet run --project tests/unit/Comuki.Modules.Projects.Unit -c Debug --no-build` → green (49 baseline + new tests)
- [ ] 6.5 (if FE touched) `cd dashboard && bun run typecheck && bun run lint && bun run test` — slice 1 does NOT touch the FE; record the skip
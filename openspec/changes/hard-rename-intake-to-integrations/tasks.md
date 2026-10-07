## 1. Domain layer rename

- [ ] 1.1 Move/rename `Comuki.Modules.Intake.Domain` to `Comuki.Modules.Integrations.Domain` (directory + csproj + namespace + assembly name); rename `IncomingTicket`→`InboundItem`, `IncomingTicketId`→`InboundItemId`, `IntakeTicketStatus`→`InboundItemStatus`, `InboundTicketKind`→`InboundItemKind`; leave `SourceConnection`, `AdmissionRule`, `AdmissionMode`, `SyncJob`, `SyncJobStatus`, `IntakeDelivery`→`Delivery`, `DeliveryOutcomes`, `TicketProvider` class names as-is except namespace — verify: `dotnet build platform/src/modules/Integrations/Comuki.Modules.Integrations.Domain/Comuki.Modules.Integrations.Domain.csproj` succeeds.

## 2. Application layer rename

- [ ] 2.1 Move/rename `Comuki.Modules.Intake.Application` to `Comuki.Modules.Integrations.Application`; rename `IntakeOptions`→`IntegrationsOptions` (`SectionName` `"Intake"`→`"Integrations"`), `IIntakeStore`→`IIntegrationsStore`, `IIntakeProfileRouter`→`IIntegrationProfileRouter`, `IntakeTicketView`→`InboundItemView`, `IntakeTicketExceptions`→`InboundItemExceptions`, `IntakeSourceExceptions`→`IntegrationsSourceExceptions`, `IntakeApplicationExtensions`→`IntegrationsApplicationExtensions`; rename all `intake.<code>` problem-detail code string literals to `integration.<code>` per the naming table (grep for `"intake\.` in the renamed project — must be zero hits) — verify: `dotnet build platform/src/modules/Integrations/Comuki.Modules.Integrations.Application/Comuki.Modules.Integrations.Application.csproj` succeeds.

## 3. Infrastructure layer rename + migration squash

- [ ] 3.1 Move/rename `Comuki.Modules.Intake.Infrastructure` to `Comuki.Modules.Integrations.Infrastructure`; rename `IntakeDbContext`→`IntegrationsDbContext`, `IntakeDatabase`→`IntegrationsDatabase` (schema constant `"intake"`→`"integrations"`, migrations history table to `integrations.n_history` — domain convention `<schema>.n_history`; the legacy `__comuki_intake` token does **not** exist in this repo and is not introduced here), `IntakeStore`→`IntegrationsStore`, table names `intake_tickets`→`inbound_items`, `intake_deliveries`→`deliveries` in the EF configurations; delete the 4 existing migrations + `IntakeDbContextModelSnapshot.cs`; rename `Comuki.Migrator/Factories/Intake/IntakeDesignTimeFactory.cs` → `Factories/Integrations/IntegrationsDesignTimeFactory.cs`; update `Comuki.Shared.Migrations/Targets/MigrationTargets.cs` registry entry from `"intake"`/`IntakeDatabase.Schema` to `"integrations"`/`IntegrationsDatabase.Schema`; rename `IntakeProvidersExtensions`→`IntegrationsProvidersExtensions`, `IntakePersistenceExtensions`→`IntegrationsPersistenceExtensions`; provider subfolders (GitHub/GitLab/Jira/YandexTracker) get namespace-only updates, class names unchanged. **Atomic:** all of the above must land together **before** `dotnet ef migrations add InitialIntegrationsSchema --context IntegrationsDbContext --project Comuki.Modules.Integrations.Infrastructure --startup-project Comuki.Migrator` is invoked (the design-time factory + Migrator registry + Infrastructure rename have to be consistent for `dotnet ef` to discover the renamed context). After the rename is consistent, generate the single fresh `InitialIntegrationsSchema` migration producing schema `integrations` and history table `integrations.n_history` — verify: `dotnet ef migrations list --context IntegrationsDbContext --project Comuki.Modules.Integrations.Infrastructure --startup-project Comuki.Migrator` shows exactly one migration on the new context; `dotnet build platform/src/modules/Integrations/Comuki.Modules.Integrations.Infrastructure/Comuki.Modules.Integrations.Infrastructure.csproj` succeeds.

## 4. Host composition, routes, permissions, config, deploy schema bootstrap

- [ ] 4.1 Rename `platform/src/host/Comuki.Host/Intake/**` (controllers + request models) directory to `Integration/`, update route attributes per the route table (`/api/v1/inbox*`→`/api/v1/integration/inbox*`, `/api/v1/tickets`→`/api/v1/integration/items`, `/api/v1/sources*`→`/api/v1/integration/sources*`, `/api/v1/admission-rules*`→`/api/v1/integration/admission-rules*`); **also** update `platform/src/host/Comuki.Host/ApiRoutes.cs` (7 constants: `Inbox`, `InboxCatalog`, `InboxClaim`, `Tickets`, `Sources`, `SourceProbe`, `AdmissionRules`/`Rule`) to the new prefix — these are the string constants the route attributes reference; the controller attribute rewrite alone is not enough. Update `HostComposer.cs`'s `AddIntakeApplication()` / `AddIntakePersistence()` / `AddIntakeProviders()` calls and `IntakeOptions` / `IntakeWorkerDefaults` binding to the renamed types/section, **plus** the `Errors/Handlers/Intake/**` handler registration at lines ~18 / ~506 (2 handler files in `platform/src/host/Comuki.Host/Errors/Handlers/Intake/` → `Errors/Handlers/Integration/`). Rename `Comuki.Migrator/Factories/Intake/IntakeDesignTimeFactory.cs`→`Factories/Integrations/IntegrationsDesignTimeFactory.cs`; update `Comuki.Shared.Migrations/Targets/MigrationTargets.cs`'s registry entry from `"intake"` / `IntakeDatabase.Schema` to `"integrations"` / `IntegrationsDatabase.Schema`; update `platform/src/shared/Comuki.Shared.Migrations/DatabaseSchemaEnsurer.cs` lines ~54 and ~79 (the 2 places the old `intake` schema name is referenced). Rename `Permissions.IntakeRead` / `IntakeClaim` → `IntegrationRead` / `IntegrationClaim` (`"intake:read"`→`"integration:read"`, `"intake:claim"`→`"integration:claim"`) and update `RoleMatrix.cs` grants. Update the **6** `comuki.slnx` entries that point at `platform/src/modules/Intake/**` to the new `platform/src/modules/Integrations/**` physical locations and assembly names. Rename `deploy/k8s/postgres.yaml`, `deploy/helm/templates/postgres.yaml`, `deploy/compose/init/01-schemas.sql`'s `CREATE SCHEMA IF NOT EXISTS intake;` to `integrations`; rename `deploy/config.example.toml`'s `[intake]` / `[intake.worker]` sections at lines ~284 / ~293 to `[integrations]` / `[integrations.worker]`; confirm (do not action, just confirm) `deploy/hybrid/migrate-job-dev.yaml` for a stray `intake` label and rename for consistency if present — verify: `dotnet build comuki.slnx -c Debug` (full solution) succeeds with zero warnings; `grep -rn "intake:" platform/src` returns zero hits; `grep -rn "CREATE SCHEMA IF NOT EXISTS intake" deploy/` returns zero hits.

## 5. Test rename

- [ ] 5.1 Move/rename `tests/unit/Comuki.Modules.Intake.Unit` → `tests/unit/Comuki.Modules.Integrations.Unit`, `tests/integration/Comuki.Host.Integration.Intake` → `Comuki.Host.Integration.Integrations`, `tests/integration/Comuki.Modules.Intake.Integration.Migrations` → `Comuki.Modules.Integrations.Integration.Migrations` (rewrite its `IntakeMigrationsShould` assertions for the new single-migration baseline, **not** the old 4-step history: assert exactly one migration on `IntegrationsDbContext`, with tables `inbound_items`/`deliveries` and history table `integrations.n_history`); rename `tests/Comuki.Architecture.Tests/IntakeModuleLayerTests.cs`→`IntegrationsModuleLayerTests.cs` and update the namespace constants inside it, plus the `Comuki.Modules.Intake.*` references in `ScopeGuardTests.cs` and `SharedContractsModuleBoundaryTests.cs`; **stragglers discovered during reconnaissance (2026-09-25):** update incidental `Intake`-named references in `tests/Comuki.Architecture.Tests/ProjectsModuleLayerTests.cs`, `tests/Comuki.Architecture.Tests/RunStatusesShould.cs`, and the Architecture.Tests `csproj` itself (boundary tests, doc-comment mentions — do not treat the file list above as exhaustive); update incidental references in `Comuki.EndToEnd.AgentLoop` fixtures and `Comuki.AgentTest.Runner` scenario YAML that hit the old routes — verify: `dotnet build comuki.slnx -c Debug` (warnings-as-errors) succeeds for `tests/`; do not run the test suites themselves in this OpenSpec authoring change (implementation-time concern) — just confirm this task's own verify command is stated correctly for whoever executes it.

- [ ] 5.2 E2E config-key rename — `Intake:Worker:*` → `Integrations:Worker:*` in 3 host fixtures plus the load comments referencing the old name: `tests/tools/Comuki.AgentTest.Runner/AgentLoopHost.cs:110-112`, `CrownScenarioHost.cs:100-102`, `RealPiFakeModelHost.cs:120-122`, and `ScenarioRunner.cs:114`. Update `scripts/ci/e2e-smoke.mjs` line ~66 (fixture path references the old `Intake/...` layout) and its sibling integration test at `tests/integration/.../test:515` — verify: `rg -n "Intake:Worker|Intake:" tests/tools/ scripts/ci/e2e-smoke.mjs` returns zero hits.

## 6. Dashboard + CLI contract regeneration

- [ ] 6.1 After workstream 4 compiles, run `dashboard`'s `bun run generate-api` (= `dotnet build ../comuki.slnx && bunx @kubb/cli@4.39.2 generate && prettier --write "src/shared/api/_generated/**/*.ts"`, defined in `dashboard/package.json`); run `cli`'s `bun run generate:contracts` (= `dotnet build ../comuki.slnx -c Debug && bun scripts/normalize-openapi.ts && bunx @kubb/cli@4.39.2 generate && dotnet run --project ../tools/Comuki.Codegen.Realtime ...`, defined in `cli/package.json`); update hand-written consumers in `dashboard/src/domains/{inbox,sources,tasks}/**` that reference the renamed generated type/route names (`IntakeTicketView`→`InboundItemView`, `/api/v1/inbox*`→`/api/v1/integration/inbox*`, `POST /api/v1/tickets`→`POST /api/v1/integration/items`) — **the `dashboard/src/domains/tasks/**` consumer surface is ~17 files and was missed in the original reconnaissance**, treat it as part of this task; also update `dashboard/src/shared/api/polling.ts` (polls the feed for `intake`-typed updates), `dashboard/src/mock/{sources,tasks}.seed.ts` (mock seed data with literal `intake` keys), and `dashboard/src/domains/settings/tracker-panel/**` if it carries Intake-typed form fields. **i18n:** rename the i18n **key** `"intake"` → `"integrations"` across the **14** locale files in `dashboard/src/shared/i18n/locales/**/intake.json` (en + ru + the others present), and update the **value** `"Intake"` → `"Integrations"` in the same locale files in lock-step — both halves of one locale file rename per locale. Optionally (low-risk, does not block the gate) rename the dashboard nav's cosmetic `id: "intake"` / `label: "Intake"` in `dashboard/src/app/layout/{nav.ts,nav-sections.ts}` to `"integrations"` / `"Integrations"`; do NOT restructure `domains/inbox` / `domains/sources` / `domains/tasks` into a merged `domains/integrations` folder (out of scope, belongs to #104) — verify (extended): `grep -rn "Intake\|intake:" dashboard/src cli/src --include=*.ts --include=*.json --exclude-dir=node_modules` returns zero hits outside comments that explicitly reference the historical rename (ideally zero hits at all).

## 7. Documentation — live references

- [ ] 7.1 Update **live** documentation references to the Intake surface — historical `audits/**` archives are **not** in scope:
  - `README.md` — 3 references to the Intake module / routes / permissions.
  - `.agents/STATE.md`, `.agents/ROADMAP.md`,
    `.agents/database-schemas/integrations.md` (and its companion
    `intake.md` if still present), `.agents/docs/host-internals/**`,
    `.agents/docs/architecture/comuki-architecture.md`,
    `.agents/rules/coding/testing-integration.md` line ~95 (one
    doc-comment reference to the intake migration suite).
  - `DESIGN.md` (repo root) — 2 cosmetic references in design-system prose.
  - `platform/src/modules/.../MemorySeeder.cs` line ~42 (doc-comment
    reference to the Intake surface; cosmetic).
  - `platform/src/shared/Comuki.Shared.Secrets/SecretRefUnsetException.cs`
    line ~9 (doc comment referencing the Intake webhook flow; cosmetic).
  - These are all cosmetic / no runtime behaviour; nothing routes
    through them. Verify by the absence of stale `Intake`-prefixed
    references in the listed paths — `rg -n "\bIntake\b" <each>` after
    the edit should return zero hits outside the explicitly
    historical audit archives.

## 8. Runbook — dev/stage cutover (textual, not app code)

- [ ] 8.1 **Textual runbook for the dev/stage environment cutover.** Not
  app logic, not committed code; an operator step executed once at
  deploy time:

  ```text
  # Dev/stage cutover (one-time, per environment)

  1. Drop the old `intake` schema (and its prior history table — the
     `__comuki_intake` token does not exist in this repo, so the
     physically-existing history table, if any, follows the project's
     earlier convention and is dropped together with the schema).
  2. Confirm `deploy/compose/init/01-schemas.sql` was updated to
     `CREATE SCHEMA IF NOT EXISTS integrations;` (it must be, for any
     fresh local `postgres` volume to come up with the new schema).
  3. Re-run the host's `migrate:dev` against the freshly-empty database
     — `Comuki.Migrator` discovers the renamed `IntegrationsDbContext`
     through the new `MigrationTargets` entry and emits the single
     `InitialIntegrationsSchema` migration, creating schema
     `integrations` and history table `integrations.n_history` from
     scratch.
  4. Smoke-check: `dotnet run --project
     tests/integration/Comuki.Modules.Integrations.Integration.Migrations`
     against a clean local Postgres (Podman machine, see
     `.agents/rules/process/local-test-runtime.md`) asserts exactly one
     migration on the new context, with tables `inbound_items` /
     `deliveries` / history `integrations.n_history`.
  ```

  This block is the `tasks.md`-side of design.md §2's "one-time
  operator step". It is **not** automated — `Comuki.Migrator` never
  auto-migrates dev/stage (deliberate, per design.md §2).
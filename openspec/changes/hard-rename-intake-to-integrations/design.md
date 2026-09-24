# Design — Hard rename `Intake` → `Integrations`

## Context

The current Intake bounded context is a three-project modular monolith —
`Comuki.Modules.Intake.{Domain,Application,Infrastructure}` — backed by a
PostgreSQL `intake` schema, four EF Core migrations under
`__comuki_intake`, and the aggregate root `IncomingTicket`. Public HTTP
routes sit at `/api/v1/{inbox,tickets,sources,admission-rules}` (with
anonymous provider ingress at `/api/hooks/{provider}/{key}`), permission
keys are `intake:read` / `intake:claim`, and the dashboard/CLI each ship
generated OpenAPI-derived TS contracts naming `IntakeTicketView`,
`PostApiV1Tickets`, etc.

`add-mission-cowork` (issue #70) cannot land its `add-work-management`
slice (#89) on top of this — it requires `Comuki.Modules.Integrations.*`
from day one, schema `integrations`, entity `InboundItem`, and
`/api/v1/integration/*` routes. The epic explicitly designates this
**task 3.0** as the dedicated pre-release hard rename so every later
Mission/Work change builds on the right foundation instead of carrying
`Intake` debt. See `proposal.md` for the full Why.

## Goals / Non-Goals

**Goals**

- Zero behavior change at runtime — every existing scenario produces the
  same observable result after the rename.
- Zero compatibility aliases — no dual-write, no `ALTER SCHEMA`, no
  shimmed routes.
- Clean, squashed EF Core migration baseline for the renamed module
  (one `InitialIntegrationsSchema` migration, history table
  `__comuki_integrations`).
- All generated OpenAPI-derived TypeScript contracts (dashboard Kubb
  client, CLI generated client) regenerated against the renamed
  routes/DTOs and drift-checked (zero `Intake*` / `intake:` hits
  outside explicit historical references).

**Non-goals** (full text in `proposal.md`'s Non-goals section)

- Introducing Work/Task creation on admission — `add-work-management` (#89).
- Outbound Project webhook subscriptions — `add-outbound-webhooks` (#103).
- Any runtime compatibility layer or data-preserving `ALTER SCHEMA`.
- Any change to provider mapping logic, signature verification, or
  sync-back comment behavior beyond identifier renames.
- Restructuring the dashboard's `domains/inbox` and `domains/sources`
  folders into a consolidated `domains/integrations/` —
  `add-mission-dashboard` (#104)'s job.

## Decisions

### 1. Naming table (epic-mandated)

| Old | New |
|---|---|
| `Comuki.Modules.Intake.{Domain,Application,Infrastructure}` | `Comuki.Modules.Integrations.{Domain,Application,Infrastructure}` |
| PostgreSQL schema `intake` | `integrations` |
| Migrations history table `__comuki_intake` | `__comuki_integrations` |
| `IntakeDbContext` | `IntegrationsDbContext` |
| `IncomingTicket` (entity) | `InboundItem` |
| `IncomingTicketId` | `InboundItemId` |
| `IntakeTicketStatus` | `InboundItemStatus` |
| `InboundTicketKind` | `InboundItemKind` (values `Issue`/`PullRequest` unchanged) |
| `IntakeTicketView` | `InboundItemView` |
| table `intake_tickets` | `inbound_items` |
| table `intake_deliveries` | `deliveries` (matches existing unprefixed convention of `source_connections`/`admission_rules`/`sync_jobs`, now schema-qualified as `integrations.deliveries`) |
| permission `intake:read` | `integration:read` |
| permission `intake:claim` | `integration:claim` |
| problem-detail codes `intake.*` | `integration.*` (e.g. `intake.ticket_not_found`→`integration.inbound_item_not_found`, `intake.ticket_conflict`→`integration.inbound_item_conflict`, `intake.secret_env_ref_unset`→`integration.secret_env_ref_unset`, `intake.provider_settings_invalid`→`integration.provider_settings_invalid`, `intake.webhook_rejected`→`integration.webhook_rejected`, `intake.signature_invalid`→`integration.signature_invalid`, `intake.source_provider_not_found`→`integration.source_provider_not_found`, `intake.connection_not_found`→`integration.connection_not_found`, `intake.rule_not_found`→`integration.rule_not_found`) |
| config section `Intake` (C# `IOptions` section name) / TOML `[intake]`, `[intake.worker]` | `Integrations` / `[integrations]`, `[integrations.worker]` |

Route renames:

| Old | New |
|---|---|
| `GET /api/v1/inbox` | `GET /api/v1/integration/inbox` |
| `GET /api/v1/inbox/catalog` | `GET /api/v1/integration/inbox/catalog` |
| `POST /api/v1/inbox/claim` | `POST /api/v1/integration/inbox/claim` |
| `POST /api/v1/tickets` | `POST /api/v1/integration/items` |
| `/api/v1/sources` (GET/POST/PUT/DELETE) | `/api/v1/integration/sources` |
| `POST /api/v1/sources/probe` | `POST /api/v1/integration/sources/probe` |
| `POST /api/v1/sources/{id}/probe` | `/api/v1/integration/sources/{id}/probe` |
| `PUT /api/v1/sources/{sourceId}/rules/{ruleId}` | `PUT /api/v1/integration/sources/{sourceId}/rules/{ruleId}` |
| `/api/v1/admission-rules` (+ `/{ruleId}`) | `/api/v1/integration/admission-rules` (+ `/{ruleId}`) |
| `POST /api/hooks/{provider}/{key}` | **unchanged** (already anonymous, routing-key-addressed) |

Not renamed (epic does not name them — kept to minimize blast radius):

- `SourceConnection` entity/table `source_connections`
- `AdmissionRule` entity/table `admission_rules`, `AdmissionMode`
- `SyncJob` entity/table `sync_jobs`, `SyncJobStatus`
- Provider names (`github`, `gitlab`, `jira`, `yandex-tracker`) and their
  provider-specific classes (`GitHubTicketSync`, `JiraPayloadMapper`, etc.)
- Permission keys `source:write`, `run:create`
- The word "ticket" when it refers to the **external tracker's own**
  issue/PR (e.g. "posts a status comment back to the originating
  ticket") — only rename "ticket" to "inbound item" when it refers to
  **our** `IncomingTicket`/`InboundItem` aggregate.

### 2. DB migration strategy — fresh squashed baseline, no carried history

**Chosen:** delete the four existing Intake migrations
(`InitialIntakeSchema`, `UseSchemas`, `InboundTicketKind`,
`AddSourceConnectionWebhookSecret`) plus `IntakeDbContextModelSnapshot.cs`,
then generate one fresh `InitialIntegrationsSchema` migration from the
renamed entity configurations, producing schema `integrations` and
history table `__comuki_integrations` from scratch. Dev/staging data is
reset by dropping the old `intake` schema by hand (one-time operator
step — `Comuki.Migrator` never auto-migrates, so this is not app logic,
just a runbook entry in `tasks.md`).

**Rejected:** live `ALTER SCHEMA intake RENAME TO integrations` plus a
new carried migration step appended to the existing 4-migration history.

**Rationale** (binding quotes from architecture.md):

- "`Integrations` is a hard pre-release replacement for `Intake`:
  projects, namespaces, schema, migration baseline, generated clients,
  and routes are renamed in one dedicated change. Development/staging
  data is reset; no runtime compatibility aliases or dual write are
  carried."
- Task 3.0 itself says "regenerate **a clean migration baseline**" —
  singular, clean, not an incremental rename step appended to the
  existing 4-migration history.
- There is no production data to preserve (pre-release); the existing 4
  Intake migrations would otherwise carry forward confusing old-named
  steps under a new module.

**Concrete file-level moves:**

- Delete `platform/src/modules/Intake/Comuki.Modules.Intake.Infrastructure/Migrations/*`
  (all 4 migrations + `IntakeDbContextModelSnapshot.cs`) as part of
  the Infrastructure-layer rename.
- The renamed `Comuki.Modules.Integrations.Infrastructure` project
  generates ONE new `dotnet ef migrations add InitialIntegrationsSchema`
  migration built from the renamed entity configurations, producing
  schema `integrations` and history table `__comuki_integrations` from
  scratch.
- Rename the `Comuki.Migrator` design-time factory at
  `platform/src/host/Comuki.Migrator/Factories/Intake/IntakeDesignTimeFactory.cs`
  to `Factories/Integrations/IntegrationsDesignTimeFactory.cs`.
- Update the ordered context registry entry in
  `platform/src/shared/Comuki.Shared.Migrations/Targets/MigrationTargets.cs`
  (currently `new("intake", IntakeDatabase.Schema, ...)`) to register
  `"integrations"` against `IntegrationsDatabase.Schema`.
- Dev/staging cutover: drop the old `intake` schema (and its
  `__comuki_intake` history table) by hand once the new baseline is
  confirmed. Documented as a runbook line in `tasks.md`, not app logic.
- `deploy/k8s/postgres.yaml`, `deploy/helm/templates/postgres.yaml`,
  `deploy/compose/init/01-schemas.sql` currently each have exactly one
  line `CREATE SCHEMA IF NOT EXISTS intake;` — rename to `integrations`
  in all three (net-new environments only ever see the new name;
  nothing there references the old schema afterward).

### 3. Scope discipline — what stays `Intake`-free

The epic does not name the following as `Intake`-branded identifiers, so
this change deliberately does **not** rename them — keeping blast radius
to the minimum that task 3.0's literal mandate requires:

- `SourceConnection`, `AdmissionRule`, `AdmissionMode`, `SyncJob`,
  `SyncJobStatus` — these are provider integration details, not
  `Intake`-branded identifiers.
- Provider names (`github`, `gitlab`, `jira`, `yandex-tracker`) and
  provider-specific classes (`GitHubTicketSync`, `JiraPayloadMapper`,
  …).
- Permission keys `source:write`, `run:create` (not Intake-namespaced).
- The dashboard's `domains/inbox` and `domains/sources` folder layout
  (consolidating them into one `domains/integrations/` folder is
  `add-mission-dashboard` #104's job, not this change's).

### 4. Touch-point inventory

Counts (case-insensitive `intake` match unless noted) over the branch
tip at `gitlab/feature/mission-cowork-index`:

- `platform/src` — 157 files. Categories:
  - The whole `Intake` module tree
    (`Comuki.Modules.Intake.{Domain,Application,Infrastructure}`,
    ~140 files — namespace + directory rename).
  - `platform/src/host/Comuki.Host/Intake/**` (controllers + request
    models, ~13 files — namespace + route rename).
  - `platform/src/host/Comuki.Host/HostComposer.cs` (DI registration
    block, lines ~244-265 — `AddIntakeApplication()` /
    `AddIntakePersistence()` / `AddIntakeProviders()` calls +
    `IntakeOptions` / `IntakeWorkerDefaults` binding +
    `IIntakeProfileRouter` / `IntakeProfileRouter` DI).
  - `platform/src/host/Comuki.Migrator/Factories/Intake/IntakeDesignTimeFactory.cs`
  - `platform/src/shared/Comuki.Shared.Migrations/Targets/MigrationTargets.cs`
    (registry entry).
  - `platform/src/modules/Identity/.../Permissions.cs`
    (`IntakeRead` / `IntakeClaim` permission key constants).
  - `platform/src/modules/Identity/.../RoleMatrix.cs` (role→permission
    grants referencing those keys).
  - `platform/src/shared/Comuki.Shared.Contracts/Runs/RunStatuses.cs`
    (one doc-comment mention only — cosmetic).
  - Two files
    (`platform/src/modules/Projects/**/DomainTypeAdmission.cs`,
    `.../DomainTypeAdmissionService.cs`) are **false positives** — they
    match the generic English word "admission"/doc-comment "intake"
    concept, not the Intake module; leave them alone, verify with a
    targeted read before touching anything there.
- `tests` — 58 files. `tests/unit/Comuki.Modules.Intake.Unit/**` (whole
  project, rename dir + csproj + namespace),
  `tests/integration/Comuki.Host.Integration.Intake/**` (whole project),
  `tests/integration/Comuki.Modules.Intake.Integration.Migrations/**`
  (whole project — this one especially needs the squashed-migration
  rewrite, not just a rename), `tests/Comuki.Architecture.Tests/{IntakeModuleLayerTests.cs,
  ScopeGuardTests.cs, SharedContractsModuleBoundaryTests.cs}` (layer-
  boundary assertions naming `Comuki.Modules.Intake.*` — rename to
  `Comuki.Modules.Integrations.*`), plus incidental references in
  `Comuki.EndToEnd.AgentLoop` and `Comuki.AgentTest.Runner`
  fixtures/scenario YAML that construct native tickets against the old
  routes.
- `dashboard/src` — 81 files. Generated contracts under
  `dashboard/src/shared/api/_generated/**` (types/schemas named
  `IntakeTicketView`, `PostApiV1Tickets`, `PostApiV1InboxClaim`,
  `GetApiV1Inbox`, `GetApiV1InboxCatalog`, etc. — regenerate via
  `dashboard`'s `generate-api` script, do not hand-edit) plus
  hand-written consumers in `dashboard/src/domains/{inbox,sources}/**`
  (api queries/mutations/mappers that reference the generated type/route
  names) and `dashboard/src/app/layout/{nav.ts,nav-sections.ts}` (a
  cosmetic nav section `id: "intake"` / `label: "Intake"` grouping
  Inbox/Sources/Tasks — optional rename to `"integrations"` /
  `"Integrations"`, low risk, does not block the gate).
- `cli/src` — 6 files, all generated contracts under
  `cli/src/contracts/_generated/**` (regenerate via cli's
  `generate:contracts` script; architecture.md notes the CLI itself is
  "explicitly not restructured" here — no hand-written cli command code
  references Intake types directly).
- `control-plane` — 0 files, no touch points.
- `deploy` (open-source paths) — `deploy/k8s/postgres.yaml`,
  `deploy/helm/templates/postgres.yaml`,
  `deploy/compose/init/01-schemas.sql` (schema bootstrap SQL, one line
  each), `deploy/config.example.toml` (`[intake]`/`[intake.worker]`
  sections → `[integrations]`/`[integrations.worker]`),
  `deploy/compose/docker-compose.yml` and `deploy/compose.e2e.yml`
  (comments only, cosmetic).

#### `deploy/hybrid` overlay

List separately per task 3.0's instruction — this is the overlay
directory, distinct from the open-source `deploy/` bullets above:

- Exactly one incidental match: `deploy/hybrid/migrate-job-dev.yaml`
  contains the word "intake" only in a comment/label with no schema or
  route coupling; confirm at implementation time whether it needs
  updating for consistency, but it is not a functional dependency.

### 5. Contracts regeneration — regenerate, don't hand-edit

Generated TypeScript contracts must be regenerated from the live OpenAPI
document, not hand-edited, because they ship type/schema names that have
to match the renamed C# routes/DTOs exactly. Commands:

- **dashboard** (defined in `dashboard/package.json`):
  `bun run generate-api` (= `dotnet build ../comuki.slnx && bunx @kubb/cli@4.39.2 generate && prettier --write "src/shared/api/_generated/**/*.ts"`).
- **CLI** (defined in `cli/package.json`):
  `bun run generate:contracts` (= `dotnet build ../comuki.slnx -c Debug && bun scripts/normalize-openapi.ts && bunx @kubb/cli@4.39.2 generate && dotnet run --project ../tools/Comuki.Codegen.Realtime ...`).

Both regenerate from the live OpenAPI document, so they must run
**after** the C# route/DTO rename compiles, and their diff must show
zero remaining `Intake`/`intake:` identifiers.

## Risks / Trade-offs

- **`[Risk]` Breaking change with no compatibility layer.**
  `Mitigation:` pre-release, no external consumers yet, single
  coordinated deploy.
- **`[Risk]` Squashed migration loses granular history.**
  `Mitigation:` git history retains the original 4 migrations;
  pre-release data has no continuity requirement.
- **`[Risk]` Generated-contract drift if regeneration is skipped.**
  `Mitigation:` tasks.md makes contract regeneration its own verifiable
  task with a grep-for-`Intake` gate.
- **`[Risk]` Stray `intake` label in `deploy/hybrid/migrate-job-dev.yaml`
  becomes inconsistent after the open-source rename.**
  `Mitigation:` confirm at implementation time and rename for
  consistency; this is not a functional dependency.
- **`[Risk]` False-positive hits in
  `platform/src/modules/Projects/**/DomainTypeAdmission.cs` and
  `.../DomainTypeAdmissionService.cs` (generic English "intake" /
  "admission" matches).**
  `Mitigation:` those files do not reference the Intake module —
  targeted read before touching confirms they stay as-is.

## Migration Plan

Ordered restated summary (mirrors tasks.md workstream order):

1. **Domain layer rename** — `Comuki.Modules.Intake.Domain` →
   `Comuki.Modules.Integrations.Domain` (directory + csproj + namespace
   + assembly name), rename `IncomingTicket`→`InboundItem`,
   `IncomingTicketId`→`InboundItemId`,
   `IntakeTicketStatus`→`InboundItemStatus`,
   `InboundTicketKind`→`InboundItemKind`; leave `SourceConnection`,
   `AdmissionRule`, `AdmissionMode`, `SyncJob`, `SyncJobStatus`,
   `IntakeDelivery`→rename to `Delivery`, `DeliveryOutcomes`,
   `TicketProvider` class names as-is except namespace.
2. **Application layer rename** — `Comuki.Modules.Intake.Application` →
   `Comuki.Modules.Integrations.Application`; rename `IntakeOptions`→
   `IntegrationsOptions` (`SectionName` `"Intake"`→`"Integrations"`),
   `IIntakeStore`→`IIntegrationsStore`,
   `IIntakeProfileRouter`→`IIntegrationProfileRouter`,
   `IntakeTicketView`→`InboundItemView`,
   `IntakeTicketExceptions`→`InboundItemExceptions`,
   `IntakeSourceExceptions`→`IntegrationsSourceExceptions`,
   `IntakeApplicationExtensions`→`IntegrationsApplicationExtensions`;
   rename all `intake.<code>` problem-detail code string literals to
   `integration.<code>` per the naming table (grep for `"intake.` after
   this workstream — must be zero hits).
3. **Infrastructure layer rename + migration squash** —
   `Comuki.Modules.Intake.Infrastructure` →
   `Comuki.Modules.Integrations.Infrastructure`; rename
   `IntakeDbContext`→`IntegrationsDbContext`,
   `IntakeDatabase`→`IntegrationsDatabase` (schema constant
   `"intake"`→`"integrations"`), `IntakeStore`→`IntegrationsStore`,
   table names `intake_tickets`→`inbound_items`,
   `intake_deliveries`→`deliveries` in the EF configurations; delete
   the 4 existing migrations + `IntakeDbContextModelSnapshot.cs`,
   generate one fresh `InitialIntegrationsSchema` migration (history
   table `__comuki_integrations`); rename
   `IntakeProvidersExtensions`→`IntegrationsProvidersExtensions`,
   `IntakePersistenceExtensions`→`IntegrationsPersistenceExtensions`;
   provider subfolders (GitHub/GitLab/Jira/YandexTracker) get
   namespace-only updates, class names unchanged.
4. **Host composition, routes, permissions, config, deploy schema
   bootstrap** — rename `platform/src/host/Comuki.Host/Intake/**`
   (controllers + request models) directory to `Integration/`, update
   route attributes per the route table, update `HostComposer.cs`'s
   `AddIntakeApplication()` / `AddIntakePersistence()` /
   `AddIntakeProviders()` calls and `IntakeOptions` /
   `IntakeWorkerDefaults` binding to the renamed types/section; rename
   `Comuki.Migrator/Factories/Intake/IntakeDesignTimeFactory.cs` →
   `Factories/Integrations/IntegrationsDesignTimeFactory.cs`; update
   `Comuki.Shared.Migrations/Targets/MigrationTargets.cs`'s registry
   entry from `"intake"` / `IntakeDatabase.Schema` to
   `"integrations"` / `IntegrationsDatabase.Schema`; rename
   `Permissions.IntakeRead` / `IntakeClaim` → `IntegrationRead` /
   `IntegrationClaim` (`"intake:read"`→`"integration:read"`,
   `"intake:claim"`→`"integration:claim"`) and update `RoleMatrix.cs`
   grants; rename `deploy/k8s/postgres.yaml`,
   `deploy/helm/templates/postgres.yaml`,
   `deploy/compose/init/01-schemas.sql`'s
   `CREATE SCHEMA IF NOT EXISTS intake;` to `integrations`; rename
   `deploy/config.example.toml`'s `[intake]` / `[intake.worker]`
   sections to `[integrations]` / `[integrations.worker]`. Confirm (do
   not action, just confirm): check `deploy/hybrid/migrate-job-dev.yaml`
   for a stray `intake` label and rename it too if present, for
   consistency with the open-source overlay.
5. **Test rename** — move/rename
   `tests/unit/Comuki.Modules.Intake.Unit` →
   `tests/unit/Comuki.Modules.Integrations.Unit`,
   `tests/integration/Comuki.Host.Integration.Intake` →
   `Comuki.Host.Integration.Integrations`,
   `tests/integration/Comuki.Modules.Intake.Integration.Migrations` →
   `Comuki.Modules.Integrations.Integration.Migrations` (rewrite its
   assertions for the new single-migration baseline, not the old
   4-step history); rename
   `tests/Comuki.Architecture.Tests/IntakeModuleLayerTests.cs` →
   `IntegrationsModuleLayerTests.cs` and update the namespace
   constants inside it, plus the `Comuki.Modules.Intake.*` references
   in `ScopeGuardTests.cs` and `SharedContractsModuleBoundaryTests.cs`;
   update incidental references in `Comuki.EndToEnd.AgentLoop`
   fixtures and `Comuki.AgentTest.Runner` scenario YAML that hit the
   old routes.
6. **Dashboard + CLI contract regeneration** — after workstream 4
   compiles, run `dashboard`'s `bun run generate-api` and `cli`'s
   `bun run generate:contracts`; update hand-written consumers in
   `dashboard/src/domains/{inbox,sources}/**` that reference the
   renamed generated type/route names (`IntakeTicketView`→
   `InboundItemView`, etc.); optionally rename the dashboard nav's
   cosmetic `id: "intake"` / `label: "Intake"` in
   `dashboard/src/app/layout/{nav.ts,nav-sections.ts}` to
   `"integrations"` / `"Integrations"` (low-risk, does not block the
   gate — mark this specific sub-task optional). Do NOT restructure
   `domains/inbox` / `domains/sources` into a merged
   `domains/integrations` folder (out of scope, belongs to #104).

There are no Open Questions — every naming call above is a decision,
not a question.
## Decision: dev/stage data reset (user, 2026-09-25)

Confirmed by the user: dev (GitLab hybrid overlay → ArgoCD dev) and stage data may be reset — **all of it, not only the `intake` schema**. The rollout therefore uses the fresh squashed `InitialIntegrationsSchema` baseline with no data migration. The implementation MR must include an explicit, documented reset step for the dev environment (drop/recreate the database or the affected schemas before `migrate:dev`), executed in the same coordinated deploy.

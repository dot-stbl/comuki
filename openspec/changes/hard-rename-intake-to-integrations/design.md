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
| `POST /api/v1/sources/{id}/rotate-secret` | `POST /api/v1/integration/sources/{id}/rotate-secret` |
| `PUT /api/v1/sources/{sourceId}/rules/{ruleId}` | `PUT /api/v1/integration/sources/{sourceId}/rules/{ruleId}` |
| `/api/v1/admission-rules` (+ `/{ruleId}`) | `/api/v1/integration/admission-rules` (+ `/{ruleId}`) |
| `POST /api/hooks/{provider}/{key}` | **unchanged** (already anonymous, routing-key-addressed) |

The `SourceRotateSecret` route (issue #46) was added to `ApiRoutes` after
this change was first drafted; the rename table above includes it for
completeness — without the entry, the rotate-secret endpoint would land
as `/api/v1/integration/sources/{id}/rotate-secret` only by a separate
patch. Same controller action, same response shape (`SecretRotationResponse`),
same `source:write` permission; identifier rename only.

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
tip at `gitlab/feature/mission-cowork-index` (verified against the
`e57f0bf4` master tip that this change is rebased onto — see Drift
section below for the small deltas since the original draft):

- `platform/src` — 157 files. Categories:
  - The whole `Intake` module tree
    (`Comuki.Modules.Intake.{Domain,Application,Infrastructure}`,
    ~140 files — namespace + directory rename).
  - `platform/src/host/Comuki.Host/Intake/**` (controllers + request
    models, ~13 files — namespace + route rename).
  - `platform/src/host/Comuki.Host/HostComposer.cs` (DI registration
    block, lines **289-304** — `AddIntakeApplication()` /
    `AddIntakePersistence()` / `AddIntakeProviders()` calls +
    `IntakeOptions` / `IntakeWorkerDefaults` binding +
    `IIntakeProfileRouter` / `IntakeProfileRouter` DI). The line
    numbers in the first draft of this change (244-265) drifted
    because of the post-`#88` Procedures module composition
    (`AddProceduresPersistence`, `AddProceduresApplication`, etc.)
    that now sits between line 240 and the Intake block.
  - `platform/src/host/Comuki.Host/HostComposer.cs` line **506** —
    `builder.Services.AddIntakeProblemHandlers();` (the typed
    problem-handler registry for the five `intake.*` error codes).
    The matching source is at
    `platform/src/host/Comuki.Host/Errors/Handlers/Intake/{IntakeProblemHandlerRegistration,IntakeProblemHandlers}.cs`
    (not under `Host/Intake/`). The directory rename target is
    `Errors/Handlers/Integrations/`.
  - `platform/src/host/Comuki.Migrator/Factories/Intake/IntakeDesignTimeFactory.cs`
  - `platform/src/shared/Comuki.Shared.Migrations/Targets/MigrationTargets.cs`
    (registry entry) and
    `platform/src/shared/Comuki.Shared.Migrations/DatabaseSchemaEnsurer.cs`
    (the `IntakeDatabase.Schema => CreateIntakeSchemaDdl` switch arm
    and the `CREATE SCHEMA IF NOT EXISTS intake` literal — both
    rename to `integrations` / `CREATE SCHEMA IF NOT EXISTS integrations`).
  - `platform/src/modules/Identity/.../Permissions.cs`
    (`IntakeRead` / `IntakeClaim` permission key constants).
  - `platform/src/modules/Identity/.../RoleMatrix.cs` (role→permission
    grants referencing those keys, lines 24, 44, 62, 93, 109).
  - `platform/src/shared/Comuki.Shared.Contracts/Runs/RunStatuses.cs`
    (one doc-comment mention only — cosmetic, in
    `/// representation modules that must not reference the engine (Intake's`).
  - `platform/src/host/Comuki.Host/Runs/SteeringWorkerDefaults.cs`
    and `Scheduler/SchedulerWorkerDefaults.cs` carry a doc-comment
    mention of `IntakeWorkerDefaults` as a sibling class — cosmetic
    only.
  - `platform/src/host/Comuki.Host/Intake/IntakeProfileRouter` /
    `IIntakeProfileRouter` is split across two locations: the
    **interface** `IIntakeProfileRouter` lives at
    `platform/src/modules/Intake/Comuki.Modules.Intake.Application/Ports/Admission/IIntakeProfileRouter.cs`,
    the **concrete** `IntakeProfileRouter` lives at
    `platform/src/modules/Intake/Comuki.Modules.Intake.Infrastructure/Admission/IntakeProfileRouter.cs`
    and exposes a `string IssueDefaultProfileKey` constructor argument
    bound by the host from `IOptions<IntakeWorkerDefaults>.Value.IssueDefaultProfileKey`.
    Both files rename to the `Integrations` namespace; the
    interface's host binding line in `HostComposer.cs` (302-304) also
    rewrites the fully-qualified type names. (The first draft
    summarised the pair as one file — they are two.)
  - Two files
    (`platform/src/modules/Projects/**/DomainTypeAdmission.cs`,
    `.../DomainTypeAdmissionService.cs`) are **false positives** — they
    match the generic English word "admission"/doc-comment "intake"
    concept, not the Intake module; leave them alone, verify with a
    targeted read before touching anything there.
- `tests` — 58 files. `tests/unit/Comuki.Modules.Intake.Unit/**` (whole
  project, rename dir + csproj + namespace),
  `tests/integration/Comuki.Host.Integration.Intake/**` (whole project,
  with one extra unit test outside the Intake project tree:
  `tests/unit/Comuki.Host.Unit.Errors/IntakeProblemHandlersShould.cs` —
  this one specifically exercises the new `Host/Errors/Handlers/Intake/`
  pair and must be renamed to `IntegrationsProblemHandlersShould.cs` and
  moved under the same `Host.Unit.Errors` folder),
  `tests/integration/Comuki.Modules.Intake.Integration.Migrations/**`
  (whole project — this one especially needs the squashed-migration
  rewrite, not just a rename; the existing
  `CreateIntakeTablesAlongsideOrchestrationAsync` test asserts the
  current 4-migration baseline and must be rewritten for the new
  one-migration baseline), `tests/Comuki.Architecture.Tests/{IntakeModuleLayerTests.cs,
  ScopeGuardTests.cs, ProjectsModuleLayerTests.cs,
  SharedContractsModuleBoundaryTests.cs}` (layer-boundary assertions
  naming `Comuki.Modules.Intake.*` — rename to
  `Comuki.Modules.Integrations.*`; the `ProjectsModuleLayerTests.cs`
  also declares `SiblingModules = "Comuki.Modules.Intake"` to assert
  Projects has no Intake reference, so that constant rewrites too),
  plus incidental references in `Comuki.EndToEnd.AgentLoop`
  (`AgentLoopHost.cs` lines 273/287, `RealPi/RealPiFakeModelHost.cs`
  lines 238/252, `CrownScenarioHost.cs` lines 179/193) and
  `Comuki.AgentTest.Runner` fixtures/scenario YAML that construct
  native tickets against the old routes.
- `dashboard/src` — 81 files. Generated contracts under
  `dashboard/src/shared/api/_generated/**` (types/schemas named
  `IntakeTicketView`, `PostApiV1Tickets`, `PostApiV1InboxClaim`,
  `GetApiV1Inbox`, `GetApiV1InboxCatalog`, plus
  `PostApiV1SourcesSourceidRotateSecret` etc. — regenerate via
  `dashboard`'s `generate-api` script, do not hand-edit) plus
  hand-written consumers in `dashboard/src/domains/{inbox,sources}/**`
  (`api/queries.ts`, `api/mutations.ts`, `api/mappers.ts`,
  `api/mappers.test.ts`, `model/types.ts`, `index.ts`,
  `tasks/{ui/tasks-badges.tsx, ui/tasks-badges.test.tsx,
  ui/tasks-badges.stories.tsx, ui/tasks-columns.tsx, ui/tasks-gate.test.tsx,
  pages/tasks-page.tsx, pages/tasks-page.test.tsx,
  pages/create-task-page.tsx, model/types.ts, api/queries.ts,
  api/mappers.ts}`) that reference the generated type/route names,
  the dashboard's `domains/tasks/AGENTS.md`, and
  `dashboard/src/app/layout/{nav.ts,nav-sections.ts,nav-active-section.test.ts,nav.test.tsx,nav-sections.test.tsx,app-shell.test.tsx,app-shell-two-pane-sidebar.test.tsx}`
  (a nav section `id: "intake"` / `label: "Intake"` / `labelKey: "nav.intake"`
  grouping Inbox/Sources/Tasks — rename to `"integrations"` /
  `"Integrations"` / `"nav.integrations"`; the i18n key
  `"nav.intake": "Intake"/"Приём"` in
  `dashboard/src/shared/i18n/locales/{en,de,es,fr,it,ja,ko,pl,pt-BR,ru,tr,zh-CN,zh-TW}/shell.json`
  renames in lockstep; otherwise the FE will ship with a missing
  translation in some locales).
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
  (Note: this branch is the open-source mirror; the `deploy/hybrid/`
  tree itself lives in the GitLab side and is not part of the
  `e57f0bf4` source-of-truth tree — out of scope for this PR's diff,
  but verify before merging the deploy roll-out so the GitLab overlay
  picks up the rename at the same time.)

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
- **`[Risk]` Identity specs read by `add-configurable-roles-and-platform-config`
  (#95) and `add-minimal-missions` (#93) touch the same `intake:read` /
  `intake:claim` permission constants this change renames.**
  `Mitigation:` both #93 and #95 declare dependency on #88; their
  permission catalog (per decomposition.md row) is `MODIFY` of
  `identity` only, not `integrations`. The post-rename `Permissions.IntegrationRead` /
  `IntegrationClaim` keys and `integration:read` / `integration:claim`
  strings are the contract they read; no collision.
- **`[Risk]` `add-domain-user-intake` change (sibling, #163-wave) declares
  a non-goal "Real-domain-type classification in intake (chat/tracker) —
  slice 2." A future slice 2 of that change will sit on top of
  `IIntakeProfileRouter` / `IIntegrationProfileRouter`.**
  `Mitigation:` confirmed in the sibling change's proposal.md
  (`openspec/changes/add-domain-user-intake/proposal.md`) and
  decomposition.md — slice 1 is purely additive to `ProjectSettings`
  and touches no Intake/Integrations file; slice 2 will be authored
  against the post-`#88` `Integrations` namespace. Owner decision:
  `#88` lands first, then `add-domain-user-intake` rebases. The
  sibling change's `non-goals` is preserved by this change.

## Drift since first draft (vs `e57f0bf4` master tip)

The change was first drafted against an older intake of the Intake
module. The following master-tip changes have landed without touching
the Intake shape this change targets — verified by file-level read,
no Intake refactor implied:

- **Procedures module** (post-`#104` wave, in `Comuki.Modules.Procedures.*`):
  the new `AddProceduresApplication` / `AddProceduresPersistence`
  composition is what shifted the Intake DI block from `HostComposer.cs`
  lines ~244-265 to the actual 289-304. Mentioned in §4 above.
- **Memory module** (`#177`/`#181`, `Comuki.Modules.Memory.*`):
  the digest/sweep wiring sits above the Intake block in
  `HostComposer.cs`. Zero references to Intake beyond the
  `intake` keyword in the
  `Intake:Worker:BridgeInterval` config comment that is itself
  in the Intake-owned `IntakeOptions` class. No change to rename.
- **Observability module** (`Comuki.Modules.Observability.*`):
  zero Intake references — different bounded context.
- **Execution spine** (`#87`): the work this change builds on
  (`add-execution-spine-orchestration`) is **explicitly** a
  dependency of `#88` per decomposition.md, but it does not add
  Intake references — it adds `WorkItem` / `Generation` /
  outbox-fencing in the orchestration engine. Verified.
- **`add-work-management` (#89, sibling change, in
  `openspec/changes/add-work-management/`):** this is the change
  that lands *after* `#88` and consumes the post-rename target
  shape. It refers to `integration.inbound.admitted.v1`,
  `IIntegrationProfileRouter`, and the `Integrations` namespace —
  all post-rename identifiers. **The `#89` workstream uses these
  as the contract; it does NOT change them.** Pre-`#88` reading
  of `#89` would show a `Intake`-prefixed citation that
  `add-work-management` will rebase onto the post-`#88` shape.
  This change does not need to pre-empt that rebase — it is
  `#89`'s job.
- **`add-domain-user-intake` (sibling, in
  `openspec/changes/add-domain-user-intake/`):** its slice 1 is
  `ProjectSettings` / `ProjectDomainTypeResolver`-only and touches
  the Projects module; its Non-goals explicitly defer "Real-domain-
  type classification in intake (chat/tracker) — slice 2." See
  Risks / Trade-offs § above for the rebase note.

The drift is **all mechanical** (HostComposer line numbers, an extra
`SourceRotateSecret` route, the `Errors/Handlers/Intake/` directory
that landed with the typed problem-handler registry, the
`IntakeProblemHandlersShould` unit test that landed under
`tests/unit/Comuki.Host.Unit.Errors/`, and the i18n nav key surface
in the dashboard that needs lockstep rename). **Zero of it is a
behavioural change in Intake** — the design decisions, the naming
table, the route renames, the migration-squash strategy, and the
non-goals are all still valid.

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

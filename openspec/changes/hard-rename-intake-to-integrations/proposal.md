## Why

`add-mission-cowork` (issue #70) needs a clean `work-management` foundation
(#89) that creates standalone Tasks on admission instead of Runs. Before
that behavioral change can land, the `Intake` bounded context — module
namespaces, `intake` PostgreSQL schema, `IncomingTicket` entity, public
routes, permission keys, and generated client contracts — must become
`Integrations`. This is a dedicated, mechanical, pre-release hard rename
(epic task 3.0, issue #88): no behavior changes, no compatibility aliases,
no dual-write. It exists only so #89 and every later Mission/Work change
can build on `Comuki.Modules.Integrations.*` from day one instead of
`Intake` debt.

## What Changes

- Rename `Comuki.Modules.Intake.{Domain,Application,Infrastructure}` to
  `Comuki.Modules.Integrations.{Domain,Application,Infrastructure}`.
- Rename the `intake` PostgreSQL schema to `integrations`
  (`IntakeDbContext` → `IntegrationsDbContext`, migrations history
  `__comuki_intake` → `__comuki_integrations`), and rename tables
  `intake_tickets` → `inbound_items`, `intake_deliveries` → `deliveries`.
- Rename `IncomingTicket` → `InboundItem` (and its id, status, kind, view,
  and store types) across Domain/Application/Infrastructure/Host.
- Regenerate a clean, squashed EF Core migration baseline for
  `Comuki.Modules.Integrations.Infrastructure` — no incremental rename
  migration, no data-preserving `ALTER SCHEMA`; development/staging
  databases are reset.
- Renest public HTTP routes under `/api/v1/integration/*` (`/api/v1/inbox`,
  `/api/v1/tickets`, `/api/v1/sources`, `/api/v1/admission-rules` and their
  sub-paths); provider webhook ingress stays `/api/hooks/{provider}/{key}`.
- Rename permission keys `intake:read`/`intake:claim` →
  `integration:read`/`integration:claim`; rename `intake.*` problem-detail
  error codes to `integration.*`; rename the `Intake` config section
  (`Intake:*` / `[intake]` TOML) to `Integrations`.
- Regenerate OpenAPI-derived TypeScript contracts (dashboard Kubb client,
  CLI generated client) against the renamed routes/DTOs; verify no stale
  `Intake*` generated files remain.
- Rename the `Comuki.Architecture.Tests` layer/boundary tests and
  `Comuki.Modules.Intake.*` test projects to `Comuki.Modules.Integrations.*`.
- **BREAKING:** no compatibility aliases for the old namespace, schema,
  routes, permission keys, or generated clients. Any client still calling
  `/api/v1/inbox` or reading `intake:*` permissions stops working the
  moment this change deploys.

## Capabilities

### New Capabilities

- `integrations`: connections, providers, inbound items, deliveries,
  admission rules, inbox/claim, native inbound item creation, and
  sync-back — the full behavior of the retired `intake` capability,
  renamed only.

### Modified Capabilities

(none behavior-changing outside the rename itself — `identity`'s
permission catalog and `host`'s composition wiring gain renamed literals
but no new authorization semantics, so no delta spec is filed against
those capabilities)

## Impact

Touches `platform/src/modules/Intake` (~140 files, moved/renamed),
`platform/src/host/Comuki.Host/Intake` + `HostComposer.cs`,
`platform/src/host/Comuki.Migrator` (design-time factory + migration
target registry), `platform/src/shared/Comuki.Shared.Migrations`,
`platform/src/modules/Identity` (permission keys), the
`Comuki.Modules.Intake.*` test projects and `Comuki.Architecture.Tests`,
dashboard generated contracts plus `domains/{inbox,sources}` consumers,
CLI generated contracts, and `deploy/{k8s,helm,compose}` schema bootstrap
plus `deploy/config.example.toml`. `deploy/hybrid/` has one incidental
`intake` string match (a comment/label) with no schema or route coupling.

## Non-goals

- Introducing Work/Task creation on admission — that is `add-work-management`
  (#89), which lands after this change and will MODIFY the `integrations`
  capability's inbox/claim, native-item, and sync-back requirements.
- Outbound Project webhook subscriptions — that is `add-outbound-webhooks`
  (#103), a separate greenfield capability.
- Any runtime compatibility layer, dual-write, or data migration for
  existing `intake` rows — architecture.md already decided a pre-release
  reset; see design.md's Migration Plan.
- Any change to provider mapping logic, signature verification, or
  sync-back comment behavior beyond identifier renames.
- Restructuring the dashboard's domain folders (`domains/inbox`,
  `domains/sources`) into a consolidated `domains/integrations/` — that is
  `add-mission-dashboard` (#104)'s job.
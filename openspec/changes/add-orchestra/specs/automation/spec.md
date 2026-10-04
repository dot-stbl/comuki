# Automation Specification

## Purpose

Defines the first-class `Automation` read-model object that joins the three existing trigger surfaces (`ScheduledJob`, `SourceConnection`/webhook, `RunStatusBridge` + `SyncJob`), the typed `AutomationAction` discriminator that replaces the three hard-coded action verbs, the action library, the per-automation run history, and the optional outbox leg for exactly-once-observable side effects.

Today the three trigger surfaces are independent — there is no unifying object that lets an operator list, audit, or migrate automations across the platform. This capability is a *read-model*: the schema is not forked, the three sources stay authoritative, and the `Automation` view joins them.

## ADDED Requirements

### Requirement: Automation is a typed read-model object

`Automation` is a record under `Comuki.Modules.Automation` (Domain / Application / Infrastructure). The record carries:

```csharp
public sealed record Automation(
    Guid Id,
    Guid ProjectId,
    Trigger Trigger,                    // discriminated union
    AutomationAction Action,            // typed record
    bool Enabled,
    AuditList History,                  // joined from existing audit rows
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public abstract record Trigger(AutomationTriggerKind Kind);
public sealed record ScheduledTrigger(string CronExpression, TimeZoneInfo TimeZone) : Trigger(AutomationTriggerKind.Scheduled);
public sealed record WebhookTrigger(Guid SourceConnectionId, string EventKind) : Trigger(AutomationTriggerKind.Webhook);
public sealed record RunTerminalTrigger(Guid ProjectId, RunStatus Status) : Trigger(AutomationTriggerKind.RunTerminal);
```

The model is read-only; the schema is not forked. The three source tables stay authoritative.

#### Scenario: List automations for a project

- **WHEN** an operator with `automation:read` GETs `/api/v1/projects/{projectId}/automations`
- **THEN** the response joins the three sources for the project and returns the typed `Automation[]`

#### Scenario: Trigger provider registry is deferred

- **WHEN** a non-cron, non-webhook trigger is needed
- **THEN** the trigger-provider registry is created at that moment; until then, the union is closed (`Scheduled` / `Webhook` / `RunTerminal`)

### Requirement: AutomationAction is a typed record

`AutomationAction` replaces the three hard-coded action verbs (`launch run` / `park` / `sync-back`) across the existing surfaces:

```csharp
public abstract record AutomationAction(AutomationActionKind Kind);
public sealed record LaunchRunAction(LaunchRunSpec Spec) : AutomationAction(AutomationActionKind.LaunchRun);
public sealed record ParkAction(ParkSpec Spec) : AutomationAction(AutomationActionKind.Park);
public sealed record SyncBackAction(SyncBackSpec Spec) : AutomationAction(AutomationActionKind.SyncBack);
```

The action is registered through `AddAutomationAction<T>(AutomationActionKind kind)` in the host composition root. New action kinds ship as additional classes; the existing hard-coded verbs migrate to the typed wrapper without changing semantics.

#### Scenario: Existing actions ship as typed records

- **WHEN** the host composes with the three hard-coded actions migrated
- **THEN** the existing tests for `launch run` / `park` / `sync-back` still pass and the action kinds surface in the `Automation` view

#### Scenario: New action registers as a class

- **WHEN** an operator authors a class `MyEscalationAction : AutomationAction` and registers through `AddAutomationAction<MyEscalationAction>(kind)`
- **THEN** the new action appears in the registry and the `Automation` view exposes it under the registered kind

### Requirement: Outbox leg is opt-in per automation

`Outbox` adoption is per-automation. When an automation side-effect needs exactly-once-observable delivery (e.g. `SyncBack` after a Run terminal), the existing `OutboxMessage` table is used with the existing `NoopOutboxPublisher` (or a future transport adapter); otherwise the read-model + idempotency-key is enough.

#### Scenario: SyncBack uses the outbox

- **WHEN** an automation with `SyncBackAction` fires on a Run reaching `Succeeded`
- **THEN** a row is committed in `outbox_messages` with the existing `SyncBack` payload and the existing `NoopOutboxPublisher` is the publisher

#### Scenario: Park does not use the outbox

- **WHEN** an automation with `ParkAction` fires on a webhook admit
- **THEN** the side-effect is journaled under the existing audit row and no `outbox_messages` row is created

### Requirement: Run history reads the existing audit rows

`Automation.History` is an `AuditList` joined from the existing `IntakeDelivery` rows (webhook admit), the scheduler's per-job `LastFiredAt` / `NextFireAt` (cron fire — the v1.x scheduler records the fire trail on the `jobs` row itself, not in a stand-alone `firings` table; see the `add-scheduled-jobs` change for the future `firings` history table), and `sync_jobs` rows (run-terminal sync-back). The audit row format stays owned by its source; the read adapter joins them under the `Automation` projection.

> **Coordination note (2026-10-04).** The v1.x scheduler records the cron fire trail on the `jobs` row itself (`LastFiredAt` / `NextFireAt` on `SchedulerDbContext`). A standalone `firings` history table is the open follow-up from the unarchived `add-scheduled-jobs` change. The Encore read adapter reads from the v1.x surface today; when `add-scheduled-jobs` lands and the `firings` table exists, the adapter is updated to join on it. The change does not block on `add-scheduled-jobs`.

#### Scenario: History shows the last ten fires

- **WHEN** an automation has fired ten times
- **THEN** the `History` view shows the ten rows in reverse-chronological order, joined from the three sources

#### Scenario: Skip rows have null run_id

- **WHEN** an automation is fired and skipped (cron overlap, webhook duplicate)
- **THEN** the audit row carries `run_id = null` and the `History` view marks the entry as `outcome: skipped`

### Requirement: REST surface under the existing projects namespace

`GET /api/v1/projects/{projectId}/automations` SHALL require `automation:read`; create / patch / archive siblings SHALL require `automation:write`. The endpoints reuse the existing `Projects` route group; no new top-level route.

#### Scenario: List

- **WHEN** an authorised caller GETs the list
- **THEN** the response is `200` with the typed `Automation[]`

#### Scenario: Create

- **WHEN** an authorised caller POSTs a new automation with `{ trigger: { kind: "scheduled", cronExpression: "*/5 * * * *" }, action: { kind: "launchRun", spec: { profileKey: "ops-critique", brief: "..." } } }`
- **THEN** the response is `201` with the persisted automation view

#### Scenario: Archive

- **WHEN** an authorised caller archives an automation
- **THEN** the response is `204` and the automation is hidden from the list under the default filter

### Requirement: Dashboard domain is parallel to operator

`dashboard/src/automations/` (mirroring the existing operator-domain shape — feature module under `domains/automations/`) SHALL expose a list + detail + history view with the existing `EmptyState` primitive for empty cases. The dashboard consumes the typed `Automation` object; no second contract.

#### Scenario: Empty state for a project with no automations

- **WHEN** an operator opens `/automations` for a project with zero automations
- **THEN** the page renders the `EmptyState` primitive with an `Add automation` call to action

#### Scenario: History render with mixed sources

- **WHEN** the page renders an automation whose last three fires came from two cron fires and one webhook
- **THEN** the history list shows the three rows with their source-coloured label (the existing domain primitive)

## ADAPTER Notes

The three source tables — the scheduler's `jobs` (in the `scheduler` schema, owned by `Comuki.Modules.Scheduler` — today the v1.x surface is `LastFiredAt` / `NextFireAt` on the `jobs` row; the future `firings` history table is the open follow-up from `add-scheduled-jobs`), `source_connections` (intake), and `sync_jobs` (intake) — are not modified. The `Outbox` (`OutboxMessage`, `NoopOutboxPublisher`) is the existing surface; this capability uses it for `SyncBack` only. The dashboard's existing operator-domain pattern (`/domains/<x>/` with features + hooks + stories) is the template; no new top-level dashboard app.

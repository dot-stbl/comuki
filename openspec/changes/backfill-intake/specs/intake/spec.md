## Purpose

Defines tracker ingress into Comuki: anonymous signed webhooks, source
connections, admission rules (watch vs inbox), the human inbox/claim loop,
native ticket creation, and sync-back jobs that push status to trackers.

## ADDED Requirements

### Requirement: Anonymous webhook ingress
`POST /api/hooks/{provider}/{key}` SHALL be anonymous. The per-connection
routing key SHALL resolve the source connection before signature verification;
a verified tracker signature IS authentication. Unknown provider/connection
answers 404; bad signature answers 401. Deliberately ignored deliveries
(replay, skip, filtered, duplicate) SHALL answer 200 with an outcome label so
trackers do not retry intentional drops.

#### Scenario: Bad signature
- **WHEN** a delivery fails signature verification
- **THEN** the answer is 401 ProblemDetails and no ticket is admitted

#### Scenario: Duplicate delivery
- **WHEN** a delivery id was already recorded
- **THEN** the answer is 200 with outcome `duplicate`

### Requirement: Supported providers
Intake SHALL support kebab-case provider keys `github`, `gitlab`, `jira`, and
`yandex-tracker`, each with payload mapping, signature verification, and
sync-back client. Unsupported provider names answer 404.

#### Scenario: Unknown provider
- **WHEN** a webhook posts to `/api/hooks/bitbucket/{key}`
- **THEN** the answer is 404

### Requirement: Source connections
Source connections SHALL persist under `source_connections` with settings,
env-ref secrets (never inline secrets), and a generated webhook routing key.
REST under `/api/v1/sources` SHALL demand `intake:read` for reads and
`source:write` for create/update/delete.

#### Scenario: Create source
- **WHEN** an operator with `source:write` creates a connection
- **THEN** the response is 201 including the webhook routing key once

### Requirement: Admission rules
Per-project admission rules SHALL live in `admission_rules` with mode
(watch → auto-admit vs inbox → pending) and a filter expression. Rules CRUD
under `/api/v1/admission-rules` SHALL demand `intake:read` / `source:write`.

#### Scenario: Inbox mode holds ticket
- **WHEN** a matching rule is inbox-mode and a webhook arrives
- **THEN** the ticket is stored pending and not launched as a run

### Requirement: Inbox and claim
`GET /api/v1/inbox` and `GET /api/v1/inbox/catalog` SHALL list pending tickets
(`intake:read`). `POST /api/v1/inbox/claim` SHALL require `intake:claim` and
launch a run through the host run-launcher port for the claimed ticket.

#### Scenario: Claim launches run
- **WHEN** an operator claims a pending ticket
- **THEN** a run is created for the project and the ticket records the active
  run id

### Requirement: Native tickets
`POST /api/v1/tickets` SHALL create a native (non-tracker) ticket and demand
`run:create`, returning 201 with the ticket view.

#### Scenario: Native ticket without webhook
- **WHEN** a caller creates a native ticket with project and title
- **THEN** the ticket is stored without a source connection and can enter
  admission/claim flows

### Requirement: Sync-back outbox
Status transitions destined for trackers SHALL enqueue `sync_jobs` rows. A
background bridge SHALL push transitions through the provider sync port and
update job status. Failures remain retryable in the outbox.

#### Scenario: Sync job recorded
- **WHEN** an admitted ticket's run reaches a terminal status that maps to a
  tracker transition
- **THEN** a sync job row exists for the provider push

### Requirement: Persistence layout
Intake tables (`intake_tickets`, `intake_deliveries`, `source_connections`,
`admission_rules`, `sync_jobs`) SHALL use module-private migrations history
`__comuki_intake`. Deliveries SHALL insert-first for idempotency.

#### Scenario: Replay uses delivery lock
- **WHEN** the same delivery id is inserted twice
- **THEN** the unique delivery constraint makes the second insert a no-op path
  answering 200 `duplicate`

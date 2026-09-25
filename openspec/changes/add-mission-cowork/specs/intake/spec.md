## MODIFIED Requirements

### Requirement: Integrations context replacement
Before the first product release, the Intake bounded context SHALL be replaced by Integrations. Existing ingress connections, provider normalization, delivery deduplication, admission rules, inbox, source sync, and outbound integration behavior move to the new context and `integrations` PostgreSQL schema under a clean baseline. Existing development data and old Intake API compatibility are not preserved. Public routes use resource-oriented nested segments under `/api/v1/integration/*`; provider ingress remains `/api/hooks/{provider}/{key}`. Native Task creation bypasses Integrations and calls Work directly.

#### Scenario: External tracker item arrives
- **WHEN** a signed provider webhook is normalized and admitted
- **THEN** Integrations stores an InboundItem and emits an idempotent command for Work to create the source-linked Task

#### Scenario: Native Task is created
- **WHEN** a user creates a Task from dashboard, CLI, or Brain capability
- **THEN** Work creates it directly without a synthetic InboundItem

### Requirement: Inbox and claim
`GET /api/v1/inbox` and `GET /api/v1/inbox/catalog` SHALL list pending tickets (`intake:read`). `POST /api/v1/inbox/claim` SHALL require `intake:claim` and idempotently admit the ticket into one standalone Task through the Work context. The Task records the source link and may dispatch its first Run according to Task policy. A concurrent or retried claim SHALL resolve to the same Task and SHALL NOT leave an orphan Run.

#### Scenario: Claim creates Task
- **WHEN** an operator claims a pending ticket
- **THEN** a standalone Task is created once, the ticket records its Task id, and any first Run belongs to that Task

#### Scenario: Claim launches run
- **WHEN** an operator claims a pending ticket whose dispatch policy permits immediate execution
- **THEN** the admitted Task creates its first Run and the ticket records the authoritative Task id

### Requirement: Native tickets
`POST /api/v1/tickets` SHALL create a native intake record and admit it into a standalone Task under `run:create`, returning 201 with the Task/source projection. The native intake record remains the ingress and provenance source; it is not the durable Task aggregate.

#### Scenario: Native ticket without webhook
- **WHEN** a caller creates a native ticket with project and title
- **THEN** the platform creates one source-linked standalone Task without requiring a Mission

### Requirement: Sync-back outbox
Task or Mission resolution destined for trackers SHALL enqueue idempotent `sync_jobs`. Intermediate Run terminal states SHALL NOT close or finalize a tracker ticket while its Task remains unresolved. A background bridge SHALL push the resolved outcome through the provider sync port; retries and migration cutover SHALL not publish duplicate terminal comments.

#### Scenario: Failed attempt is replaced
- **WHEN** a tracker-linked Task's first Run fails and a replacement Run later succeeds
- **THEN** sync-back publishes the resolved Task outcome rather than finalizing the tracker at the first Run failure

#### Scenario: Sync job recorded
- **WHEN** a tracker-linked Task reaches a resolved outcome
- **THEN** one idempotent sync job is recorded for the provider bridge

## ADDED Requirements

### Requirement: Run artifact bundle journal event
The `run_events` journal SHALL record a `run.artifacts_bundled` event every
time the host-composed packager finishes bundling a run. The event payload
SHALL carry the canonical artifact pointer list (object names plus
canonical URIs the host can fetch, typically MinIO signed URLs) and the
terminal status the run landed on. The event SHALL be appended by the host
in the same logical commit as the bundle row it represents; downstream
consumers (SignalR `RunEvent` broadcast, dashboard Artifacts tab) SHALL
see the event after the next journal poll.

#### Scenario: Terminal run lands a bundled event
- **WHEN** a run transitions to a terminal status and the packager uploads
  its objects
- **THEN** the journal carries a `run.artifacts_bundled` entry whose
  payload lists every object name + URI the packager uploaded

#### Scenario: In-flight run never emits the event
- **WHEN** a run is `queued`, `running`, or `waiting`
- **THEN** the packager skips it and no `run.artifacts_bundled` entry is
  appended for that run

### Requirement: Run artifacts read API
`GET /api/v1/projects/{projectId:guid}/runs/{runId:guid}/artifacts` (route
constant `ApiRoutes.RunArtifacts`) SHALL return the same artifact pointer
list the `run.artifacts_bundled` event carries, in the order the packager
uploaded them. The endpoint SHALL require permission `run:read`; an
unauthenticated caller answers 401, an authenticated caller without the
permission answers 403. The response is an empty list when the run has
not been bundled yet (still in flight, or the packager has not yet
observed the terminal transition).

#### Scenario: Read returns bundled pointers
- **WHEN** an authenticated caller with `run:read` queries the artifacts
  endpoint for a run whose packager has already bundled
- **THEN** the response is 200 with the same pointer list the
  `run.artifacts_bundled` event carried

#### Scenario: Empty list before bundling
- **WHEN** an authenticated caller with `run:read` queries the artifacts
  endpoint for an in-flight run
- **THEN** the response is 200 with an empty `items` array — the run has
  not been bundled yet

#### Scenario: Authentication required
- **WHEN** an anonymous client calls the artifacts endpoint
- **THEN** the response is 401 `application/problem+json`

### Requirement: Webhook delivery outcome labels
The intake webhook pipeline SHALL classify every delivery against a fixed
set of outcome labels: `admitted`, `pending`, `filtered`, `skipped`,
`duplicate`, `rejected`, `replay`. The label SHALL be recorded on the
`intake_deliveries` row and SHALL appear in the `outcome` field of the
webhook response. Labels `admitted`, `pending`, `filtered`, `skipped`,
`duplicate`, and `replay` answer 200; `rejected` answers 401. Trackers
SHOULD not retry `replay`, `duplicate`, `skipped`, `filtered`,
`pending`, or `admitted` (the latter is a one-time launch signal).

#### Scenario: Replay short-circuits
- **WHEN** a delivery id has been recorded before
- **THEN** the answer is 200 with outcome `replay` and no ticket is
  touched

#### Scenario: Bad signature answers rejected
- **WHEN** signature verification fails
- **THEN** the answer is 401 with outcome `rejected` and a stable code
  `intake.signature_invalid`

#### Scenario: Watch mode admits into a run
- **WHEN** a delivery matches a `watch` admission rule
- **THEN** the answer is 200 with outcome `admitted` and a run id in the
  detail

#### Scenario: Inbox mode parks the ticket
- **WHEN** a delivery matches an `inbox` admission rule
- **THEN** the answer is 200 with outcome `pending` and no run is
  launched until an operator claims the ticket

#### Scenario: Filtered out by admission rules
- **WHEN** no enabled admission rule matches the normalized ticket
- **THEN** the answer is 200 with outcome `filtered` and the ticket is
  stored dismissed

#### Scenario: Duplicate active ticket
- **WHEN** an active ticket for the same external id already exists in
  the project
- **THEN** the answer is 200 with outcome `duplicate` and the existing
  ticket is untouched

#### Scenario: Non-ticket event skipped
- **WHEN** the provider normalizes the delivery to null (ping, unrelated
  event kind)
- **THEN** the answer is 200 with outcome `skipped`

## ADDED Requirements

### Requirement: Run artifact bundle journal broadcast
The `IRunEventsBroadcaster` SHALL forward `run.artifacts_bundled` journal
entries the same way it forwards any other `run_events` row: to the
`run:{id}` group as a `RunEvent` client method. The entry is broadcast
after the host appends it to the journal; consumers joining the run
group after the broadcast miss the pointer list and SHALL fall back to
`GET /api/v1/projects/{projectId}/runs/{runId}/artifacts` (see runs).

#### Scenario: Bundled event reaches the run group
- **WHEN** the host appends a `run.artifacts_bundled` row for run R
- **THEN** every connected member of `run:R` receives a `RunEvent`
  client method whose payload carries the artifact pointer list

## ADDED Requirements

### Requirement: Admission journal event types
The `run_events` type set SHALL include `worker.admitted` (payload: admission id, env class, profile key, isolation class; no secret values) and `worker.admission_denied` (payload: typed `admission.*` code). These types are platform-owned.

#### Scenario: Timeline shows a deny
- **WHEN** admission fails with `admission.capacity`
- **THEN** the journal ordered by `OccurredAt` then `Id` contains `worker.admission_denied` with that code

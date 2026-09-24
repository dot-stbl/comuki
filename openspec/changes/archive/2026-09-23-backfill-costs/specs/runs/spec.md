## ADDED Requirements

### Requirement: Budget exceeded journal type
The append-only run journal type set SHALL include `budget.exceeded`. The
payload SHALL carry spent and hard-limit USD micros plus the project id.
Emission is owned by the host budget gate before/with run cancel (see costs).

#### Scenario: Hard budget journals
- **WHEN** a hard budget stop cancels a run
- **THEN** a `budget.exceeded` entry appears on that run's timeline

## ADDED Requirements

### Requirement: Verify journal event types
The `run_events` type set SHALL include `verify.completed` (payload: opcode list, duration) and `verify.failed` (payload: opcode name, exit code, truncated log; no secret values). A Host crash SHALL NOT be the signal of a failed product build.

#### Scenario: Failed build is verify.failed
- **WHEN** verify `dotnet build` exits 1
- **THEN** the journal contains `verify.failed` with exit code 1 and the Host process is still running

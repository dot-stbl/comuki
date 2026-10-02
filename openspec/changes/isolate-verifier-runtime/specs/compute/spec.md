## ADDED Requirements

### Requirement: Verifier slots are ordinary starts
A verification execution SHALL start through the same `StartAsync` contract as an implement worker, with a SlotAdmission whose env class equals the work item's class. The start MAY use profile key `verify`. It SHALL NOT use a Host-local process runner and SHALL NOT mount the Host docker socket.

#### Scenario: Verify start is a compute start
- **WHEN** verify is requested for an item of class `net10-sdk-bun`
- **THEN** compute starts a slot admitted as `net10-sdk-bun` and profile `verify` (or implement with a verify-only loop)

#### Scenario: Verify cannot use a thinner class
- **WHEN** the item is `net10-sdk-bun` and a runtime-only image is idle
- **THEN** that image is not assigned to the verify slot

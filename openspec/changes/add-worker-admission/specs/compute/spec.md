## ADDED Requirements

### Requirement: Start requires SlotAdmission
`StartAsync` SHALL require a successful SlotAdmission identifier on the start request. Profile, env class, isolation, secret refs, and image digest SHALL be taken from that admission (catalog resolve), not from unvalidated caller strings. A request that supplies a conflicting image or class SHALL fail.

#### Scenario: Conflicting image is rejected
- **WHEN** a start request names admission for `net10-sdk-bun` and also an image digest from another class
- **THEN** start fails and no container is created

### Requirement: Hosts advertise env class
A worker host (1:1 container today, warm host after worker-pools) SHALL advertise the env classes it can run. Placement SHALL match the admitted class. A host that does not advertise the class SHALL NOT be assigned the slot.

#### Scenario: Advertised class mismatch
- **WHEN** the only idle host advertises `net10-sdk` and the admission is `ue5.4-win`
- **THEN** that host is not assigned; the planner reports `admission.capacity` if no other host matches

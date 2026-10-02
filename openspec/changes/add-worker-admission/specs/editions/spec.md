## ADDED Requirements

### Requirement: Fifth enforcement point is the coding-agent slot
Edition coverage SHALL be enforced at SlotAdmission as a fifth point, calling the same `EditionGate` decision as API, modules, hosted `IComukiWorker`, and dashboard visibility. This point is the ephemeral coding-agent worker, not the hosted-service worker. A class or runtime the registry marks paid (for example Windows/GPU bundles or community marketplace) SHALL deny with `admission.edition` / `edition.feature_unavailable` when the license does not cover it.

#### Scenario: Community cannot start a paid GPU class
- **WHEN** a Community edition admits a slot whose env class requires a paid GPU feature
- **THEN** admission denies with `admission.edition` and no container starts

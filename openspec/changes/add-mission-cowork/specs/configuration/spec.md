## Purpose

Defines a typed, explainable, and safely applicable configuration plane so operators and Brain can inspect and tune Comuki without bypassing deployment or authorization boundaries.

## ADDED Requirements

### Requirement: Typed configuration catalog
Every Brain-manageable setting SHALL have a stable key, schema, scope, default, validation, sensitivity classification, owner capability, source precedence, and apply class. Effective precedence SHALL be compiled defaults, configuration file, managed database revision, environment variables, then CLI startup overrides. Brain and clients SHALL use typed read/preflight/change capabilities rather than editing arbitrary files or environment variables.

#### Scenario: Brain explains a setting
- **WHEN** a user asks Brain how Mission autonomy is configured
- **THEN** Brain reads the typed catalog and reports current/effective values, source, constraints, and impact without exposing secrets

#### Scenario: Environment overrides managed value
- **WHEN** Brain changes a database-managed setting that is overridden by an environment variable
- **THEN** the desired revision is recorded but clients report the environment source as effective and provide the required restart/redeploy guidance

### Requirement: Configuration apply classes
Settings SHALL declare one apply class: `hot-reload`, `restart-required`, or `redeploy-required`. Hot-reload changes update live consumers through the supported reload mechanism. Restart/redeploy changes create a staged desired-state operation with explicit impact and SHALL NOT be reported as active before the required action succeeds.

#### Scenario: Backplane changes
- **WHEN** an authorized change switches realtime backplane from InMemory to Redis
- **THEN** preflight reports restart/deployment requirements and the active configuration remains unchanged until rollout succeeds

### Requirement: Brain-managed configuration
Brain MAY read, explain, compare, validate, and propose any non-hard-denied typed configuration. Applying changes SHALL follow effective autonomy and context-owned policy. Role/grant definitions, Brain exposure/autonomy maxima, bootstrap, runtime protocol, and secret plaintext remain subject to their stricter control-plane rules.

#### Scenario: Hot-reload project setting
- **WHEN** an authorized Autopilot operation changes a hot-reload project limit within policy
- **THEN** the validated revision becomes effective without restart and the operation records old/new values and actor

### Requirement: Safe rollback and concurrency
Configuration revisions SHALL use optimistic version guards and preserve a redacted audit history. Preflight SHALL detect conflicting edits and declare rollback semantics. Rollback is a new validated revision, not mutation of history.

#### Scenario: Concurrent edits
- **WHEN** two actors change the same settings revision
- **THEN** only the operation with the expected version applies and the other receives a conflict with a refreshed diff

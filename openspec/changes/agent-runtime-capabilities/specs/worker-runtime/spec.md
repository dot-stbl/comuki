## ADDED Requirements

### Requirement: Worker sees injected secrets

A worker container SHALL, on start, expose each resolved secret as
an environment variable whose name is the `EnvName` from the matching
`SecretRefSpec`. The variable SHALL be present at the moment the
worker's entrypoint runs; the worker SHALL NOT need to fetch or
unwrap anything to read it. The provider SHALL be responsible for
the env shape (Docker `-e` or K8s Secret mount); the worker contract
treats all secrets as opaque env entries.

#### Scenario: Worker reads the env directly

- **WHEN** a worker is started with
  `SecretRefs = [OPENAI_API_KEY, JIRA_API_TOKEN]`
- **THEN** `os.environ["OPENAI_API_KEY"]` and
  `os.environ["JIRA_API_TOKEN"]` are readable from the entrypoint

### Requirement: Worker reports secret manifest to the orchestrator

When the worker calls `claim` over gRPC, the orchestrator SHALL
attach a list of `secretReferences` describing which env names the
worker is requesting (read from the host env at start). The
translator loop SHALL NOT log the values; the claim body SHALL
carry only the env names and a one-way hash of each value
(FNV-1a 64-bit, hex) so the orchestrator can verify the env matches
what it injected without ever storing the plaintext.

#### Scenario: Claim body has hashes only

- **WHEN** a worker claims a work item
- **THEN** the claim body's `secretReferences` array contains only
  env names and one-way hashes; values are not present anywhere in
  the journal

### Requirement: Worker never logs secret values

The worker runtime SHALL add a logger filter that drops any log
record whose payload matches `^[A-Za-z0-9+/=_-]{20,}$` AND is found
in the set of injected secret values. The filter is best-effort
and SHALL NOT replace structured logging discipline; it exists to
catch the obvious foot-gun of `console.log(env.X)` in worker code.

#### Scenario: Logger filter blocks leak

- **WHEN** a worker attempts to log an injected secret value
- **THEN** the record is dropped and a counter
  `comuki.worker.secret_log_suppressed` increments; the worker
  receives a notice in the journal entry but no plaintext

### Requirement: Worker secret rotation requires restart

A worker that was started with a set of `SecretRefs` SHALL continue
to use the values injected at start; rotation in the catalog SHALL
NOT live-reload into a running worker. A re-claim after the
existing worker exits picks up the new version. The host SHALL
document this clearly in the secret's detail page.

#### Scenario: Rotation does not re-inject

- **WHEN** a local secret is rotated while a worker is running
- **THEN** the running worker keeps the previous value; the next
  worker start sees the new value
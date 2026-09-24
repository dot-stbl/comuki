## ADDED Requirements

### Requirement: Kubernetes provider selection
`Compute:Provider` SHALL accept `docker` or `kubernetes`. The composition root
SHALL resolve `IComputeProvider` to the matching implementation. Orchestration
and the scale supervisor SHALL continue to depend only on the port.

#### Scenario: Provider switch
- **WHEN** configuration sets `Compute:Provider=kubernetes`
- **THEN** Start/Stop/List/GetCapacity execute against the Kubernetes
  implementation without supervisor code changes

### Requirement: Kubernetes Job contract
The Kubernetes provider SHALL run one worker as one `batch/v1` Job with
`backoffLimit: 0`, `restartPolicy: Never`, configured
`ttlSecondsAfterFinished`, optional service account and node selector, and
CPU/memory requests from `Compute:Kubernetes` options. The Job name SHALL be
`comuki-w-{trailing-12-hex-of-worker-id-N-form}` so UUID7 timestamp prefixes
do not collide. The worker id SHALL be stored as annotation `comuki.worker_id`
(not a claim label). Claim-matching labels SHALL reuse the shared `comuki.*`
contract with slash sanitization.

#### Scenario: Job name uniqueness for same-ms UUID7
- **WHEN** two workers are minted in the same millisecond
- **THEN** their Job names differ because the suffix uses trailing random hex

#### Scenario: Failed Job is not retried by Kubernetes
- **WHEN** a worker container exits non-zero
- **THEN** the Job does not restart the pod; lease expiry reclaims the work item

### Requirement: Kubernetes list and capacity
`ListAsync` SHALL return only Jobs with an active pod for the project label
selector, mapping labels/annotation back to `WorkerInfo`. `StopAsync` SHALL
delete the Job with Foreground propagation; soft stop reasons use configured
grace, Force uses 0; NotFound is a no-op. `GetCapacityAsync` SHALL estimate
free slots from node allocatable minus pod requests (coarse hint).

#### Scenario: Finished Job excluded
- **WHEN** a Job has finished but TTL has not collected it
- **THEN** ListAsync does not report it as a running worker

#### Scenario: Stop missing Job
- **WHEN** StopAsync targets a Job already TTL-collected
- **THEN** the call succeeds as a no-op

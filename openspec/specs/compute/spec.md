# Compute Specification

## Purpose

Defines the container-compute layer: the `IComputeProvider` port, the Docker and Kubernetes providers (worker env/label contract), the opaque worker-token issuer, and the scale supervisor v0 that keeps an idle worker pool matched to queue backlog.

## Requirements

### Requirement: IComputeProvider port

Container runtimes SHALL implement a shared contract: `StartAsync` (start one worker container from a request), `StopAsync` (stop and remove a worker's runtime for a reason), `ListAsync` (running workers of a project, label-selected), `GetCapacityAsync` (allocatable-capacity hint). Orchestration and scaling SHALL depend only on this port, never on a runtime SDK. A start request SHALL carry project id, optional pre-issued worker id, profile key, pinned profiles git ref, image with digest, worker token, orchestrator gRPC URL and extra env entries.

#### Scenario: Swap provider
- **WHEN** a Kubernetes provider replaces the Docker provider behind the same port
- **THEN** the pool and scale supervisor operate without changes

### Requirement: WorkerId pre-issue alignment

`StartAsync` SHALL reuse a caller-provided `PreIssuedWorkerId` instead of minting its own, so the token identity and the container identity agree. When absent, the provider mints a fresh UUIDv7 worker id.

#### Scenario: Supervisor pre-issues the identity
- **WHEN** the supervisor issues a token for worker id W and starts a container with `PreIssuedWorkerId = W`
- **THEN** the returned handle carries W and REST/gRPC calls under that token map to W

### Requirement: Docker provider environment contract

The Docker provider SHALL stamp every worker container with, in order: the `COMUKI_*` contract env (`COMUKI_WORKER_TOKEN`, `COMUKI_PROJECT_ID`, `COMUKI_PROFILE_KEY`, `COMUKI_PROFILES_REF`, `COMUKI_WORKER_IMAGE`, `COMUKI_ORCH_GRPC`) followed by caller-supplied extra env entries. Containers SHALL join the configured network (`Compute:Docker:NetworkMode`, default `bridge`; a compose deployment overrides it) and SHALL be named `comuki-{projectId:N}-{12-char worker suffix}`.

#### Scenario: Env stamped on start
- **WHEN** a worker container starts
- **THEN** its environment carries all six `COMUKI_*` variables and the extras, and it is reachable on the compose network

### Requirement: Docker provider labels and sanitization

Every worker container SHALL carry the claim-matching labels `comuki.project`, `comuki.profile`, `comuki.image`, `comuki.profiles_ref`, plus the provider-local `comuki.worker_id` used to map containers back to orchestrator worker ids. Label values SHALL be sanitized (`/` → `_`) because Kubernetes label values cannot contain slashes; claim matching compares sanitized values on both sides. `ListAsync` SHALL return workers with id, provider ref, profile, image and profiles ref parsed from these labels, skipping containers whose worker-id label is missing or unparsable. `StopAsync` SHALL stop (SIGTERM with `WaitBeforeKillSeconds` grace, default 10) then force-remove every container of the worker. `GetCapacityAsync` SHALL report `FreeSlots = max(0, MaxWorkers - running)` and the running count.

#### Scenario: Profiles ref with slashes
- **WHEN** a worker starts with profiles ref `refs/tags/v1.2`
- **THEN** the label value is `refs_tags_v1.2` and list reports the sanitized form

#### Scenario: Container name uniqueness
- **WHEN** two workers of the same project start
- **THEN** their container names differ by the 12-char worker-id suffix

### Requirement: Opaque 256-bit worker tokens

The token issuer SHALL issue opaque tokens: 32 random bytes, base64url-encoded. Only `HMAC-SHA256(pepper, token)` SHALL be stored, with the worker id and expiry; the plaintext exists only at issue time. One live token per worker — a new issue replaces the previous record. Validation SHALL compare the computed HMAC against every stored record with `CryptographicOperations.FixedTimeEquals` and no early exit, returning the worker id only for a live (unexpired, unrevoked) match. Revocation SHALL remove the worker's record and be a no-op when absent.

#### Scenario: Timing does not reveal matches
- **WHEN** a token is validated against the store
- **THEN** every record pays the comparison cost regardless of which one matched

#### Scenario: Replaced token dies
- **WHEN** a worker's token is re-issued
- **THEN** the previous token no longer validates

### Requirement: Token pepper and TTL from configuration

The HMAC pepper SHALL come from the `COMUKI_TOKEN_PEPPER` environment variable; when unset a documented dev-only default is used (production MUST set the variable — rotating the pepper invalidates all stored hashes by design). Default token TTL SHALL be 15 minutes (bounded 1 minute–24 hours); an explicit TTL may override per issue. The v1 token store is process-local in-memory: tokens die with an orchestrator restart, which the short TTL makes acceptable.

#### Scenario: Expired token
- **WHEN** a worker presents a token past its expiry
- **THEN** validation returns no worker id

### Requirement: Scale policy v0 (create-per-task)

The scale decision per project × profile SHALL be the pure function: `StartWorkers = clamp(QueuedCount - IdleCount, 0, MaxConcurrent - RunningCount)` and `StopIdleWorkers = max(0, min(StaleIdleCount, IdleCount - MinIdle))`. Inputs are the queued backlog of the profile, idle workers of the profile, stale idle workers (past idle TTL), all running workers of the project (the cap denominator), and the project's `MinIdle`/`MaxConcurrent`. No I/O — the supervisor maps the decision to provider calls.

#### Scenario: Backlog beyond cap
- **WHEN** 10 items are queued, 0 idle, and MaxConcurrent is 4 with 1 running
- **THEN** the policy starts 3 workers, not 10

#### Scenario: Never reap below the warm floor
- **WHEN** 2 stale idle workers exist and MinIdle is 1
- **THEN** at most 1 is stopped

### Requirement: Scale supervisor pass

The supervisor SHALL run one pass per poll interval (default 15 seconds; a failed pass is logged and retried on the next tick). Per configured project: reconcile the pool with the provider, then per configured profile read the backlog, apply the scale policy, start the decided workers (pre-issuing each WorkerId and its token, image/profiles-ref from per-project settings falling back to supervisor options), and stop stale idle workers oldest-first with `ComputeStopReason.IdleTtl`, revoking each stopped worker's token and removing it from the pool. Idle TTL default is 10 minutes. An empty `Projects` or `ProfileKeys` list makes the pass a no-op. Defaults: `MinIdle` 0, `MaxConcurrent` 4, worker image `ghcr.io/comuki/worker:latest`, profiles ref `main`.

#### Scenario: Idle worker reaped after TTL
- **WHEN** an idle worker exceeds the project's idle TTL and the warm floor allows it
- **THEN** the supervisor stops the container, revokes the token and drops it from the pool

#### Scenario: Adopted workers get a full TTL
- **WHEN** the orchestrator restarts and the pool reconciles against running containers it no longer knows
- **THEN** unknown running workers are adopted as idle with activity = now, so they run a full idle TTL before reaping; cached workers the provider no longer lists are dropped

### Requirement: Per-project scale settings port

Scale knobs SHALL be read through a per-project settings port returning `MinIdle`, `MaxConcurrent`, `IdleTtl` and optional image/profiles-ref overrides; null image/ref fall back to supervisor options. The engine ships an in-memory default store; the host replaces it with the Projects-backed adapter (see projects) at composition time.

#### Scenario: Host swaps the store
- **WHEN** the host registers the Projects settings-backed adapter
- **THEN** supervisor decisions observe settings writes without a restart

### Requirement: Kubernetes provider selection

`Compute:Provider` SHALL accept `docker` or `kubernetes`. The composition root SHALL resolve `IComputeProvider` to the matching implementation. Orchestration and the scale supervisor SHALL continue to depend only on the port.

#### Scenario: Provider switch
- **WHEN** configuration sets `Compute:Provider=kubernetes`
- **THEN** Start/Stop/List/GetCapacity execute against the Kubernetes implementation without supervisor code changes

### Requirement: Kubernetes Job contract

The Kubernetes provider SHALL run one worker as one `batch/v1` Job with `backoffLimit: 0`, `restartPolicy: Never`, configured `ttlSecondsAfterFinished`, optional service account and node selector, and CPU/memory requests from `Compute:Kubernetes` options. The Job name SHALL be `comuki-w-{trailing-12-hex-of-worker-id-N-form}` so UUID7 timestamp prefixes do not collide. The worker id SHALL be stored as annotation `comuki.worker_id` (not a claim label). Claim-matching labels SHALL reuse the shared `comuki.*` contract with slash sanitization.

#### Scenario: Job name uniqueness for same-ms UUID7
- **WHEN** two workers are minted in the same millisecond
- **THEN** their Job names differ because the suffix uses trailing random hex

#### Scenario: Failed Job is not retried by Kubernetes
- **WHEN** a worker container exits non-zero
- **THEN** the Job does not restart the pod; lease expiry reclaims the work item

### Requirement: Kubernetes list and capacity

`ListAsync` SHALL return only Jobs with an active pod for the project label selector, mapping labels/annotation back to `WorkerInfo`. `StopAsync` SHALL delete the Job with Foreground propagation; soft stop reasons use configured grace, Force uses 0; NotFound is a no-op. `GetCapacityAsync` SHALL estimate free slots from node allocatable minus pod requests (coarse hint).

#### Scenario: Finished Job excluded
- **WHEN** a Job has finished but TTL has not collected it
- **THEN** ListAsync does not report it as a running worker

#### Scenario: Stop missing Job
- **WHEN** StopAsync targets a Job already TTL-collected
- **THEN** the call succeeds as a no-op

## ADAPTER Notes

`IBacklogReader` (queued count per project/profile) is the seam the host fills with the work-queue adapter; the compute engine itself never references the orchestration context.

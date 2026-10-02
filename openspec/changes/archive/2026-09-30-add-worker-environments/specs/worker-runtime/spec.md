## MODIFIED Requirements

### Requirement: Worker REST surface
The worker REST API SHALL expose, all authenticated by the worker token in the `Authorization` header (401 `worker.unauthenticated` ProblemDetails otherwise):

- `POST /workers/claim` — body: **envClass**, profilesRef, profileKey; 200 with the claimed item (workItemId, runId, profileKey, envClass, brief, leaseUntil unix-ms, attempt, generation) or 204 when the queue has nothing; 400 on validation failure
- `POST /workers/{workItemId}/heartbeat` — extends the lease; body carries the claimed generation; 204 held, 409 `work-item.not-owner` when the item is unknown, not running, leased to another worker, or leased at a stale generation (the Run was cancelled or superseded since claim)
- `POST /workers/{workItemId}/complete` — body: non-empty result JSON plus the claimed generation; 204 / 409 ownership-or-generation
- `POST /workers/{workItemId}/fail` — body: non-empty reason plus the claimed generation; 204 / 409 ownership-or-generation

The worker id the queue sees is the one the token was issued for — ownership is token-derived, never claimed. A generation mismatch answers the same 409 `work-item.not-owner` code as an ownership miss — from the worker's perspective, losing a stale generation and losing a lease to another worker are the same "you no longer hold this" outcome; this change does not introduce a second 409 code. This requirement covers the single-worker `WorkerId`+lease ownership model that exists today; a per-slot `WorkerHostId`/`SlotId`/`ExecutionId` ownership model is out of scope here (see `add-worker-pools-and-isolation-classes`).

#### Scenario: Ownership miss is 409, not 500
- **WHEN** a worker completes an item the reaper already requeued
- **THEN** the answer is 409 with code `work-item.not-owner`

#### Scenario: Stale generation after cancellation is rejected
- **WHEN** a worker completes a WorkItem using the generation it claimed under, after the owning Run was cancelled or superseded (generation bumped)
- **THEN** the answer is 409 with code `work-item.not-owner` and the WorkItem/Run outcome is unaffected by the late completion

#### Scenario: Current generation still succeeds
- **WHEN** a worker completes a WorkItem using the Run's current generation
- **THEN** the completion is accepted exactly as before this change

#### Scenario: Claim body without envClass is 400
- **WHEN** a worker posts `/workers/claim` with profileKey and profilesRef but no envClass
- **THEN** the answer is 400

### Requirement: Translator loop
The worker's outer loop SHALL run claim → execute → report → repeat until the process stops; an empty claim waits the poll interval (default 10 seconds) before retrying. One cycle:

1. claim over REST (empty → return)
2. prepare the working directory: clone the target repository, then run the accepted `[restore]` opcodes from `.comuki/environment.toml` at that ref (host-side, not the coding agent); then prepare profiles material under `profiles/` (copy from the mounted path when configured, else shallow-clone the public git URL at the pinned ref, else warn and skip). Restore failure fails the item and skips spawn
3. open the gRPC session, send StageStart
4. spawn the agent executable and pump its stream-json output, forwarding text deltas / authoritative assistant text / tool invocations as Activity events while a heartbeat task extends the lease every interval (default 30 seconds) and a command task consumes orchestrator commands
5. send the final StageReport, close the session
6. if the lease was lost (rejected heartbeat or a `LeaseExpired` command): skip completion entirely — the reaper owns the item
7. else complete on `success` (result JSON = the serialized StageReport) or fail with a `status: error-text` reason otherwise

Failures propagate and stop the host — an ephemeral worker is meant to die and be replaced, not limp along.

#### Scenario: Non-zero pi exit fails the item
- **WHEN** the spawned agent process exits non-zero
- **THEN** the outcome is `failed` carrying the exit code and stderr, and the item is failed over REST

#### Scenario: Lease lost mid-run
- **WHEN** a heartbeat is rejected (409) while pi still runs
- **THEN** pi is cancelled, the report says `cancelled`, and no complete/fail is written — the reaper owns the item

#### Scenario: Result text is authoritative
- **WHEN** the agent streams text deltas and later a message-end assistant text
- **THEN** the summary replaces accumulated deltas with the authoritative final wording

#### Scenario: Restore runs before pi
- **WHEN** the claimed item's class declares `[restore] dotnet = "comuki.slnx"`
- **THEN** `dotnet restore comuki.slnx` completes before pi is started

### Requirement: Translator environment contract
The worker container's `COMUKI_*` environment SHALL map onto the Translator configuration: `COMUKI_ORCH_HTTP` (REST base URL), `COMUKI_ORCH_GRPC` (gRPC URL), `COMUKI_WORKER_TOKEN`, `COMUKI_PROFILE_KEY`, `COMUKI_PROFILES_REF`, **`COMUKI_ENV_CLASS`**, `COMUKI_WORKER_IMAGE` (resolved digest), `COMUKI_PROFILES_PATH`, `COMUKI_PROFILES_GIT_URL`, `COMUKI_PI_EXECUTABLE`, `COMUKI_WORKING_DIRECTORY`. Options validate on start — a missing orchestrator URL or token fails the boot. Missing `COMUKI_ENV_CLASS` SHALL fail the boot once this change is the running contract.

#### Scenario: Container env becomes config
- **WHEN** the compute provider stamps `COMUKI_WORKER_TOKEN`, `COMUKI_ORCH_GRPC` and `COMUKI_ENV_CLASS`
- **THEN** the Translator authenticates, connects, and claims with that class without further configuration

### Requirement: Worker container image
A worker image SHALL be a catalog environment-class bundle: Translator published from an SDK stage; final stage carries the class toolchain (for `net10-sdk-bun`: .NET 10 **SDK**, bun >= 1.4, git, CA certificates), the pi coding agent, invariant globalization as required by the base, and the translator as ENTRYPOINT with a `/work` volume. Image defaults set `COMUKI_ORCH_HTTP`/`COMUKI_ORCH_GRPC` to the compose-network orchestrator and `COMUKI_PI_EXECUTABLE=pi`; the rest of the env contract is stamped at container start. The runtime-only image without SDK SHALL NOT be the default class for implement work. A client Dev Container image SHALL NOT be used as ENTRYPOINT.

#### Scenario: Sanity-run the image
- **WHEN** the image runs with entrypoint overridden to `pi --version`
- **THEN** it reports a version without any orchestrator configured

#### Scenario: Sanity-run the golden class
- **WHEN** the `net10-sdk-bun` image runs with entrypoint overridden to `dotnet --version` and `bun --version`
- **THEN** both report versions without any orchestrator configured

#### Scenario: Runtime-only image is not the implement default
- **WHEN** implement work is enqueued for a repository bound to `net10-sdk-bun`
- **THEN** the started container is that class, not the historic runtime-only worker image

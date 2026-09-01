# Worker Runtime Specification

## Purpose

Defines how ephemeral workers execute work items: the bidirectional gRPC stream between the worker container (Translator) and the orchestrator, the worker REST claim/heartbeat/complete/fail surface, the Translator's claim → pi spawn → stream → report → complete loop, its environment contract, the worker container image, and the TestFakePi test harness.

## Requirements

### Requirement: Code-first gRPC bidi contract

The worker stream SHALL be a contract-first protobuf-net.Grpc service (no `.proto` files; `[Service]`/`[Operation]` attributes, runtime-built descriptors): `Connect(events)` returns the command stream while consuming the worker's event stream. Event and command messages SHALL be records discriminated by which optional field is set:

- `WorkerEvent`: `Start` (StageStart: workItemId, runId, brief) | `Activity` (StageActivity: workItemId + one of text chunk / tool name + raw tool-input JSON) | `Report` (StageReport: workItemId, status `success`|`failed`|`cancelled`, durationMs, resultText, errorText)
- `OrchestratorCommand`: `Stop` (reason) | `InjectContext` (context text) | `LeaseExpired` (empty)

#### Scenario: Contract has no .proto
- **WHEN** the worker client and orchestrator server are built
- **THEN** both reference the shared contracts assembly and no proto compilation step exists

### Requirement: Stream authentication

The worker SHALL authenticate with its opaque worker token in the `authorization` gRPC metadata key (lowercase per gRPC convention; a `Bearer ` prefix is tolerated and stripped). An unknown or expired token SHALL fail the call with gRPC `Unauthenticated` before any event processing. The server maps the token to the WorkerId it was issued for (see compute).

#### Scenario: Bad token dies at the gate
- **WHEN** a stream opens with an invalid token
- **THEN** the call throws RpcException Unauthenticated and no events are consumed

### Requirement: Stream semantics — end on events completion

One stream per connection, bound to one WorkerId. The call SHALL end when the worker completes its events enumeration (the worker's "I'm done" signal); commands flow orchestrator → worker only while events are still coming. When the events side finishes, the command pump is cancelled — no pending `MoveNextAsync` is left for dispose. A worker dropping the stream mid-events is an expected close path, not a fault; real event-pump faults propagate.

#### Scenario: Report then complete ends the call
- **WHEN** the worker finishes its event stream after sending the final StageReport
- **THEN** the server's command loop ends and the stream closes cleanly

### Requirement: Journaling and the StageStart binding

The server SHALL bind each stream to a run on the first `StageStart` (parsing its runId) and append every event to that run's journal as a `worker.reported` entry whose payload mirrors the stage record (camelCase JSON). Events arriving before a StageStart — or with an unparsable binding — SHALL be dropped with a warning; the protocol guarantees Start first (see runs for the journal).

#### Scenario: Activity before Start dropped
- **WHEN** an Activity event arrives before any StageStart on the stream
- **THEN** it is not journaled and a warning is logged

### Requirement: Outbound command hub

Anything in the orchestrator needing to reach a connected worker mid-run (chat Stop, context inject, lease reaper) SHALL send through a per-worker command channel (bounded, one per connected WorkerId; a new stream replaces the previous channel). Sends are best-effort: a worker without a live stream is a miss (false), not an error.

#### Scenario: Stop without a stream
- **WHEN** the orchestrator soft-stops a worker that has no live stream
- **THEN** the pipe reports false and nothing throws

### Requirement: Worker REST surface

The worker REST API SHALL expose, all authenticated by the worker token in the `Authorization` header (401 `worker.unauthenticated` ProblemDetails otherwise):

- `POST /workers/claim` — body: image, profilesRef, profileKey; 200 with the claimed item (workItemId, runId, profileKey, brief, leaseUntil unix-ms, attempt) or 204 when the queue has nothing; 400 on validation failure
- `POST /workers/{workItemId}/heartbeat` — extends the lease; 204 held, 409 `work-item.not-owner` when the item is unknown, not running, or leased to another worker (the lease may have expired)
- `POST /workers/{workItemId}/complete` — body: non-empty result JSON; 204 / 409 ownership
- `POST /workers/{workItemId}/fail` — body: non-empty reason; 204 / 409 ownership

The worker id the queue sees is the one the token was issued for — ownership is token-derived, never claimed.

#### Scenario: Ownership miss is 409, not 500
- **WHEN** a worker completes an item the reaper already requeued
- **THEN** the answer is 409 with code `work-item.not-owner`

### Requirement: Translator loop

The worker's outer loop SHALL run claim → execute → report → repeat until the process stops; an empty claim waits the poll interval (default 10 seconds) before retrying. One cycle:

1. claim over REST (empty → return)
2. prepare profiles material under `profiles/` in the working directory (copy from the mounted path when configured, else shallow-clone the public git URL at the pinned ref, else warn and skip)
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

### Requirement: Orchestrator command handling in the worker

`Stop` SHALL cancel the agent process (whole tree kill) — the run reports `cancelled`. `InjectContext` SHALL append the context to `comuki-injected-context.md` in the working directory. `LeaseExpired` SHALL cancel pi AND mark ownership gone so the loop will not complete/fail the item.

#### Scenario: Soft stop
- **WHEN** the orchestrator sends Stop with a reason
- **THEN** the agent process tree is killed and the StageReport status is `cancelled`

### Requirement: Agent invocation and stream parsing

The Translator SHALL spawn the configured executable (`pi` in production, a fake in tests) with `-p <brief> --mode json --no-session`, streaming stdout line by line. The stream-json parser SHALL be tolerant: blank lines yield nothing; malformed JSON yields an `unparseable` event; unmodelled event types yield an `unknown` event preserving the raw JSON; a single bad line never kills a running task. Modelled events: `system`, `user`, `assistant` (text / tool_use blocks), `result`, plus the pi-native `session` header, `message_update` (text_delta / toolcall_start), `message_end` and `tool_execution_start` and `agent_end`. Session headers, results and unmodelled events are not forwarded as Activity.

#### Scenario: One garbage line survives
- **WHEN** the agent emits a non-JSON line mid-stream
- **THEN** the run continues and the line surfaces as an unparseable event

### Requirement: Translator environment contract

The worker container's `COMUKI_*` environment SHALL map onto the Translator configuration: `COMUKI_ORCH_HTTP` (REST base URL), `COMUKI_ORCH_GRPC` (gRPC URL), `COMUKI_WORKER_TOKEN`, `COMUKI_PROFILE_KEY`, `COMUKI_PROFILES_REF`, `COMUKI_WORKER_IMAGE`, `COMUKI_PROFILES_PATH`, `COMUKI_PROFILES_GIT_URL`, `COMUKI_PI_EXECUTABLE`, `COMUKI_WORKING_DIRECTORY`. Options validate on start — a missing orchestrator URL or token fails the boot.

#### Scenario: Container env becomes config
- **WHEN** the compute provider stamps `COMUKI_WORKER_TOKEN` and `COMUKI_ORCH_GRPC` on the container
- **THEN** the Translator authenticates and connects without further configuration

### Requirement: Worker container image

The worker image SHALL be multi-stage: an SDK stage compiles and publishes the Translator (framework-dependent linux-x64 Release); the final stage is `oven/bun:1.4.0-slim` with the pi coding agent installed globally via bun, the .NET 10 runtime (no SDK) via the install script, invariant globalization, `git`/`curl`/CA certificates, and the translator as ENTRYPOINT with a `/work` volume. Image defaults set `COMUKI_ORCH_HTTP`/`COMUKI_ORCH_GRPC` to the compose-network orchestrator and `COMUKI_PI_EXECUTABLE=pi`; the rest of the env contract is stamped at container start. The image requires bun >= 1.4 (older pi crashes on 1.3.x).

#### Scenario: Sanity-run the image
- **WHEN** the image runs with entrypoint overridden to `pi --version`
- **THEN** it reports a version without any orchestrator configured

### Requirement: TestFakePi harness

The test fake SHALL mimic the agent CLI contract: it emits the pi-native json event stream — a `session` header, then each `Fixtures/*.json` in ordinal order, then `agent_end` — one JSON object per line, and exits 0. `--fixtures-dir=PATH` (forwarded via the prompt args) selects a custom stream; `--exit-code=N` forces the failure path. A missing fixtures dir is a non-zero exit with a stderr note.

#### Scenario: Forced failure exit
- **WHEN** the integration test spawns the fake with `--exit-code=3`
- **THEN** the Translator's outcome is `failed` and the item is failed with the exit code in the reason

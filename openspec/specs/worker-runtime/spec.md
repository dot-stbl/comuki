# Worker Runtime Specification

## Purpose

Defines how ephemeral workers execute work items: the bidirectional gRPC stream between the worker container (Translator) and the orchestrator, the worker REST claim/heartbeat/complete/fail surface, the Translator's claim → pi spawn → stream → report → complete loop, its environment contract, the worker container image, and the TestFakePi test harness.

## MODIFIED Requirements

### Requirement: Agent invocation and stream parsing

The Translator SHALL spawn the configured executable (`pi` in production, a fake in tests) with `-p <brief> --mode json` (no `--no-session` — pi runs in **session mode** with the in-process session transport; `TurnInput` commands arrive over the bidi channel), streaming stdout line by line. The stream-json parser SHALL be tolerant: blank lines yield nothing; malformed JSON yields an `unparseable` event; unmodelled event types yield an `unknown` event preserving the raw JSON; a single bad line never kills a running task. Modelled events: `system`, `user`, `assistant` (text / tool_use blocks), `result`, plus the pi-native `session` header, `message_update` (text_delta / toolcall_start), `message_end` and `tool_execution_start` and `agent_end`. Session headers, results and unmodelled events are not forwarded as Activity.

> **Coordination note (2026-10-04).** This change replaces the v1.x `pi -p <brief> --mode json --no-session` invocation with session-mode pi (`pi -p <brief> --mode json`). The `--no-session` flag is removed — the in-process session transport carries `TurnInput` commands over the bidi channel (`session` capability, Phase 1 of `add-orchestra`). `TranslatorOptions` carries no session flag; the decision lives in `IHarness.Capabilities.LiveSession` (`harness-spi` capability, Phase 8).

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

### Requirement: Orchestrator command handling in the worker

`Stop` SHALL cancel the agent process (whole tree kill) — the run reports `cancelled`. `InjectContext` SHALL append the context to `comuki-injected-context.md` in the working directory. `LeaseExpired` SHALL cancel pi AND mark ownership gone so the loop will not complete/fail the item.

#### Scenario: Soft stop
- **WHEN** the orchestrator sends Stop with a reason
- **THEN** the agent process tree is killed and the StageReport status is `cancelled`

#### Scenario: One garbage line survives
- **WHEN** the agent emits a non-JSON line mid-stream
- **THEN** the run continues and the line surfaces as an unparseable event

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

### Requirement: TestFakePi harness

The test fake SHALL mimic the agent CLI contract: it emits the pi-native json event stream — a `session` header, then each `Fixtures/*.json` in ordinal order, then `agent_end` — one JSON object per line, and exits 0. `--fixtures-dir=PATH` (forwarded via the prompt args) selects a custom stream; `--exit-code=N` forces the failure path. A missing fixtures dir is a non-zero exit with a stderr note.

#### Scenario: Forced failure exit
- **WHEN** the integration test spawns the fake with `--exit-code=3`
- **THEN** the Translator's outcome is `failed` and the item is failed with the exit code in the reason

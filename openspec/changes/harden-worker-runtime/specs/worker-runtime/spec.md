## MODIFIED Requirements

### Requirement: Agent invocation and stream parsing

The Translator SHALL spawn the configured executable (`pi` in production, a fake in tests) with `pi --mode rpc --no-session` (the v2 session-mode invocation per `add-orchestra` §1.3.1 — JSON-RPC over stdin/stdout; `--no-session` turns persistence off so the process is ephemeral), streaming stdout line by line. Stream-json event parsing (deltas, authoritative assistant text, tool invocations) remains the wire contract; the harness parses each `stream-json` line into the canonical event record. The **Translator SHALL track `last_event_age` — the time since the last parsed stream-event** — and reset it on every legal event (`text_delta`, `tool_use`, `tool_result`, `StageStart`, `StageReport`, `agent_end`, `system`, `user`, `message_end`, `tool_execution_start`). `last_event_age` SHALL be the input to the `WorkerProgressWatchdog` (separate from heartbeat-side liveness). Stream-json lines longer than `TranslatorOptions.MaxLineLengthBytes` (default 1 MB; `0` = unlimited, non-production) SHALL be dropped with a `line_too_long` parse-error counter increment; the worker SHALL survive a single bad line. The `WorkerCommandHub.TrySendStopAsync` escalation path (D1.1.3) is invoked when `last_event_age > WorkerProgressTimeout`; the worker SHALL also handle a graceful `Stop` from the existing bidi command channel without exiting the entry-point check.

> **Coordination note (2026-10-07).** `last_event_age` is the *progress*
> timer — distinct from `heartbeat` (REST `POST /workers/{workItemId}/heartbeat`), which is the *liveness* timer. The architectural reference is `.agents/docs/architecture/comuki-architecture.md:416` (the two-timer decision). Heartbeat continues to be exercised; `last_event_age` is *additional*, not *replacing*. `agent-runtime-capabilities` Phase D, `harden-pi-worker-sandbox`, and the v1.x wire are unchanged here.

#### Scenario: Translator resets progress on every legal event

- **WHEN** the harness parses a stream-event of any legal type
- **THEN** `last_event_age` is reset to zero and the `WorkerProgressWatchdog` timer is restarted

#### Scenario: Long line is dropped, worker survives

- **WHEN** pi emits a stream-json line longer than `TranslatorOptions.MaxLineLengthBytes`
- **THEN** the line is dropped, `parse_errors_total{kind = line_too_long}` increments, and the worker continues processing the next line

#### Scenario: Stall triggers escalation

- **WHEN** `last_event_age` exceeds `TranslatorOptions.WorkerProgressTimeout` with no legal event parsed
- **THEN** the `WorkerProgressWatchdog` journals `worker.stall_warn{worker_id, last_event_age_ms}` and escalates per `WorkerProgressWheelPolicy` (warn → gentle-kill → fail-item)

#### Scenario: Worker emits no events for the duration of the budget

- **WHEN** the harness parses zero events for `TranslatorOptions.TurnBudget`
- **THEN** `DeadlinePolicy` cancels the cycle (tier 2 gentle-kill); if `RunBudget` is also exceeded after `red_pattern = 3` consecutive turn-breaches, the WorkItem is failed with reason `worker.stall_detected`

#### Scenario: pi exits before StageReport

- **WHEN** the pi process exits with a non-zero `ExitCode` and no final `StageReport` event was parsed
- **THEN** the Translator journals `worker.exit_code{worker_id, exit_code, stderr_safe}` and increments `parse_errors_total{kind = exit_no_stage_report}`; the Translator **continues to wait for `StageReport`** (exit-code is visible, not authoritative — the full exit-code-as-authority fix is a separate bug-batch)

#### Scenario: One garbage line survives

- **WHEN** the agent emits a non-JSON line mid-stream
- **THEN** the run continues and the line surfaces as an unparseable event

### Requirement: Stream semantics — end on events completion

One stream per connection, bound to one WorkerId. The call SHALL end when the worker completes its events enumeration (the worker's "I'm done" signal); commands flow orchestrator → worker only while events are still coming. When the events side finishes, the command pump is cancelled — no pending `MoveNextAsync` is left for dispose. A worker dropping the stream mid-events is an expected close path, not a fault; real event-pump faults propagate.

#### Scenario: Report then complete ends the call

- **WHEN** the worker finishes its event stream after sending the final StageReport
- **THEN** the server's command loop ends and the stream closes cleanly

#### Scenario: One stream per WorkerId under bounded backpressure

- **WHEN** the harness emits events faster than the consumer drains
- **THEN** the bounded `Channel<WorkerEvent>` (default 1024) drops progress-fragments (`text_delta`) on the drop-oldest policy and queues mandatory events (`StageStart`, `StageReport`, `agent_end`) without drop; each drop journals `worker.events_dropped{kind = progress}` and increments `events_dropped_total{kind = progress}`

### Requirement: Orchestrator command handling in the worker

`Stop`, `InjectContext`, `LeaseExpired` SHALL target one WorkerId. `Stop` SHALL cancel the agent process (whole tree kill) — the run reports `cancelled`. `InjectContext` SHALL append the context to `comuki-injected-context.md` in the working directory. `LeaseExpired` SHALL cancel pi AND mark ownership gone so the loop will not complete/fail the item. The Translator SHALL **distinguish between the WorkerCommandHub's `Stop` (tier 2 gentle-kill, fired by the escalation path) and the existing reaper's `LeaseExpired`** — both reach the worker through the same bidi channel; the existing reaper wiring is preserved verbatim.

#### Scenario: Soft stop

- **WHEN** the orchestrator sends `Stop` for one WorkerId with a reason
- **THEN** only that worker's agent process tree is cancelled and its report is `cancelled`

#### Scenario: Escalation tier 2 sends Stop through WorkerCommandHub

- **WHEN** `WorkerProgressWatchdog` escalates past tier 1 (`stall_warn`) to tier 2 (`gentle_kill`)
- **THEN** the existing `WorkerCommandHub.TrySendStopAsync(WorkerId, reason)` is called; the existing channel carries the command exactly as the reaper's kill switch does today

#### Scenario: One garbage line survives

- **WHEN** the agent emits a non-JSON line mid-stream
- **THEN** the run continues and the line surfaces as an unparseable event

### Requirement: Worker REST surface

The worker REST API SHALL expose, all authenticated by the worker token in the `Authorization` header (401 `worker.unauthenticated` ProblemDetails otherwise):

- `POST /workers/claim` — body: **envClass**, profilesRef, profileKey; 200 with the claimed item (workItemId, runId, profileKey, envClass, brief, leaseUntil unix-ms, attempt, generation) or 204 when the queue has nothing; 400 on validation failure
- `POST /workers/{workItemId}/heartbeat` — extends the lease; body carries the claimed generation; 204 held, 409 `work-item.not-owner` when the item is unknown, not running, leased to another worker, or leased at a stale generation (the Run was cancelled or superseded since claim). The heartbeat response **SHALL count toward `heartbeat_outcome{status}` counter** (status = `ok | not_owner | stale_generation`)
- `POST /workers/{workItemId}/complete` — body: non-empty result JSON plus the claimed generation; 204 / 409 ownership-or-generation
- `POST /workers/{workItemId}/fail` — body: non-empty reason plus the claimed generation; 204 / 409 ownership-or-generation

The worker id the queue sees is the one the token was issued for — ownership is token-derived, never claimed. A generation mismatch answers the same 409 `work-item.not-owner` code as an ownership miss — from the worker's perspective, losing a stale generation and losing a lease to another worker are the same "you no longer hold this" outcome; this change does not introduce a second 409 code.

#### Scenario: Heartbeat outcome is observed in telemetry

- **WHEN** the heartbeat endpoint resolves a claim
- **THEN** `heartbeat_outcome{status = ok}` increments; when the heartbeat returns 409, the counter increments under `status = not_owner` or `status = stale_generation` accordingly

#### Scenario: Ownership miss is 409, not 500

- **WHEN** a worker completes an item the reaper already requeued
- **THEN** the answer is 409 with code `work-item.not-owner`

#### Scenario: Stale generation after cancellation is rejected

- **WHEN** a worker completes an item using the generation it claimed under, after the owning Run was cancelled or superseded (generation bumped)
- **THEN** the answer is 409 with code `work-item.not-owner` and the WorkItem/Run outcome is unaffected by the late completion

#### Scenario: Current generation still succeeds

- **WHEN** a worker completes an item using the Run's current generation
- **THEN** the completion is accepted exactly as before this change

#### Scenario: Claim body without envClass is 400

- **WHEN** a worker posts `/workers/claim` with profileKey and profilesRef but no envClass
- **THEN** the answer is 400

### Requirement: Translator loop

The worker's outer loop SHALL run claim → execute → report → repeat until the process stops; an empty claim waits the poll interval (default 10 seconds) before retrying. One cycle:

1. claim over REST (empty → return)
2. prepare the working directory: clone the target repository, then run the accepted `[restore]` opcodes from `.comuki/environment.toml` at that ref (host-side, not the coding agent); then prepare profiles material under `profiles/` (copy from the mounted path when configured, else shallow-clone the public git URL at the pinned ref, else warn and skip). Restore failure fails the item and skips spawn
3. open the gRPC session, send StageStart, **extract `traceparent` from the claim metadata and propagate it as `Activity.Current.TraceId` for the Translator-side `Activity` chain**
4. spawn the agent executable through the harness abstraction (`IHarness.StartAsync`, `harness-spi` capability per `add-orchestra` §8) and pump its stream-json output into a **bounded `Channel<WorkerEvent>` (default 1024) with drop-oldest policy on progress-fragments and no-drop on mandatory events**, forwarding text deltas / authoritative assistant text / tool invocations as Activity events while:
   - a **progress watchdog** tracks `last_event_age` and escalates (`stall_warn` → `gentle_kill` → `fail-item`) when `last_event_age > TranslatorOptions.WorkerProgressTimeout`
   - a **deadline policy** enforces `TranslatorOptions.TurnBudget` (per cycle) and `RunBudget` (per worker process); a `RunBudget` breach after `red_pattern = 3` consecutive turn-budget breaches fails the item
   - a heartbeat task extends the lease every interval (default 30 seconds) and a command task consumes orchestrator commands. **A heartbeat failure (non-2xx response, or an exception from the REST call — Polly timeout, `HttpRequestException`, orchestrator 5xx) SHALL be treated as lease-lost semantics, not as a fatal host failure.** The TranslatorLoop's existing "lease-lost skips completion" path takes over: the in-flight item is failed (not skip-completed, since the run was on the wire), the worker does not authoritatively own the item, and the host process keeps running
   - on a pi process exit without `StageReport`, the Translator journals `worker.exit_code{worker_id, exit_code, stderr_safe}` and increments `parse_errors_total{kind = exit_no_stage_report}` (visibility, not authority — full exit-code-as-authority fix is a separate bug-batch)
5. send the final StageReport, close the session
6. if the lease was lost (rejected heartbeat or a `LeaseExpired` command, or a heartbeat call that threw — see the heartbeat-failure clause on step 4): skip completion entirely — the reaper owns the item
7. else complete on `success` (result JSON = the serialized StageReport) or fail with a `status: error-text` reason otherwise

Failures propagate and stop the host, **except for heartbeat failures (a non-2xx heartbeat response, or an exception from the heartbeat REST call — Polly timeout, `HttpRequestException`, orchestrator 5xx), which are scoped to lease-lost semantics**: the in-flight item is failed, the host process keeps running. An ephemeral worker is meant to die and be replaced, not limp along.

#### Scenario: traceparent propagates from claim

- **WHEN** a claim body carries `traceparent: 00-<trace_id>-<span_id>-01` and the Translator opens the first `Activity` for the cycle
- **THEN** `Activity.Current.TraceId == trace_id`

#### Scenario: Progress watchdog resets on every event

- **WHEN** the harness parses a legal stream-event of any type
- **THEN** `last_event_age_ms` resets to zero and `WorkerProgressWatchdog` is restarted

#### Scenario: Bounded channel drops oldest progress-fragments

- **WHEN** the channel has reached its capacity (1024) and a new `text_delta` event arrives
- **THEN** the oldest `text_delta` is dropped, `events_dropped_total{kind = progress}` increments, and the channel accepts the new event

#### Scenario: Bounded channel queues mandatory events without drop

- **WHEN** the channel has reached its capacity (1024) and a `StageReport` event arrives
- **THEN** the channel writer awaits the consumer; no drop occurs; `events_dropped_total` does not increment

#### Scenario: Long line is dropped

- **WHEN** pi emits a stream-json line longer than `TranslatorOptions.MaxLineLengthBytes`
- **THEN** the line is dropped, `parse_errors_total{kind = line_too_long}` increments, and the worker continues

#### Scenario: pi exits without StageReport

- **WHEN** the pi process exits with a non-zero `ExitCode` and no final `StageReport` event was parsed
- **THEN** `worker.exit_code{worker_id, exit_code, stderr_safe}` is journaled and `parse_errors_total{kind = exit_no_stage_report}` increments; the worker **continues to wait for `StageReport`** (the Translator does not authoritatively fail the item on exit-code alone)

#### Scenario: Non-zero pi exit fails the item

- **WHEN** a spawned pi process exits non-zero
- **THEN** that worker reports failed with the exit code and safe stderr detail

#### Scenario: Result text is authoritative

- **WHEN** the agent streams deltas and later emits authoritative final assistant text
- **THEN** the worker's authoritative-text rule replaces accumulated deltas with the final wording

#### Scenario: Restore runs before pi

- **WHEN** the claimed item's class declares `[restore] dotnet = "comuki.slnx"`
- **THEN** `dotnet restore comuki.slnx` completes before pi is started

#### Scenario: Lease lost mid-run

- **WHEN** a heartbeat is rejected while the agent still runs
- **THEN** that agent is cancelled and the worker does not complete or fail the item authoritatively

#### Scenario: Heartbeat call throws — lease-lost, not host-stop

- **WHEN** the heartbeat REST call throws (Polly timeout, `HttpRequestException`, orchestrator 5xx)
- **THEN** the heartbeat task returns `false` (lease-lost), the run is failed (not skip-completed, since the run was on the wire), the host process keeps running, and the exception is logged for the operator to investigate; the next claim cycle begins normally

## ADDED Requirements

### Requirement: Contract-handshake declares protocol version and capabilities

The worker's `Connect` request SHALL carry a `WorkerHandshake` payload:

- `protocol_version: string` (semver MAJOR.MINOR.PATCH; e.g. `"1.0.0"`)
- `capabilities: repeated string` (the set of capability-flags the worker declares, e.g. `["session.live", "rpc.steer"]`)

The host's `HandshakeValidator` SHALL compare `protocol_version` against the **expected** version:

- **MAJOR mismatch** → reject `Connect` with code `worker.skew_major`; the worker SHALL NOT connect.
- **MINOR mismatch** → accept with `capabilities_compat` filter (only capabilities compatible with the expected minor version are honored); journal `worker.skew_detected{worker_id, expected_version, actual_version}` and increment `skew_detected_total{expected_version}`.
- **Unknown capability flag** in `capabilities[]` → ignore; increment `unknown_command_total{cmd = <flag>}`.

Backward-compat: a v1.x worker without a `WorkerHandshake` payload SHALL be accepted in compat-mode (host applies `default_capabilities = []`); the worker-sdk minor version is bumped alongside this change.

#### Scenario: Major-mismatch rejected

- **WHEN** a `Connect` carries `protocol_version = "2.0.0"` and the host expects `"1.0.0"`
- **THEN** `Connect` is rejected with code `worker.skew_major`; the worker is not connected; `skew_detected_total{expected_version = "1.0.0"}` increments

#### Scenario: Minor-mismatch accepted with filter

- **WHEN** a `Connect` carries `protocol_version = "1.1.0"` and the host expects `"1.0.0"`, with capabilities `["session.live", "rpc.unstable_feature"]`
- **THEN** the worker connects; `capabilities_compat = ["session.live"]` (the unstable capability is filtered); `worker.skew_detected` is journaled with `expected_version = "1.0.0"` and `actual_version = "1.1.0"`; `skew_detected_total{expected_version = "1.0.0"}` increments

#### Scenario: Unknown capability ignored

- **WHEN** a `Connect` carries `protocol_version = "1.0.0"` and capabilities `["session.live", "made_up_capability"]`
- **THEN** the worker connects; `made_up_capability` is ignored; `unknown_command_total{cmd = "made_up_capability"}` increments

#### Scenario: v1.x worker without handshake works in compat-mode

- **WHEN** a `Connect` carries no `WorkerHandshake` payload (legacy v1.x worker)
- **THEN** the host applies `default_capabilities = []` and accepts the connection; no skew or unknown-capability event is journaled

### Requirement: Last-event-age gauge drives the progress watchdog

The Translator SHALL publish a gauge `comuki.worker.last_event_age_ms{worker_id}` that monotonically increases from the time of the last legal parsed event and resets to zero on the next legal event. The gauge SHALL be the input to the `WorkerProgressWatchdog` (per `TranslatorOptions.WorkerProgressTimeout`, default 60s, range 5s–1h). The gauge SHALL be emitted on the same `Meter = "comuki.worker"` resource as the rest of the worker-side metrics (per `observability/diagnostics.md` §3).

#### Scenario: Gauge reset on legal event

- **WHEN** the harness parses a stream-event of any legal type
- **THEN** `comuki.worker.last_event_age_ms{worker_id}` resets to zero on the next emission

#### Scenario: Gauge monotonically increases during silence

- **WHEN** the harness parses no events for `WorkerProgressTimeout`
- **THEN** the gauge value on each scrape reflects the elapsed time since the last event (≥ `WorkerProgressTimeout` triggers `WorkerProgressWatchdog` escalation per the T2 starter)

### Requirement: Worker telemetry emits nine counters through the host OTLP endpoint

The Translator SHALL emit nine worker-side metrics on the same OTLP endpoint and resource as the host (`ComukiTelemetryInstaller`, master after `add-orchestra` phase 2). The metrics use the `comuki.worker.*` prefix per `observability/diagnostics.md` §3:

- `comuki.worker.last_event_age_ms` (gauge; `worker_id`)
- `comuki.worker.events_total` (counter; `worker_id`, `type`)
- `comuki.worker.parse_errors_total` (counter; `worker_id`, `kind` ∈ `{"invalid_json", "line_too_long", "exit_no_stage_report"}`)
- `comuki.worker.stdin_commands_total` (counter; `worker_id`, `cmd`)
- `comuki.worker.heartbeat_outcome` (counter; `worker_id`, `status` ∈ `{"ok", "not_owner", "stale_generation"}`)
- `comuki.worker.events_per_sec` (histogram; `worker_id`)
- `comuki.worker.skew_detected_total` (counter; `expected_version`)
- `comuki.worker.unknown_command_total` (counter; `cmd`)
- `comuki.worker.events_dropped_total` (counter; `kind` ∈ `{"progress"}`)

All tags SHALL be dot.case and bounded-cardinality per `observability/diagnostics.md` §4. The Translator's MEL log SHALL also flow through the same OTLP exporter (`builder.Logging.AddOpenTelemetry(o => o.AddOtlpExporter())`) on the existing `Comuki.Translator.Runtime` `ActivitySource`.

#### Scenario: All nine metrics emit on the same resource

- **WHEN** the Translator runs with `OTEL_EXPORTER_OTLP_ENDPOINT=http://victoria:4318`
- **THEN** VictoriaMetrics receives all nine `comuki.worker.*` metrics with the same `service.name` resource as the host-side metrics (single exporter, single resource per `observability/diagnostics.md`)

#### Scenario: Tag cardinality is bounded

- **WHEN** a metric emits with a tag
- **THEN** the tag value comes from the closed set documented above (`worker_id`, `type`, `kind`, `cmd`, `status`, `expected_version`); no unbounded tags (no run ids, no user identifiers, no raw query text)

### Requirement: WorkerCommandHub persists commands to PG LISTEN/NOTIFY

The `WorkerCommandHub` SHALL write commands (`Stop`, `InjectContext`, `LeaseExpired`, `TurnInput`, future command variants) through a `comuki_cmdhub` PG table + LISTEN channel `comuki.cmdhub`. The schema:

```sql
CREATE TABLE comuki_cmdhub (
  id           bigserial PRIMARY KEY,
  worker_id    text NOT NULL,
  cmd          text NOT NULL,
  payload      jsonb NOT NULL,
  created_at   timestamptz NOT NULL DEFAULT now(),
  claimed_by   text NULL,
  claimed_at   timestamptz NULL
);
CREATE INDEX comuki_cmdhub_worker_unclaimed
  ON comuki_cmdhub (worker_id) WHERE claimed_by IS NULL;
```

A trigger SHALL fire `NOTIFY comuki.cmdhub` on every `INSERT`. The reader side SHALL `LISTEN comuki.cmdhub` and consume commands through `SELECT FOR UPDATE SKIP LOCKED LIMIT 1`. A polling fallback (every 5s, `SELECT WHERE claimed_by IS NULL LIMIT 1`) SHALL catch the case where LISTEN misses (network blip, pg restart). The in-memory pipe (`IWorkerCommandPipe`) SHALL remain as the consumer-side optimisation but SHALL no longer be the source of truth.

The mutable virtual-key store (`IVirtualKeyStore` tunings, per `add-orchestra` §7) SHALL similarly persist through a PG table `proxy_virtual_keys_tunings` with a 1-second read-through in-memory cache; the existing `ConfigurationVirtualKeyStore` (config-seed) stays.

> **Single-replica constraint remains.** A second replica SHALL now read the same commands as the first (no silent loss), but claim-fencing between replicas is **out of scope here** and is filed as the next change. The `comuki.orchestra.single_replica` health check (per `add-orchestra` §10.7) remains the production gate; deployments with `replicas: 2` go Unhealthy and the runbook records the constraint.

#### Scenario: Two replicas observe the same command

- **WHEN** replica A inserts a `Stop` command into `comuki_cmdhub` and replica B is `LISTEN comuki.cmdhub`
- **THEN** replica B receives the command within 100 ms via LISTEN; the polling fallback picks it up within 5 s if LISTEN misses (network blip)

#### Scenario: Polling fallback after LISTEN miss

- **WHEN** a worker LISTEN session is killed between INSERT and NOTIFY (simulated by `pg_terminate_backend` on the LISTEN connection)
- **THEN** the polling fallback within 5 s sees the unclaimed row and processes it; `claimed_by = <replica_hostname>` after the claim

#### Scenario: Mutable virtual key tunings are PG-backed

- **WHEN** replica A applies a tuning write to `proxy_virtual_keys_tunings` and replica B reads through the read-through cache
- **THEN** replica B observes the new value within 1 s (cache TTL); the write is durable across replica restarts

## ADAPTER Notes

The `WorkerCommandHub` and the mutable virtual-key store are migrated from in-memory to PG in Phase 6 (`design.md` §D6); the in-memory pipe stays as a consumer-side optimisation but is no longer the source of truth. The single-replica health check `comuki.orchestra.single_replica` (per `add-orchestra` §10.7) is documented as the production gate until the next change introduces PG-claim-fencing.

Worker telemetry uses the same OTLP endpoint as the host (`ComukiTelemetryInstaller`, master after `add-orchestra` phase 2). One exporter, one resource — no second pipeline. The worker-side `ActivitySource = "Comuki.Translator.Runtime"` is added on the existing resource.

The v1.x wire (`pi -p BRIEF --mode json --no-session`) was already replaced by `pi --mode rpc --no-session` in `add-orchestra` §1.3.1; this change does not re-introduce the v1.x wire or alter the v2 wire format. The `WorkerHandshake` payload (Phase 4) is **metadata** on `Connect` — it does not replace the stream-json event format.
## Phase order

Семь фаз; каждая — вертикальный change со своим gate. Фазы грубо
упорядочены по зависимостям (liveness → telemetry → backpressure →
handshake → promotion → multi-replica), но поздние фазы не блокируют
ранние; каждая shippable behind feature flag.

```text
1. Liveness + deadlines   ── (independent)
2. Worker telemetry       ── (independent)
3. Backpressure            ── (independent)
4. Contract-handshake      ── (independent; wire-change)
5. TrustClass promotion   ── (independent of 1-4)
6. Multi-replica hub      ── (independent of 1-5; PG schema migration)
7. Cross-phase gate        ── (after 1-6)
```

Wave split: фазы 1-3 — первая волна (волна-1 worker-runtime hardening);
фазы 4-6 — вторая (wire + persistence); фаза 7 — гейт.

## 1. Phase — Liveness two-timer + wall-clock deadlines + exit-code baseline

Translator различает «контейнер жив» (heartbeat REST) и «pi продвигается»
(last event age). Stall на progress-таймере дольше порога → escalation
tiers (warn → gentle-kill → fail item). Wall-clock budget'ы (turn + run)
ограничивают run по времени. Базовое чтение pi `ExitCode` (фаза 1D1) —
видимо в журнале, не полномочно.

> **Wave 1 note.** Базовая ветка (фаза 1) делает exit-code *видимым*
> (`worker.exit_code` journal event), не *авторитетным* (Translator
> всё ещё ждёт `StageReport`). Полная ветка «exit-code → fail by code»
> — отдельный bug-батч (задевает concubine run state machine).
> См. `design.md` §D5 и §`One-Legacy Right`.

### 1.1 Phase 1A — progress watchdog + escalation

- [ ] 1.1.1 Add `TranslatorOptions.WorkerProgressTimeout` (default 60s, range 5s–1h) and `WorkerProgressEscalationPolicy` (typed: `Warn | GentleKill | FailItem`). Bind via `ValidateDataAnnotations().ValidateOnStart()` per canon `di-options.md`. Add `TranslatorOptions.ToolCallTimeout` (default 5min) — per-call budget that resets on each parsed event, separate from `WorkerProgressTimeout` (the worker-progress watchdog).
  - Verify: a unit test with a fake harness that emits no events → `WorkerProgressWatchdog` raises `stall_warn` after `WorkerProgressTimeout`, then escalates to `gentle_kill` after the second interval if configured, and `fail_item` after the third. `dotnet run --project tests/unit/Comuki.Host.Translator.Unit.Runtime` exits 0.

- [ ] 1.1.2 Implement `WorkerProgressWatchdog` in `platform/src/host/Comuki.Host.Translator/Runtime/ProgressWatchdog.cs`: a single timer per Translator instance, reset on every parsed stream-event (text delta, tool_use, tool_result, StageStart, StageReport). Timer fires `WorkerProgressTimeout` after last reset → journal `worker.stall_warn{worker_id, last_event_age_ms}` with structured payload. Escalation is policy-driven (1.1.1).
  - Verify: a unit test with a synthetic event stream (no events for 60s) emits exactly one `worker.stall_warn` per cycle; a stream with regular events (`text_delta` every 5s) emits zero `stall_warn`. `dotnet run --project tests/unit/Comuki.Host.Translator.Unit.Runtime` exits 0.

- [ ] 1.1.3 Wire the escalation to the existing `WorkerCommandHub.TrySendStopAsync` (the `Stop` command variant; tier 2) and to the lease-side fail path (tier 3 — fail the WorkItem with reason `worker.stall_detected` + last_event_age). Tier 1 (warn) is log-only; tier 2/3 are documented in spec delta.
  - Verify: a unit test that escalates from warn → gentle-kill sees the `Stop` command on the bidi channel within one tick. A unit test that escalates to fail-item sees the `complete` call with the typed reason. `dotnet run --project tests/unit/Comuki.Host.Translator.Unit.Runtime` exits 0.

### 1.2 Phase 1B — wall-clock deadlines (turn + run)

- [ ] 1.2.1 Add `TranslatorOptions.TurnBudget` (default 60min) and `RunBudget` (default 480min), each with `[Range]` per canon. ValidateOnStart. Per-profile override (`control-plane/profiles/<name>.md` `turn_budget_min` / `run_budget_min` frontmatter) — read at claim time, validated against `TranslatorOptions` ceiling.
  - Verify: a unit test with `TurnBudget = 30min` and a harness that never finishes → Translator cancels at `60min` (default ceiling). A profile test with `turn_budget_min = 5` honored up to the `TurnBudget` ceiling.

- [ ] 1.2.2 Implement `DeadlinePolicy` (`platform/src/host/Comuki.Host.Translator/Runtime/DeadlinePolicy.cs`): two timers per cycle (turn-budget since spawn; run-budget since worker process start). On breach → same escalation path as 1.1.3 (tier 2 gentle-kill for turn; tier 3 fail-item for run after 3 consecutive turn-breaches).
  - Verify: a unit test that simulates 3 consecutive turn-budget breaches → `fail_item`. A unit test that simulates 1 turn-budget breach with restart → restart succeeds. A unit test that simulates run-budget breach → fail-item.

### 1.3 Phase 1C — exit-code baseline (visible, not authoritative)

- [ ] 1.3.1 Add a process-exit handler in `TranslatorLoop` (already on master at `platform/src/host/Comuki.Host.Translator/Execution/TranslatorLoop.cs:93-115`): on `pi` process exit with no final StageReport, record `worker.exit_code{worker_id, exit_code, stderr_safe}` journal event and increment `parse_errors_total{kind = exit_no_stage_report}` counter. **Translator still waits for `StageReport`; this is visibility, not authority.**
  - Verify: a unit test that runs a TestFakeHarness with `--exit-code=2` and no final StageReport → the journal carries `worker.exit_code{exit_code = 2}` and the counter increments. A unit test that runs with `--exit-code=0` and a `StageReport{success}` → the journal is silent (no spurious `worker.exit_code`). `dotnet run --project tests/unit/Comuki.Host.Translator.Unit.Pi` exits 0.

### 1.4 Cross-phase gate (Phase 1)

- [ ] 1.4.1 `dotnet build comuki.slnx -c Debug` exits 0; `dotnet run --project tests/unit/Comuki.Host.Translator.Unit.Runtime` exits 0; `dotnet run --project tests/unit/Comuki.Host.Translator.Unit.Pi` exits 0.

## 2. Phase — Worker telemetry (OTel)

Translator-side OTel: 9 метрик (D3 таблица в `design.md`), bounded
cardinality, через тот же OTLP endpoint, что host (`add-orchestra`
phase 2). MEL лог Translator'а через тот же exporter. W3C trace id —
из claim body.

- [ ] 2.1 Extend `ComukiTelemetryInstaller` (under `platform/src/host/Comuki.Host/Observability/Installers/`) with the **worker-side** OTel exporter: a second `OpenTelemetryBuilder` chained on the existing resource (the existing host-side `MeterFactory` / `ActivitySourceFactory` is shared; the worker-runtime adds its own `ActivitySource = "Comuki.Translator.Runtime"` and `Meter = "comuki.worker"` on the same resource, same exporter). One OTLP endpoint, one resource — no second pipeline.
  - Verify: a manual run with `OTEL_EXPORTER_OTLP_ENDPOINT=http://victoria:4318` produces worker-side metrics in VictoriaMetrics within one scrape interval; metrics carry `comuki.worker.*` prefix per `observability/diagnostics.md` §3.

- [ ] 2.2 Add the 9 metrics from `design.md` §D3 (`last_event_age_ms` gauge; `events_total`, `parse_errors_total`, `stdin_commands_total`, `heartbeat_outcome`, `skew_detected_total`, `unknown_command_total`, `events_dropped_total` counters; `events_per_sec` histogram). Tags follow `observability/diagnostics.md` §4: dot.case, bounded cardinality (`worker_id`, `type`, `cmd`, `status`, `expected_version`, `kind`); no PII; no unbounded tags.
  - Verify: a unit test that emits one event per second for 10s and asserts `events_total{type = text_delta} = 10` and `events_per_sec` populates. A unit test with a malformed JSON line asserts `parse_errors_total{kind = invalid_json}` increments. `dotnet run --project tests/unit/Comuki.Host.Translator.Unit.Telemetry` exits 0.

- [ ] 2.3 Wire `traceparent` propagation: at claim body parse time (`ClaimSourceGitResolver.cs` (and `Host.Translator/Claim/`)), extract `traceparent` from the worker-side metadata; pass it as `Activity.Current.TraceId` for every subsequent Translator-side `Activity`. The existing `ActivitySource` in `ComukiTelemetryInstaller` already propagates W3C; this phase wires the *input* end.
  - Verify: a unit test with a claim body `traceparent: 00-{trace_id}-{span_id}-01` asserts that the next `Activity` opened in Translator carries `Activity.Current.TraceId == trace_id`. A unit test without `traceparent` carries the existing fallback (translator-local trace).

## 3. Phase — Backpressure (bounded events-канал + ReadLine cap)

- [ ] 3.1 Replace the existing unbounded events loop in `TranslatorLoop` with `Channel<WorkerEvent>` bounded (default 1024). Policy: progress-fragments (text deltas) — drop-oldest; mandatory events (`StageStart`, `StageReport`, `agent_end`) — без drop'а: writer awaits consumer. Both policies journaled: drop → `worker.events_dropped{kind = progress}` + `events_dropped_total{kind = progress}` counter.
  - Verify: a unit test that fills the channel with 1024 text-deltas then emits one StageReport — channel drops 1024 deltas, accepts the StageReport, journals exactly 1 `events_dropped`. A unit test with the channel full and a StageReport incoming — StageReport is queued and consumed; no spurious drop. `dotnet run --project tests/unit/Comuki.Host.Translator.Unit.Runtime` exits 0.

- [ ] 3.2 Add `TranslatorOptions.MaxLineLengthBytes` (default 1MB; `0` = unlimited, non-production). Lines longer than cap → drop + `parse_errors_total{kind = line_too_long}` counter + journal event.
  - Verify: a unit test that emits a 1.5MB JSON line → counter increments, the line is dropped, the worker loop survives. A unit test with `MaxLineLengthBytes = 0` accepts it. `dotnet run --project tests/unit/Comuki.Host.Translator.Unit.Runtime` exits 0.

## 4. Phase — Contract-handshake (Wire version + skew detection)

- [ ] 4.1 Add `WorkerHandshake` payload to `Connect` request (per `design.md` §D5): `protocol_version: string` (semver), `capabilities: repeated string`. The proto schema lives under `platform/src/shared/Comuki.Shared.Contracts.Grpc/WorkerMessages.cs`. Backward-compat: v1.x worker без handshake'а работает в compat-mode (host applies `default_capabilities = []`).
  - Verify: a unit test with a `Connect` carrying `protocol_version = "1.0.0"` against host expecting `"1.0.0"` → worker connects with full capabilities. A unit test with `protocol_version = "2.0.0"` (major mismatch) → `Connect` rejected with `worker.skew_major`. A minor-mismatch test → worker connects with `capabilities_compat:*` filter and journals `worker.skew_detected`. `dotnet run --project tests/unit/Comuki.Host.Unit.WorkerRuntime` exits 0.

- [ ] 4.2 Implement skew detection in `Comuki.Host.Translator/Connection/HandshakeValidator.cs`: MAJOR mismatch → reject `Connect`; MINOR mismatch → accept with filtered capabilities; unknown capability flag → ignore + counter. All paths journal.
  - Verify: covers the three scenarios above. `dotnet run --project tests/unit/Comuki.Host.Translator.Unit.Runtime` exits 0.

- [ ] 4.3 Wire the existing `agents/comuki-worker-sdk/` `Connect` method to emit `WorkerHandshake` payload. Bump the worker-sdk minor version. Release-coordinated with the host change (one PR per side, deployed in lock-step per `cross-service-communication.md` §4).
  - Verify: a worker-sdk unit test with the new payload — `Connect` carries `protocol_version` and `capabilities` fields.

## 5. Phase — TrustClass-промоушен (evidence-driven)

- [ ] 5.1 Add `TrustClassPromotionPolicy` (`platform/src/engine/Comuki.Engine.Orchestration/Domain/TrustClassPromotionPolicy.cs`): typed record with `K_green: int = 3`, `window_ms: long = 24h`, `red_pattern: int = 3`, `promotion_target: RunTrustClass = RunTrustClass.Max`, `predecessor: RunTrustClass = RunTrustClass.Standard`. Bind via `OrchestrationOptions.TrustClassPromotionPolicy` with `ValidateDataAnnotations().ValidateOnStart()`.
  - Verify: a unit test with `K_green = 3` and 3 consecutive `gate_evaluated:passed` events → `RunTrustClass.SetAsync(runId, Max)` is invoked once. A unit test with 3 consecutive `gate_evaluated:failed` → `RunTrustClass.SetAsync(runId, Standard)` (predecessor) is invoked once. A unit test with mixed verdicts → no promotion. `dotnet run --project tests/unit/Comuki.Engine.Orchestration.Unit.StatusMachine` exits 0.

- [ ] 5.2 Add `ITrustClassPromotionConsumer` (`Comuki.Engine.Orchestration.Application.TrustClassPromotionConsumer`): reads `IVerificationRecordStore.ListRecentByProjectAsync(projectId, window_ms)`, applies `TrustClassPromotionPolicy`, calls `IRunTrustClassStore.SetAsync(runId, target)` for promoted/demoted. Subscription model — the existing `VerificationEvaluationService` (added in `add-orchestra` §3.2 commit `b9fe26dc`) is the publisher; this consumer is the subscriber.
  - Verify: a unit test that subscribes to a synthetic stream of `gate_evaluated` events for one RunId — assertions on `SetAsync` calls match the policy. A unit test that subscribes to events across multiple RunIds — promotion is per-RunId (no cross-contamination). `dotnet run --project tests/unit/Comuki.Engine.Orchestration.Unit.StatusMachine` exits 0.

## 6. Phase — Multi-replica воркер-hub (PG LISTEN/NOTIFY)

> **Single-replica по-прежнему документирован** (см. `add-orchestra` §D11
> + §10.7 health check). Этот change переезжает **источник** состояния
> на PG (multi-replica становится *возможным*); claim-fencing —
> следующий change.

### 6.1 Phase 6A — PG schema + read

- [ ] 6.1.1 Add `comuki_cmdhub` table per `design.md` §D6 schema. Migration `dotnet ef migrations add AddWorkerCommandHubTable` (per canon `ef-migrations.md` — tool-generated, never hand-edited). The table lives in the existing `comuki_infra` schema.
  - Verify: `dotnet ef migrations add` produces the migration; `dotnet ef database update` against the test postgres fixture applies it; the new index (`WHERE claimed_by IS NULL`) is created.

- [ ] 6.1.2 Implement `IPGWorkerCommandHubReader` (`Comuki.Host.Translator.Hub.PGWorkerCommandHubReader`): `LISTEN comuki.cmdhub` channel + polling fallback (every 5s `SELECT WHERE claimed_by IS NULL LIMIT 1`). Delivers commands to the existing `WorkerCommandHub` in-memory pipe (the in-memory pipe stays as the consumer-side optimisation; it is no longer the **source**).
  - Verify: a unit test with a real PG (Testcontainers; see `.agents/rules/process/local-test-runtime.md`) — INSERT into `comuki_cmdhub` from one connection is observed by LISTEN on a second connection within 100ms; polling fallback within 1s even with `pg_terminate_backend` between INSERT and NOTIFY. `dotnet run --project tests/integration/Comuki.Host.Integration.Workers` exits 0.

### 6.2 Phase 6B — PG write + cache invalidation

- [ ] 6.2.1 Add `IPGWorkerCommandHubWriter`: `INSERT INTO comuki_cmdhub (...)` (one INSERT per command) + `NOTIFY comuki.cmdhub` is fired by an `AFTER INSERT` trigger (the schema after writing is automatic; no application-level NOTIFY). Replica name (hostname) is the `claimed_by` value.
  - Verify: a unit test that writes a `Stop` command — the same INSERT is observable by every replica's LISTEN; the polling fallback also picks it up within 5s if LISTEN misses.

- [ ] 6.2.2 Migrate `IVirtualKeyStore.MutatingTunings` (the in-memory overlay used by `Tuner` per `add-orchestra` §7.1) to PG: new `proxy_virtual_keys_tunings` table + read-through in-memory cache (TTL 1s, refresh-on-miss). The `ConfigurationVirtualKeyStore` (config-seed) stays; the overlay becomes PG-backed.
  - Verify: a unit test with a real PG — a `Tuner` write is visible across two replicas within 100ms; cache TTL refresh is 1s; `FindAsync` reads from cache, miss-refreshes from PG.

### 6.3 Cross-phase gate (Phase 6)

- [ ] 6.3.1 `dotnet build comuki.slnx -c Debug` exits 0; `dotnet run --project tests/integration/Comuki.Host.Integration.Workers` exits 0; `dotnet run --project tests/integration/Comuki.Modules.Proxy.Integration` exits 0.

## 7. Cross-phase gate

- [ ] 7.1 `dotnet build comuki.slnx -c Debug` exits 0 after each merged phase; warnings-as-errors gates the build (per canon `~/.agents/rules/process/build-verification.md`).
- [ ] 7.2 `dotnet run --project tests/unit/<touched-project>` for the touched unit projects exits 0; coverage floor 70% line (`Directory.Build`).
- [ ] 7.3 `dotnet run --project tests/integration/Comuki.Host.Translator.Integration.PiCli` exits 0 against the local Testcontainers runtime (see `.agents/rules/process/local-test-runtime.md`) — exercises the end-to-end pi CLI path through the new telemetry, deadline, and exit-code paths.
- [ ] 7.4 OpenSpec validation: `openspec validate harden-worker-runtime --strict` (and `--all` to keep the rest of the repo clean) exits 0.
- [ ] 7.5 Phase 4 release-coordination note in `STATE.md` and the deployment runbook: `WorkerHandshake` is a wire change — host and worker-sdk deploy in lock-step. Single-replica probe `comuki.orchestra.single_replica` (`add-orchestra` §10.7) remains; phase 6 does not retire it (claim-fencing is the next one).
- [ ] 7.6 The two `add-orchestra` §8 tasks added by this change (`8.5 ACP-adapter as third harness` and `8.6 PlanValidator width/depth caps`) live in `openspec/changes/add-orchestra/tasks.md` — not here. This change ships the documentation/coordination record in `design.md` §Coordination notes; the implementation lands in `add-orchestra` §8 wave.

## Sub-Tasks closed alongside this change

This change lands **two** small edits to `add-orchestra/tasks.md` (Phase 8 — the owner of those tasks). See the file `openspec/changes/add-orchestra/tasks.md` after this commit:

- **8.5** «ACP-adapter as third harness» — `IHarness` implementation atop Agent-Client-Protocol; entry point for Codex/Gemini/OpenHands-compatible clients; depends on 8.1-8.3 (the existing Instrument phase).
- **8.6** «PlanValidator width/depth caps» — node/edge/depth caps on `PlanValidator` (configurable, conservative default; per-profile override).

These live in `add-orchestra` because the `IHarness` boundary belongs to `add-orchestra` §8 (Instrument) and `PlanValidator` is a `harness-spi` consumer concern. They are NOT deltas of `harden-worker-runtime`; this change records the ownership in `design.md` §Coordination notes and lands the tasks.
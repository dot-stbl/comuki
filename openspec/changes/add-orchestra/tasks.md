## Phase order (dogfooding-first)

Each phase is a vertical change with its own verification gate. Phases ship in the order listed below; later phases ride what earlier phases ship. The dependency edges are explicit at the head of each phase section.

```text
1. Baton           ──→ (independent of cowork 11.1)
2. Critic-foundation──→ (independent of cowork 11.1)
3. Coda           ──→ 1
4. Critic-sweep   ──→ 2
5. Section        ──→ (independent)
6. Encore         ──→ 1, 5
7. Tuner          ──→ 1
8. Instrument     ──→ adds-mission-cowork 11.1 (work-pool slice)
```

## 1. Phase — Baton (live-session steering)

First real caller of `WorkerCommandHub` (the existing plumbing ships on master, 0 production calls). Translator learns session-mode execution; steering is the first operator verb that turns a session into something an LLM can be talked to mid-flight.

- [ ] 1.1 Promote `IPiRunner` to `IHarness` for *this* phase: `Capabilities { LiveSession: bool }` (a `record struct` with one field for now; later phases extend). pi declares `Capabilities.LiveSession = true`. Translator reads the value and chooses `--mode json` with the in-process session transport (existing `pi` CLI, no API change in the harness).
  - Verify: `new PiHarness().Capabilities.LiveSession == true`; an integration test with the test-fake harness declares `false` and the steering endpoint refuses `InjectContext` with a typed 409.
- [ ] 1.2 Extend `WorkerCommandHub` with a typed `TurnInput` command variant (the existing `InjectContext` carries a file path; `TurnInput` carries a structured turn `{ Text, Role, Metadata }`). Wire it through the existing bidi command stream; delivery is best-effort like `Stop` (a worker without a live stream is a miss, not an error).
  - Verify: a unit test that two commands on a connected stream are delivered in order; a unit test that a command without a stream returns `false` and does not throw.
- [ ] 1.3 Add `[RequiresFeature(Features.Steering)]` endpoint `POST /api/v1/runs/{runId}/steer` (route constant `ApiRoutes.RunSteer`) on the existing `RunsController` under `Comuki.Host/Runs/Controllers/`. The handler resolves `runId → WorkItem → LeasedBy → ExecutionId`, calls `WorkerCommandHub.TrySendTurnInput(ExecutionId, TurnInput)`, and answers 202 with `{ delivered: bool }`. The resolver is a shared helper next to the existing `HostCancelRunAdapter.cs` in `Comuki.Host/Runs/`.
  - Verify: an integration test with `Comuki.Host.Testing/HostComposer` where the steer is sent during a live session; the session harness's stream receives the turn. A second integration test sends a steer for a run whose harness declares `LiveSession = false` and observes the fallback response (a new research WorkItem, per coworker tasks 11.1).
- [ ] 1.4 Add `WorkerCommandHub.TrySendTurnInput(ExecutionId, TurnInput)` and the `runId → lease → ExecutionId` resolver as production code. Today the channel ships with `Stop` / `InjectContext` / `LeaseExpired` / `Exec` plumbing and zero production callers (`Stop` and `LeaseExpired` are wired to the reaper, the others sit on the channel unused); this phase is the first call-site that sends a steering command through the existing bidi stream. The `SendAsync` naming is illustrative — the actual entry point follows the `TrySend*` shape of the existing methods on `IWorkerCommandPipe`.
  - Verify: `dotnet build comuki.slnx -c Debug` + `dotnet run --project tests/unit/Comuki.Host.Unit.WorkerCommandHub` (the existing Host unit test for the hub) exit 0; an integration test that the steer lands on a connected session and returns `false` (not throws) when the worker has no live stream.
- [ ] 1.5 Document the single-replica constraint (`design.md` D11) in the deployment runbook under `.agents/docs/operations/`: a `comuki.orchestra.single_replica` health check reports the active replica count and goes Unhealthy when > 1. No code change; the runbook is the contract.
  - Verify: a manual deployment run with `replicas: 2` fails the health probe; a manual run with `replicas: 1` passes.

## 2. Phase — Critic-foundation (logs + metrics read)

The swarm becomes readable to itself through MCP. Today `MEL` writes to the console and `ComukiTelemetryInstaller.cs` exports tracing + metrics only; VictoriaLogs is up and empty. This phase is the substrate the Critic-sweep (Phase 4) rides.

- [ ] 2.1 Extend `ComukiTelemetryInstaller` with the MEL → OTLP exporter log leg: `builder.Logging.AddOpenTelemetry(o => o.AddOtlpExporter())` on the existing OTel resource; structured logs flow through the same resource as traces and metrics. The existing console leg stays (Community observability never relied on a collector).
  - Verify: a manual run with `OTEL_EXPORTER_OTLP_ENDPOINT=http://victoria:4318` produces VictoriaLogs records in the next minute; a run with the endpoint unset keeps the console-only path.
- [ ] 2.2 Add `Comuki.Modules.Observability` project (Domain / Application / Infrastructure) with `IVictoriaLogsQueryClient` and `IVictoriaMetricsQueryClient` ports. One concrete implementation each behind the `IVictoriaEndpointResolver` (config + health probe). Typed wrappers over the Victoria HTTP API; no protobuf, no OpenTelemetry SDK misuse. The interface methods return DTOs, not raw `JsonElement`.
  - Verify: a unit test against the typed wrappers with `WireMock.Net` (or a recorded fixture); the contract is read-only.
- [ ] 2.3 Register the four MCP read tools in `Comuki.Host/Mcp/McpToolCatalog.cs`: `observability.logs.search` (LogsQL query), `observability.metrics.query` (PromQL query), `observability.logs.context` (log-context lookup), `observability.metrics.series` (label-set lookup). Each tool demands `observability:read` (new permission in `Permissions.cs` and `RoleMatrix`). The existing `McpToolPermissionMap` (the canonical permission map on `Comuki.Host.Mcp`) and `McpWorkerToolGate` extend; no new gate path.
  - Verify: a ChatAgent unit test that calls each tool through the MCP integration test harness; an unauthorised caller observes the gate denial.
- [ ] 2.4 Wire the four tools into `ComukiTelemetryInstaller` so the OTel `ActivitySource` correlation ids match the typed client (the client accepts an `Activity.Current?.TraceId` and threads it as the Victoria query filter). No new telemetry exporter.
  - Verify: a manual run that emits a trace event and then queries `observability.logs.search` for the trace id receives the matching log record.
- [ ] 2.5 Add `Observability:Victoria:RetentionPeriod` (configurable, no code-level default — the deployed Victoria stack ships with `--retentionPeriod=1` per the platform baseline) and `Observability:Victoria:ScrapeInterval` (default 15s, range 5s–5m) to `config.toml`. The Victoria stack under `deploy/docker-compose.yml` reads the retention from the same TOML path. The change does not lower the retention baseline; an explicit retention setting is a deployment-level concern, not a code-level default.
  - Verify: a manual run with `Observability:Victoria:RetentionPeriod=30d` produces a Victoria container with the documented retention; `dotnet run --project tests/integration/Comuki.Modules.Observability.Integration` exercises the parse against the typed options.

## 3. Phase — Coda (verification axis)

Done is not verified. Today the run state machine accepts every `Succeeded` claim at face value; a worker that finished with incomplete work is indistinguishable from a worker that finished with the verified proof. This phase makes the axis orthogonal: the run state machine stays seven states; `VerificationRecord` is a sibling.

- [ ] 3.1 Create the `verifications` table (snake_case, indexed on `(work_item_id, gate_name)`) and a `gate_evaluated` journal event type (`run_events` is the existing append-only store). The event payload carries `{ gate_name, verdict: pending|passed|failed, evidence_ref? }`. Migration is `dotnet ef migrations add AddVerificationRecord` — never hand-edited.
  - Verify: the migration is the only schema change in this phase; the existing seven run states are untouched; the golden fixtures in `tests/unit/Comuki.Engine.Orchestration.Unit.Eval` still pass.
- [ ] 3.2 Add `IVerificationGateProvider` SPI (per `design.md` §D5) and the platform-shipped registry. The first registered provider is the existing `Verify` module — flip `Verify:Verifier:Enabled=true`, route `GenericCommandRun`'s `Pending → Running → Green/Red` through the registry, and stamp the result as a `gate_evaluated` event on the bound WorkItem.
  - Verify: an integration test that registers a second fake provider (`"commands-green"` returning `passed`) and observes the journal carries two `gate_evaluated` entries with the correct verdict.
- [ ] 3.3 Wire `ProjectSettings.VerifyEnabled` (existing field, 0 consumers today) into the verification path. When `false`, gates are skipped and `VerificationRecord` rows are not written (the run lands as `Succeeded` with the existing semantics). When `true`, gates are required; a `Succeeded` run with no `passed` gate verdicts gets the visible "verification pending" annotation through the derived view, not a new run state.
  - Verify: a unit test with a project that has `VerifyEnabled = false` keeps the existing run path; a project with `VerifyEnabled = true` and zero `passed` gates observes the annotation.
- [ ] 3.4 Extend the worker artifact upload endpoint (`POST /workers/{workItemId}/artifacts`) with `text/x-diff` on the mime allow-list. The current allow-list (`VisualArtifactLimits.MaxBytesByMime`) carries exactly three mimes (`image/png`, `text/html`, `image/svg+xml`); this task adds the fourth (`text/x-diff`) and a per-mime size cap (`TextDiffMaxBytes` in the same `VisualArtifactLimits` static). A worker uploading a `changeset.diff` lands the bundle member in the existing `{project}/{run}/` MinIO prefix; the gate provider reads it from the bundle's `changeset.diff` member (the named key `cmdiff` in the `artifacts` table).
  - Verify: an integration test uploads a sample diff, runs a gate provider that reads `cmdiff`, and observes the verdict is based on the diff content. A second test posts an unsupported mime (e.g. `text/plain`) and observes `415 Unsupported Media Type`.
- [ ] 3.5 Build a derived view `GET /api/v1/runs/{runId}/verification` returning the per-gate verdict list, the verification-pending annotation when `Succeeded` with no `passed` gates, and a stable evidence pointer list (gate name → `cmdiff` bundle member URI when present). The endpoint requires `run:read` (no new permission).
  - Verify: an integration test with `Comuki.Host.Testing/HostComposer` exercises the view path.
- [ ] 3.6 Restore the `MergeQueueEntry` / `MergeBatch` ↔ `Run` link. Today the orchestration engine's merge-queue tables (`platform/src/engine/Comuki.Engine.Orchestration/Domain/MergeQueue/MergeQueueEntry.cs`, `MergeBatch.cs`) carry `ProjectId?` but no `RunId`; the link from a merge-queue entry to the run that produced the PR (and thus to the verification record under Coda) is implicit through `PullRequestUrl` text only. This task adds `RunId?` (nullable for cross-project release trains) to both aggregates, with a database migration `dotnet ef migrations add AddMergeQueueRunReference`; the field is journaled under the `merge_queue.run_referenced` event when set. Dependency-ordering (issue #50) is **out of scope here** — `add-multi-repo-projects` (#163) already subsumes it through per-repository queues (see its design.md §"Relationship to issue #50"); this task only restores the run reference, not a dependency graph.
  - Verify: a `MergeQueueEntry` / `MergeBatch` row created by the platform now carries the `RunId` of the producing run; the verification view (task 3.5) joins on this field for the gate-evidence trace. The existing `MergeQueueEntryShould` / `MergeMergeQueueHandlerShould` tests stay green (the new column is nullable and additive).

## 4. Phase — Critic-sweep (standing queries, on the foundation)

A scheduled worker reads the same logs/metrics the human reads (Phase 2) and decides. Today `ComukiWorkerRegistry` has zero consumers; this phase is the first.

- [ ] 4.1 Register `CriticSweepWorker : IComukiWorker` in `Comuki.Host` (the hosted-service worker abstraction, not the ephemeral coding-agent worker — the latter is the *consumer* of an `INativeTicketHandler`). Decorate with `[RequiresFeature(Features.Critic)]` — `Critic` is one of the seven new feature keys defined in the **editions** capability (Phase 4 brings the call-site; the editions spec §"First paid-feature call-sites land with the orchestra phases" records this binding as the first call-site for the `critic` key). **Placement:** the worker MUST live in `Comuki.Host` — one of the three assemblies the `EveryPaidRegistryEntryIsGatedShould` architecture test indexes — otherwise the companion `NoGateAttributeLivesOutsideTheIndex` guard fails the build with an orphan-key message. The worker opens a fresh DI scope per cycle, reads `IScheduledJobStore` for the due `StandingQuery` jobs, and dispatches each as a normal worker run with profile `ops-critique` (the profile ships as part of this change; the standing-query library under `control-plane/profiles/standing-queries/` introduces `ops-critique.md` — see task 4.4).
  - Verify: `WorkerFeatureGate` partitions the registry into the gated and ungated halves — `ComukiWorkerRegistry.Snapshot()` in Community excludes `CriticSweepWorker`, in paid includes it; the existing `ComukiWorkerRegistry.Snapshot()` test still passes; `EveryPaidRegistryEntryIsGatedShould` no longer flags `Critic` as an orphan.
- [ ] 4.2 Add `StandingQuery` job type to `Comuki.Modules.Scheduler`: `Schedule` (cron), `SessionBrief` (the LLM brief), `McpToolBudget` (per-cycle token / wallclock limit), and an output schema (`{"ticket"?: {...}, "memory_note"?: {...}}`). The job lands in the same `scheduled_jobs` table in the `scheduler` schema (the `scheduler` module's existing `jobs` table, under a `standing_query` discriminator); no new table.

  > **Coordination note (2026-10-04).** The full per-tick fire-trail (a `firings` table) is the open follow-up from the unarchived `add-scheduled-jobs` change. On master today, the scheduler records the fire trail on the `jobs` row itself — `LastFiredAt` and `NextFireAt` on `SchedulerDbContext` (see `ScheduledJobConfiguration.cs`); a stand-alone `firings` history table is not present. The Coda phase (verification record) does not depend on a `firings` history — the sweep worker's `critic` verdict is journaled as a `gate_evaluated` event on the bound WorkItem, which is enough to compose the worker's audit row. When `add-scheduled-jobs` lands, the read adapter joins on the `firings` table; the Coda phase does not block on that.
  - Verify: an integration test fires a `StandingQuery` that asks "errors in the last hour" and observes a `native_tickets` row.
- [ ] 4.3 Add a `CritiqueHandler` that translates the sweep's verdict into one of: `CreateNativeTicketHandler` (the existing native-ticket card path), `MemoryNoteHandler` (write a `Project`-scope memory fact with the standing query's brief as provenance), or `Skip` (the standing query's verdict was a false positive — log only). The decision is per-leg and lives in the handler; the worker just hands the verdict to the dispatcher.
  - Verify: an integration test with a controlled log stream produces a native ticket and a memory note respectively.
- [ ] 4.4 Add the standing-query *library* under `control-plane/profiles/standing-queries/` (markdown files: errors-this-hour, p95-latency-this-hour, missed-events-this-hour, idle-pool-this-hour). Each is a `StandingQuery` job with a default cron and the `ops-critique` profile. The `ops-critique.md` profile is **created by this change** — it does not exist today (the directory carries `docs-writer`, `env-author`, `explore-readonly`, `implement`, `pr-review`); this task adds the first `standing-query`-only profile alongside. Operators add new standing queries by editing the library, not by changing the registry.
  - Verify: the four shipped queries are visible in `IScheduledJobStore` under the standing-query discriminator; `dotnet build comuki.slnx -c Debug` accepts the new profile entries.

## 5. Phase — Section (four scope layers)

`ProjectSettings` move out of being the only settings surface. Orchestra becomes the first *write* path to global.

- [ ] 5.1 Add the `Section` aggregate (slug, name, parent-project, archived) with a `__comuki_sections` migrations history. The Section is a *grouping* layer: workers carry a `SectionId` column on the `Claim` row (`worker.section_id`), and the precedence resolver reads `Sections ∩ ProjectScope` to determine which section's overrides apply.
  - Verify: the migration is the only schema change in this phase; `dotnet run --project tests/integration/Comuki.Modules.Projects.Integration` still passes.
- [ ] 5.2 Promote `GET /api/v1/settings` to `PATCH /api/v1/settings` with optimistic concurrency on a single `OrchestraSettings` row (the first *write* path to global). The body shape is the same `ProjectSettings`-shaped field set; the precedence layer puts orchestra values at the top of the chain.
  - Verify: an integration test with `Comuki.Host.Testing/HostComposer` patches a field and observes the precedence resolver reads it before the section / worker layers.
- [ ] 5.3 Build the four-section precedence adapter as `ProjectSettingsResolver` — a single `IProjectSettingsSnapshotCache`-style seam (the documented pattern from `ProjectScaleSettingsAdapter`). The resolver reads in order: orchestra → section → worker → card. Card returns `null` for this phase (the card entity is deferred to the cowork entity landing); the precedence chain is implemented, the four-layer population is three-and-a-half.
  - Verify: an integration test with explicit orchestra, section, and worker overrides confirms the precedence; an integration test with no orchestra value observes the section value.
- [ ] 5.4 Wire the four unused `ProjectSettings` bool flags (`ApproveRequired`, `KnowledgeEnabled`, `VerifyEnabled`, `ProxyEnabled`) onto their real consumer paths. The two budget-side fields (`softBudgetUsdMicros` / `hardBudgetUsdMicros`) are already consumed by `Comuki.Host/Costs/ProjectBudgetSettingsAdapter.cs`; this phase does not touch them. This phase announces the contract: each flag is read by exactly one consumer; the `null`-means-default semantics is unchanged. `VerifyEnabled` was wired in Phase 3; this phase confirms the wiring with an integration test that reads the precedence and observes the consumer reading the right value.
  - Verify: `dotnet run --project tests/unit/Comuki.Modules.Projects.Unit` exits 0; the architecture test `EditionsRegistryContainsEveryGateKeyShould` still passes (no new feature key in this phase).

## 6. Phase — Encore (automation as an object)

A first-class `Automation` object on top of the three existing surfaces. No schema fork; the three sources stay.

- [ ] 6.1 Add the `Automation` read-model under `Comuki.Modules.Automation` (Domain / Application / Infrastructure). The read-model joins three existing sources: `ScheduledJob` (cron trigger), `SourceConnection` (webhook trigger), `RunStatusBridge` + `SyncJob` (run-terminal trigger). The model is read-only; it never forks the schema. The outbox leg is opt-in per automation (D8) — default is the read-model + idempotency key.
  - Verify: an integration test that wires one of each trigger type observes the read model joining all three.
- [ ] 6.2 Ship the action type as `AutomationAction` (a discriminated union of `LaunchRun` / `Park` / `SyncBack`) and replace the three hard-coded action verbs across the existing surfaces. The trigger-provider registry is **deferred** — recorded in `design.md` §D8 as "created on the first non-cron/non-webhook trigger".
  - Verify: `dotnet build comuki.slnx -c Debug`; the existing tests for the three hard-coded actions still pass under the new typed wrapper.
- [ ] 6.3 Add `GET /api/v1/projects/{projectId}/automations` and the CRUD siblings (create / patch / archive). Each row carries `{ id, projectId, trigger: {kind, ...}, action: AutomationAction, enabled, history: AuditList }`. The endpoint requires `automation:read` / `automation:write` (new permissions in `Permissions.cs` / `RoleMatrix`).
  - Verify: an integration test with the four CRUD paths and a non-authorised write observer.
- [ ] 6.4 Add the dashboard `/automations` page (list + detail + history) under a new `dashboard/src/automations/` domain. FE parity with the operator dashboard (no new top-level dashboard app). The page renders the trigger, action, enabled state, and the latest history row; an empty state follows the existing EmptyState primitive contract.
  - Verify: `bun run typecheck && bun run lint && bun run test` exit 0; the existing dashboard contract drift fixture still passes.

## 7. Phase — Tuner (mutable virtual keys)

`PATCH /api/v1/proxy/keys/{id}` becomes 200. Today it is 501 ("need a mutable key store first"). The platform already has the in-memory overlay (`ConfigurationVirtualKeyStore` + in-memory) — Tuner promotes the overlay to primary for tunings.

- [ ] 7.1 Add `Tuning` shape (`{ id, keyId, generation, model?, limitUsd?, changedAt, changedBy }`) and the `IVirtualKeyStore.ApplyTuningAsync` port. The in-memory overlay's `SetAsync` becomes the new primary; the `ConfigurationVirtualKeyStore` stays for seeded keys. Every write bumps `generation`; the live worker's session transport carries the current generation and rejects stale ones.
  - Verify: a unit test that `ApplyTuningAsync` writes the new value and bumps the generation; a unit test that a stale generation rejects with a typed 409.
- [ ] 7.2 Promote `PATCH /api/v1/proxy/keys/{keyId}` to 200 with the typed body shape. The handler demands the new `model-control:write` permission AND `[RequiresFeature(Features.ModelControl)]` (the existing `proxy:write` is the surface for revoke / mint / revoke-mint operations on the existing proxy key store; model tuning is a separate, model-control-only surface — see the `identity` and `model-control` capabilities for the canonical access model). The handler returns the new view with the bumped generation. Auth scope, expiry, allowed-models — unchanged.
  - Verify: an integration test with `Comuki.Host.Testing/HostComposer` patches a key and observes the live proxy accepts requests with the new model. A second test confirms the existing `proxy:write` permission is **not** sufficient to call this endpoint.
- [ ] 7.3 Cooperate with `NullBudgetGate` (costs): when a tuning adds or lowers `limitUsd`, the next proxy call sees the new cap. The cooperation is through the existing `usage_events.cost_usd_micros` query; Tuner does not introduce a parallel budget path. The `BudgetUsd` field on the key becomes mutable.
  - Verify: an integration test that lowers a key's `BudgetUsd` and observes the next call over the cap is rejected by the existing `NullBudgetGate` path.
- [ ] 7.4 Wire the live propagation through Baton's `WorkerCommandHub`: a tuning that targets a *running* key sends a `ModelChanged` command to the worker through the same bidi stream; the worker's harness reload reads the new model from the session transport. The harness declares `Capabilities.LiveSession` (Phase 1); the propagation rides it.
  - Verify: an integration test that patches a key while its worker is alive and observes the worker receives the `ModelChanged` command within one heartbeat.

## 8. Phase — Instrument (harness SPI)

`IHarness` is the abstraction. pi is the first implementation; `Comuki.TestFakePi` is the second. The phase ships *with* cowork 11.1.

- [ ] 8.1 Promote `IPiRunner` to `IHarness` (D10) and register `PiHarness` (existing code path, renamed) and `TestFakeHarness` (existing `Comuki.TestFakePi`, lifted) as the first two implementations. The harness declares `Capabilities.LiveSession` (Phase 1). Translator's `PiRunner` becomes `HarnessRunner`; the public surface is `IHarness` only.
  - Verify: `dotnet build comuki.slnx -c Debug`; the existing pi spawning unit tests still pass under the harness abstraction.
- [ ] 8.2 Add `HarnessProfile` (a control-plane marker: `harness: pi`, `harness: test-fake-pi`). The profile is a key in the catalog (per `control-plane/profiles/<name>.md`'s `harness:` frontmatter); the claim-matching engine reads it through the same path as `envClass`. Profiles without `harness:` default to `pi`.
  - Verify: an integration test that registers a profile with `harness: test-fake-pi` and observes claims match it.
- [ ] 8.3 Ship the dependency on cowork 11.1 (slot/execution identity): `IHarness.StartAsync` accepts a `SlotHandle` (the cowork contract). If cowork 11.1 slips, Phase 8 is the first thing this change drops — the SPI ships in a slot-agnostic shape and the slot adapter is added when 11.1 lands.
  - Verify: the phase's `IHarness.StartAsync` signature references `SlotHandle` (or a stub) at compile time; the slot adapter is filed as a follow-up if 11.1 slips.
- [ ] 8.4 Add the harness picker in the dashboard project-settings drawer as a single select field (`harness: pi | test-fake-pi`). The picker's source is the harness catalog (`IHarnessCatalog`); the catalog is populated at start from the registered `IHarness` instances. The picker component lives under `dashboard/src/domains/projects/` next to the existing project-settings components (the existing feature module under `domains/<x>/` is the template — no top-level `dashboard/src/automations/` directory).
  - Verify: `bun run typecheck && bun run lint && bun run test` exit 0; the dashboard's project-settings-drawer storybook story covers the picker.

## 9. Phase — `harden-pi-worker-sandbox` checkbox closeout (docs-completeness)

This is **not** a code change. The four checkboxes listed below describe work that landed on master under the `harden-pi-worker-sandbox` umbrella (#121); they were left open in that change's `tasks.md`. This phase closes them with code references (no implementation, no test changes).

- [x] 9.1 Close `harden-pi-worker-sandbox` task **4.1** — `SourceGitUrl` / `SourceGitRef` on Project.
  - Evidence: migration `20260928184008_AddProjectSourceGitAndCredentialRef` (per the migration filename); `Project.SourceGitUrl` / `Project.SourceGitRef` columns; PATCH accepts and views expose them. Reference: the migration file under `platform/src/modules/Projects/*/Migrations/` and the Project aggregate source.
- [x] 9.2 Close `harden-pi-worker-sandbox` task **4.2** — optional git-credential secret ref on project settings.
  - Evidence: the migration in §9.1 also adds the credential-ref column; `ProjectSettings` carries the env-var name; `ISecretResolver` resolves at clone time.
- [x] 9.3 Close `harden-pi-worker-sandbox` task **4.3** — Translator clones HTTPS into cwd after claim and before pi.
  - Evidence: `Host/Workers/ClaimSourceGitResolver.cs` resolves the URL + credential; `Host.Translator/Execution/Clone/SourceCloneRunner.cs` runs the clone; the step is invoked from `TranslatorLoop.cs:93-115` between the claim cycle's prepare and the spawn.
- [x] 9.4 Close `harden-pi-worker-sandbox` task **5.1** *partially* — `WorkspacePrepared` / `EgressApplied` / `AgentRunning` journal conditions. The translator loop emits the three events on the bound run (`TranslatorLoop.cs:157-183`). The remaining work in 5.1 (the journal payload tests for the three conditions) is filed as a follow-up and is *not* closed in this change.
  - Evidence: the three calls in `TranslatorLoop.cs:157-183`; the journal payload-test follow-up is tracked as task 9.5 below.
- [ ] 9.5 Follow-up: `WorkspacePrepared` / `EgressApplied` / `AgentRunning` journal payload tests. The three `run_events` rows are written (see task 9.4); the test surface that asserts the payload shape (worker token, run id, lease id, conditions) is not in this change. Tracking: this task; the change does not introduce a separate `.planning/BACKEND-ISSUES.md` catalog (none exists in this repo).

## 10. Cross-phase gate

- [ ] 10.1 `dotnet build comuki.slnx -c Debug` exits 0 after each merged phase; warnings-as-errors gates the build. (Same as the rest of the repo; not new.)
- [ ] 10.2 `dotnet run --project tests/unit/<touched-project>` for the touched unit projects exits 0; coverage floor 70% line (`Directory.Build`).
- [ ] 10.3 `cd dashboard && bun run typecheck && bun run lint && bun run test` exits 0 after each phase that touches the FE.
- [ ] 10.4 The editions architecture tests (`EditionsRegistryContainsEveryGateKeyShould`, `EveryPaidRegistryEntryIsGatedShould`, `CommunityEditionComposesCleanlyShould`) accept the seven new feature keys after the first paid-feature call-sites land (Phase 1 for `Steering`, Phase 4 for `Critic`, Phase 3 for `Verification`, Phase 6 for `Automation`, Phase 7 for `ModelControl`). `Scope-layers` and `harness-spi` are Community — no gate, but the entries are still in the registry.
- [ ] 10.5 OpenSpec validation: `openspec validate add-orchestra` (or the equivalent CLI command from `openspec/config.yaml` §operations) exits 0 after each phase's spec delta is synced into `openspec/specs/`.
- [ ] 10.6 Roll out behind project feature flags by phase, record minimum rollback version at each gate, and remove compatibility projections only after telemetry shows no legacy consumers.
- [ ] 10.7 Single-replica health check (`comuki.orchestra.single_replica`) reports the active replica count on every phase's merge; deployments with `replicas: 2` go Unhealthy and the runbook records the constraint (D11).
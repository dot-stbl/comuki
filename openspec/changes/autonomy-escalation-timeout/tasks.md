## 1. Domain: journal event type

- [ ] 1.1 Add `RunEventTypes.RunEscalationTimeout = "run.escalation_timeout"` constant in `platform/src/engine/Comuki.Engine.Orchestration/Domain/Journal/RunEventTypes.cs`; verify the dot.case format and the XML doc match the other constants in the file.

## 2. Options + payload helper

- [ ] 2.1 Create `platform/src/engine/Comuki.Engine.Orchestration/Options/EscalationTimeoutOptions.cs`: `SectionName = "Orchestration:EscalationTimeout"`, `EscalationTimeout` (`TimeSpan`, `[Range]` 5m–24h, default 1h), `SweepInterval` (`TimeSpan`, `[Range]` 5s–5m, default 15s), `Enabled` (`bool`, default `true`); verify `dotnet format` and the file passes `EnforceCodeStyleInBuild`.
- [ ] 2.2 Create `platform/src/engine/Comuki.Engine.Orchestration/Infrastructure/Journal/RunStatusChangePayloads.cs` as `internal static class` with one helper `RunStatusChangePayloads.EscalationTimeout(RunId runId, string from, string to, double ageSeconds)` that returns JSON via `JsonSerializerOptions.Web`; verify no shared singleton options field (per `anti-patterns.md` §6).

## 3. Sweeper + worker + wiring

- [ ] 3.1 Create `platform/src/engine/Comuki.Engine.Orchestration/Infrastructure/EscalationTimeout/EscalationTimeoutSweeper.cs`: sealed class with primary-ctor `(OrchestrationDbContext db, TimeProvider clock, IOptions<EscalationTimeoutOptions> options)`; `SweepAsync(CancellationToken) → EscalationTimeoutSwept` (record `{ int Archived; IReadOnlyList<Guid> RunIds; }`); query `db.Runs.Where(r => r.Status == RunStatus.Escalated && r.UpdatedAt < cutoff)` with `AsNoTracking` then re-load each tracked entity for the transition (or use `ExecuteUpdate` only if the model allows it — pick the simpler path: load, transition, journal, save); verify zero private methods (file-static helpers for the SQL/raw path if needed), no `_ =` discard, no `ThrowIf*`.
- [ ] 3.2 Create `platform/src/engine/Comuki.Engine.Orchestration/Infrastructure/Hosting/EscalationTimeoutWorker.cs`: `sealed class EscalationTimeoutWorker(IServiceScopeFactory scopeFactory, ISubjectScopeAccessor scopeAccessor, IOptions<EscalationTimeoutOptions> options, ILogger<EscalationTimeoutWorker> logger) : BackgroundService`; mirrors `LeaseReaperWorker` exactly — `ExecuteAsync` loops `AsSystem("escalation-timeout-sweeper")`, resolves `EscalationTimeoutSweeper`, calls `SweepAsync`, logs the count, awaits `Task.Delay(SweepInterval)`; verify `Enabled=false` short-circuits before the loop (cheaper than spinning the host).
- [ ] 3.3 Extend `OrchestrationInfrastructureExtensions.AddOrchestrationQueue` to: (a) `services.AddOptions<EscalationTimeoutOptions>().Bind(...).ValidateDataAnnotations().ValidateOnStart()`, (b) `services.AddScoped<EscalationTimeoutSweeper>()`, (c) conditionally `services.AddHostedService<EscalationTimeoutWorker>()` when `options.Value.Enabled` is true (resolve options once to avoid the spec warning about using a non-Options-aware bool); verify `dotnet build comuki.slnx -c Debug` is 0/0 after the change.

## 4. Integration test

- [ ] 4.1 Create `tests/integration/Comuki.Host.Integration.Runs/EscalationTimeoutSweeperShould.cs`: xUnit v3 + Testcontainers + xUnit `[Fact]`; seed one Escalated run with `UpdatedAt = now - 2h` and one Running run (control); run the sweeper once; assert: the Escalated run is `Cancelled`, one `run_events` row exists with `Type = "run.escalation_timeout"` and payload containing the age; the Running run is untouched; verify the test project already references the engine + host projects and only adds the new test file.

## 5. Docs + close-out

- [ ] 5.1 Add a one-line note to `.agents/STATE.md` "Дальше" section: post-v1 #11 Autonomy ratchet first sub-slice landed on `feature/autonomy` (`openspec/changes/autonomy-escalation-timeout/`).
- [ ] 5.2 Run the full gate: `dotnet build comuki.slnx -c Debug` (0/0), `dotnet run --project tests/Comuki.Architecture.Tests` (22+ green), `dotnet run --project tests/integration/Comuki.Host.Integration.Runs` (existing 6+ new tests green), and the orchestrator unit suite via `dotnet run --project tests/unit/Comuki.Engine.Orchestration.Unit.StatusMachine` (existing 5+ still green).
- [ ] 5.3 Self-audit: re-read `~/.agents/rules/csharp/code-shape.md`, `anti-patterns.md`, `naming-and-types.md`, `class-layout-and-tooling.md`, `di-options.md`, `di-lifetimes.md`, `ef-core.md`, `architecture.md`, `process/build-verification.md`, `process/commit-format.md`; fix every violation before committing.
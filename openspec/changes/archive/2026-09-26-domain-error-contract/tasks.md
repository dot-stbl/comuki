# Domain Error Contract — Tasks

Order: baseline check → mechanism (inert) → one module per commit
(Projects first, then Intake, Scheduler, Chat, Runs, Learning, Knowledge,
Workers, Proxy) → cleanup → gates. Every module section follows the same
discipline (design D7): shape tests written from the runner's current
literals land in the SAME commit as the handler addition and the runner
deletion. Gate commands are the repo's own: `dotnet format comuki.slnx
--severity hidden`, `dotnet build comuki.slnx -c Debug`,
`dotnet run --project <tests>`.

## 1. Baseline + mechanism (inert)

- [x] 1.1 Verify D-BE's landed baseline: the three Projects exceptions carry a `Code` property, `ProjectsEndpointRunner` sets `type:` URNs and reads `exception.Code` (design D8). If not yet on this branch, STOP this change and report — do not rebase D-BE's files. Verify: `platform/src/host/Comuki.Host/Projects/ProjectsModuleEndpoints.cs` and the three exception files show the `Code` + URN state
- [x] 1.2 Create `tests/unit/Comuki.Host.Unit.Errors` (xUnit v3 MTP project, slnx-registered) with the registry fixtures; it builds and runs empty: `dotnet run --project tests/unit/Comuki.Host.Unit.Errors`
- [x] 1.3 Add `Comuki.Host/Errors/ProblemAnswer.cs` (record per design D2 — status, type, title, detail, code, extensions, optional validation errors), `IProblemHandler.cs`, and the abstract `ProblemHandler<TException>` base (design D3). Verify: `dotnet build comuki.slnx -c Debug`
- [x] 1.4 Add `ProblemHandlerRegistry` (exact-type key, base-chain walk, kernel defaults, sealed-type startup assertion, warning log on a `DomainException` subclass reaching the 422 default) and the core default handlers: ProviderTimeout 504 / ProviderNotFound 404 / ProviderForbidden 403 / ProviderException 502, BudgetExceeded 402, DomainException 422 (instance `Code`), ValidationException 400 (per-field grouping identical to the current five runner copies), Exception 500 `about:blank`. Verify: new `ProblemHandlerRegistryShould` cases — exact beats default, base chain walked, sealed subclass assertion fires at boot, validation fixture matches the current grouping byte-for-byte
- [x] 1.5 Rewrite `ProviderExceptionHandler` to registry-lookup → log → `TypedResults.Problem`/`ValidationProblem` (delete the `ExceptionMapping` switch; URN construction moves to the answer per design D4); register registry + core handlers in `HostComposer` beside `AddExceptionHandler`. Behavior must be byte-identical for kernel exceptions — existing host error tests stay green unchanged: `dotnet run --project tests/unit/Comuki.Host.Unit.Errors` + `dotnet build comuki.slnx -c Debug`

## 2. Projects (first module — proves the pattern)

- [x] 2.1 Re-parent `ProjectNotFoundException`, `ProjectConflictException`, `ProjectSettingsConflictException`, `ProjectDomainTypeNotMappedException` to `DomainException` with codes as class-fact constants (design D1); grep `platform/src` for other catch sites of these types (expect only the runner + throwers). Verify: `dotnet build comuki.slnx -c Debug`
- [x] 2.2 Add `Errors/Handlers/Projects/` handlers (NotFound 404 + `projectId`; SettingsConflict 409 + `projectId` + `currentVersion` + the "; re-read the settings and retry" detail suffix; Conflict 409; DomainTypeNotMapped mapped to its current answer) + `AddProjectsProblemHandlers()` registration in `HostComposer`; write `ProjectsProblemHandlersShould` asserting the exact pre-migration literals (status/type URN/title/code/extensions). THEN in the same commit delete the catch arms from `ProjectsEndpointRunner` (endpoints call handlers directly) and delete the runner. Verify: `dotnet run --project tests/unit/Comuki.Host.Unit.Errors` green; `dotnet run --project tests/unit/Comuki.Modules.Projects.Unit` green unchanged (contract preserved)

## 3. Intake

- [x] 3.1 Re-parent the Intake exceptions (`IntakeTicketNotFoundException`, `SourceConnectionNotFoundException`, `AdmissionRuleNotFoundException`, `IntakeTicketConflictException`) to `DomainException` (codes from the current literals: `intake.ticket_not_found` etc.); add `Errors/Handlers/Intake/` handlers + registration + shape tests from `IntakeEndpointRunner` literals; delete `IntakeEndpointRunner`, `IntakeProblems` and the per-file `IntakeValidationErrors` copy in the same commit (ValidationException arm just disappears — the core handler owns it). Verify: `dotnet run --project tests/unit/Comuki.Host.Unit.Errors`; `dotnet run --project tests/unit/Comuki.Modules.Intake.Unit` green unchanged

## 4. Scheduler

- [x] 4.1 Same discipline for `SchedulerEndpointRunner`: re-parent its typed exceptions, handlers + registration + shape tests, delete the runner in the same commit. Verify: `dotnet run --project tests/unit/Comuki.Host.Unit.Errors` + the Scheduler module's unit project green unchanged

## 5. Chat

- [x] 5.1 Same discipline for `ChatEndpointRunner` (`ChatApprovePendingException` — a host-held domain exception, re-parents like the module ones per design D1). Verify: `dotnet run --project tests/unit/Comuki.Host.Unit.Errors` + `dotnet run --project tests/unit/Comuki.Modules.Chat.Unit` green unchanged

## 6. Runs

- [x] 6.1 Same discipline for the three `TypedResults.Problem` blocks in `RunsController` (`RunDecisionConflictException` re-parents; keep its 409 + current detail sentence and any extensions it ships today). Verify: shape tests in `Comuki.Host.Unit.Errors` + the Runs-related host/engine unit projects green unchanged

## 7. Learning + Knowledge

- [x] 7.1 Same discipline for `LearningCandidatesController` (three problem blocks) and `KnowledgeModuleEndpoints` (three). Verify: `dotnet run --project tests/unit/Comuki.Host.Unit.Errors` + the Learning/Knowledge module unit projects green unchanged

## 8. Workers (last HTTP surface — SDK-facing)

- [x] 8.1 Migrate `WorkersReadEndpoints` (three blocks) and `WorkerResults` (two): re-parent, handlers, shape tests, delete catch blocks in one commit. The worker SDK consumes these shapes for retry decisions — before merging, diff the handler literals against the removed code line by line. Verify: `dotnet run --project tests/unit/Comuki.Host.Unit.WorkersRead` + `Comuki.Host.Unit.WorkerRuntime` green unchanged
- [x] 8.2 Migrate `WorkerEndpoints` (validation arm) and `UploadArtifactEndpoint` (catch blocks only — the `ParsedMultipartFile.WithFailure` value flow stays value-based). Verify: `dotnet run --project tests/unit/Comuki.Host.Unit.WorkerRuntime` green; `dotnet build comuki.slnx -c Debug`

## 9. Proxy + cleanup

- [x] 9.1 Migrate `ProxyKeyAdminEndpoints` (two blocks). Verify: `dotnet run --project tests/unit/Comuki.Host.Unit.Errors` green
- [x] 9.2 Cleanup: grep `platform/src/host` for leftover `catch (` over typed exceptions and for dead `*Problems`/`*EndpointRunner` helpers (expect zero outside `RequiresPermissionFilter` and value-based flows); update the `ProviderExceptionHandler` doc comment — the "until PR #20" transitional note becomes the retirement record. Verify: `rg -n "EndpointRunner" platform/src/host` returns nothing; build green

## 10. Gates

- [x] 10.1 `dotnet format comuki.slnx --severity hidden` then `dotnet build comuki.slnx -c Debug` → 0 warnings / 0 errors
- [x] 10.2 Full affected test set: `dotnet run --project tests/unit/Comuki.Host.Unit.Errors`, the migrated modules' unit projects, `dotnet run --project tests/Comuki.Architecture.Tests --no-build` → all green (no new cross-layer references)
- [x] 10.3 FE no-change proof: `cd dashboard && bun run typecheck && bun run lint && bun run test` → all exit 0 with ZERO dashboard file edits (contract preserved by construction; if any dashboard change seems needed, STOP and report — that is a contract drift bug, not a task) — **verified by the parent**: the error-contract slices made zero dashboard edits (git-confirmed); FE suite green on the change's own final run (2115/2115, worker T3) and the two later full-suite reds were environmental timeouts (post-shutdown machine degradation, tests pass in isolation 8/8) — documented, not a contract drift.
- [x] 10.4 Self-audit per `worker-audit.md` §2b on the changed files (naming, private methods, `_ =` discards, `List<T>` surfaces, ThrowIf) — findings fixed or justified in the commit

## 1. Module skeleton

- [ ] 1.1 Create `Comuki.Modules.ScheduledJobs.{Domain,Application,Infrastructure}` projects, add to `comuki.slnx` by hand, wire ProjectReferences, and verify `dotnet sln comuki.slnx list` shows all three
- [ ] 1.2 Add `ScheduledJobsDbContext` (schema `scheduled_jobs`), installer `AddScheduledJobsModule`, and a Migrator loop entry; verify a Debug build of the three projects succeeds

## 2. Domain + persistence

- [ ] 2.1 Implement `ScheduledJob` aggregate (slug, cron, timezone, profile, brief, secretEnvRefs, overlap=skip, timeout, nextFireAt, lastVerdict, soft archive) with factory validation; verify unit tests reject empty slug, empty brief, unknown timezone and unparseable cron
- [ ] 2.2 Add `jobs` + `firings` configurations (snake_case, max lengths, unique `(project_id, slug)`), generate the migration with `dotnet ef` (never hand-edit); verify migrator creates schema `scheduled_jobs`
- [ ] 2.3 Add Cronos (or chosen parser) in Directory.Packages.props and a `CronNextFire` helper using `TimeProvider`; verify unit tests for `*/15 * * * *` UTC and a DST-crossing IANA zone

## 3. Identity

- [ ] 3.1 Add `schedule:read` / `schedule:write` to `Permissions` and `RoleMatrix` (read: viewer+; write: project-admin+); verify `RoleMatrixShould` catalog-count and “every key held by a role” tests pass

## 4. Launch seam

- [ ] 4.1 Add `IScheduledRunLauncher` port (project, job → RunId) implemented in Host like `IntakeRunLauncher`: one run + one queued work item, brief `{ goal, scheduleId, scheduleSlug, firedAt }`, `scheduled_job_id` on the run; verify a unit test with a fake orchestration context
- [ ] 4.2 Generate the orchestration migration that adds nullable `scheduled_job_id` on `runs` via `dotnet ef`; verify snapshot matches the model

## 5. Dispatcher

- [ ] 5.1 Implement due-query `FOR UPDATE SKIP LOCKED` + overlap check (live attributed run) + secret resolve (skip `secret_unset`) + launch + stamp `nextFireAt` / `lastFiredAt`; verify integration tests: happy fire, overlap skip, unset secret skip, launch failure still advances nextFireAt
- [ ] 5.2 Implement `ScheduledJobDispatcher` hosted service (`AsSystem("schedule-dispatcher")`, `Host:Schedule:{Enabled,Interval}`, failed pass logged not fatal); verify unit test that `Enabled=false` returns immediately and a thrown pass does not stop the loop
- [ ] 5.3 Implement timeout pass: cancel attributed non-terminal runs older than `timeoutSeconds` via existing cancel adapter; verify a hung run becomes `Cancelled` and job verdict `error`

## 6. Verdict + journal

- [ ] 6.1 On attributed run terminal: map status → verdict, honor worker report `verdict` hint when valid, write `lastVerdict`, append `schedule.verdict`, emit transition notification (journal + structured log) only on change; verify consecutive `ok` is silent and ok→fail emits once
- [ ] 6.2 Append `schedule.fired` on launch and persist a `firings` row for both fires and skips (`run_id` null on skip); verify skip has no run id

## 7. REST + host composition

- [ ] 7.1 Controllers under `/api/v1/projects/{projectId}/schedules` (list/create/get/patch/archive/fire) with FV validators, `RequiresPermission`, secret names not values; verify 201 create, 409 duplicate slug, 409 fire overlap, 400 bad cron, 403 member write
- [ ] 7.2 Wire module + dispatcher + launcher + controllers in `HostComposer`; strip the dispatcher in OpenAPI build-time like other hosted services; verify Debug build emits `artifacts/openapi.json` containing the new paths

## 8. Control plane

- [ ] 8.1 Add `control-plane/profiles/ops-sentry.md` (readonly tools, `model: light`, mandatory verdict in output); verify catalog lists key `ops-sentry` and create-job default uses it when profile omitted

## 9. Tests + docs

- [ ] 9.1 Architecture test: ScheduledJobs projects do not reference Engine or Host; verify `Comuki.Architecture.Tests` still green
- [ ] 9.2 Document operator setup (env secrets, `Host:Schedule:*`, overlap/alert behavior) under `.agents/docs/operations/` and mention the slice in STATE/ROADMAP as post-v1; verify links from the GitHub issue
- [ ] 9.3 `dotnet build comuki.slnx -c Debug` and the new unit/integration projects via `dotnet run --project` all exit 0

## Purpose

Time-triggered admission: a project-scoped cron job that, when due, launches
one ordinary run with a pinned profile, a brief template and a scoped secret
env list. The worker stays ephemeral; the schedule lives in the orchestrator.

## ADDED Requirements

### Requirement: Scheduled job aggregate

A scheduled job SHALL belong to exactly one project and SHALL carry: a unique
(per project) kebab-case `slug`, a human `name`, an enabled flag (default
true), a five-field cron expression, an IANA timezone, a worker `profileKey`,
a brief template (non-empty markdown/text), a list of secret env-var names
(the values are never stored), overlap policy `skip` (the only v1.1 value),
alert policy `on-transition` (the only v1.1 value), optional `timeoutSeconds`
(default 300, bounded 30–1800), optional `maxConcurrent` for this job
(default 1), `nextFireAt` (UTC), `lastFiredAt`, `lastVerdict`
(`ok` / `fail` / `error` / null), and timestamps. Creation SHALL compute
`nextFireAt` from cron + timezone at `now`. Empty slug, empty brief, unknown
timezone or unparseable cron SHALL be rejected as 400.

#### Scenario: Create computes next fire

- **WHEN** a job is created with cron `*/15 * * * *` and timezone `UTC` at
  12:01 UTC
- **THEN** `nextFireAt` is 12:15 UTC and `lastVerdict` is null

#### Scenario: Bad cron rejected

- **WHEN** a caller posts cron `every fifteen minutes`
- **THEN** the API answers 400 with a field error on `cron` and no row is
  written

### Requirement: Unique slug per project

`(project_id, slug)` SHALL be unique among non-archived jobs. A duplicate
SHALL answer 409. Slug is immutable after create.

#### Scenario: Duplicate slug refused

- **WHEN** a second job is created on the same project with an existing slug
- **THEN** the API answers 409 and no row is written

### Requirement: Soft archive and disable

Disabling SHALL keep the row, skip dispatcher selection, and leave
`nextFireAt` untouched. Archiving SHALL stamp `archived_at`, exclude the job
from default lists, and skip dispatcher selection. Archiving twice is a
no-op. Enable SHALL recompute `nextFireAt` from `now` so a long-disabled job
does not catch up a backlog of missed ticks.

#### Scenario: Re-enable does not backfill

- **WHEN** a job disabled for three hours is enabled
- **THEN** `nextFireAt` is the next cron instant after now, not the missed
  ticks

### Requirement: Secret env refs, never values

The job SHALL store only environment-variable *names* (`secretEnvRefs`).
Values SHALL be resolved at fire time from the host process environment.
A name that is unset at create/update SHALL fail validation (400). A name
that becomes unset between save and fire SHALL skip the tick, journal
`schedule.skipped` with reason `secret_unset`, and advance `nextFireAt`.
The resolved values SHALL be passed to the worker only as extra env on
that run's start request. They SHALL never appear in the brief, the
journal payload, the REST view, or logs.

#### Scenario: Unset secret at fire skips

- **WHEN** a due job names `KAFKA_BOOTSTRAP` and that env var is unset
- **THEN** no run is launched, `schedule.skipped` is journaled with reason
  `secret_unset`, and `nextFireAt` moves to the next cron instant

#### Scenario: REST view hides values

- **WHEN** a caller GETs a job that has secret env refs
- **THEN** the response lists the names and never the values

### Requirement: Overlap skip

At most one live run (status `Queued`, `Waiting`, `Running`, `Escalated`)
attributed to a job SHALL exist. When the dispatcher finds the job due and
a live run still exists, it SHALL skip, journal `schedule.skipped` with
reason `overlap`, and leave `nextFireAt` as the next cron instant (not
retry immediately). Fire-now SHALL answer 409 `schedule.overlap` in the
same case.

#### Scenario: Due during in-flight run

- **WHEN** a job is due and its previous run is still `Running`
- **THEN** no second run is created and `schedule.skipped` / `overlap` is
  journaled

### Requirement: Dispatcher due query

A host dispatcher SHALL, on a poll interval (default 15 seconds, same
order as the scale supervisor), select enabled non-archived jobs whose
`nextFireAt <= now`, under a `FOR UPDATE SKIP LOCKED` claim so two host
replicas cannot double-fire the same job. For each claimed job it SHALL
either launch a run (and stamp `lastFiredAt`, recompute `nextFireAt`,
journal `schedule.fired`) or skip per overlap/secret rules. A failed
launch SHALL journal `schedule.skipped` with reason `launch_failed`,
advance `nextFireAt`, and not crash the dispatcher loop.

#### Scenario: Two replicas, one fire

- **WHEN** two host processes poll the same due job
- **THEN** exactly one run is launched

#### Scenario: Launch failure does not stall the job

- **WHEN** run creation throws
- **THEN** `nextFireAt` advances and the next poll may try again

### Requirement: Fired run shape

A fired run SHALL be a normal orchestration run with one queued work item.
The work item SHALL carry the job's `profileKey`, the project's worker
image and profiles git-ref, and a brief JSON `{ goal, scheduleId,
scheduleSlug, firedAt }`. `goal` is the brief template with no secret
interpolation. The run SHALL be attributed to the job via
`scheduled_job_id` on the run (or an equivalent attribution row) so
overlap and history can join. The worker image, claim, lease, Translator
and scale path SHALL be unchanged.

#### Scenario: Tick launches one work item

- **WHEN** a due job with profile `ops-sentry` fires
- **THEN** a `Queued` run exists with one work item whose `ProfileKey` is
  `ops-sentry` and whose brief contains the template text and the job id

### Requirement: Verdict and sticky alerts

When a scheduled run reaches a terminal status the platform SHALL record a
verdict on the job: `Succeeded` → `ok`; `Failed` / `Cancelled` → `fail`;
any other terminal including timeout → `error`. Under alert policy
`on-transition` a notification SHALL be emitted only when `lastVerdict`
changes (including null → first fail). Consecutive `ok` ticks SHALL be
silent. The verdict SHALL be journaled as `schedule.verdict` on the run
with `{ from, to, scheduleId }`.

#### Scenario: Repeated ok is quiet

- **WHEN** a job already at `ok` succeeds again
- **THEN** `lastVerdict` stays `ok` and no notification is emitted

#### Scenario: ok to fail alerts

- **WHEN** a job at `ok` lands `Failed`
- **THEN** `lastVerdict` becomes `fail` and one transition notification is
  emitted

### Requirement: Timeout

If `timeoutSeconds` elapses while the attributed run is non-terminal, the
host SHALL cancel the run (existing cancel path) and record verdict
`error`. Timeout SHALL count as a transition from `ok`/`fail` like any
other verdict change.

#### Scenario: Hung sentry is cancelled

- **WHEN** a scheduled run is still `Running` after `timeoutSeconds`
- **THEN** the run is cancelled and the job verdict is `error`

### Requirement: REST surface

Jobs live under `/api/v1/projects/{projectId}/schedules`:

- `GET /` — list (skips archived unless `includeArchived=true`);
  `schedule:read`
- `POST /` — create; `schedule:write`; 201
- `GET /{scheduleId}` — detail + last N firings; `schedule:read`
- `PATCH /{scheduleId}` — partial update (name, cron, timezone, profile,
  brief, secretEnvRefs, timeout, enabled); slug immutable; write
- `POST /{scheduleId}/archive` — 204; write
- `POST /{scheduleId}/fire` — enqueue immediately if no overlap; 201 with
  run id, or 409 overlap; write

List/detail SHALL NOT return secret values. Cron changes SHALL recompute
`nextFireAt` from now.

#### Scenario: Fire now

- **WHEN** an operator posts fire on an idle enabled job
- **THEN** a run is launched and 201 returns its id, regardless of
  `nextFireAt`

#### Scenario: Fire now overlap

- **WHEN** fire is posted while a live attributed run exists
- **THEN** the answer is 409 `schedule.overlap` and no run is created

### Requirement: Persistence layout

Scheduled jobs SHALL live in Postgres schema `scheduled_jobs` with their
own `__ef_migrations_history` table, matching the one-schema-per-DbContext
rule. Tables: `jobs`, `firings` (append-only: job id, run id, fired_at,
skip_reason nullable, verdict nullable). The migrator SHALL apply this
schema in the existing EnsureSchema → MigrateAsync loop.

#### Scenario: Migrator applies scheduled_jobs schema

- **WHEN** the migrator runs
- **THEN** schema `scheduled_jobs` and tables `jobs` / `firings` exist
  without colliding with other module histories

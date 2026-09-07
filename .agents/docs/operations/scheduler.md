# Scheduler (S15) — operator guide

The scheduler turns time into an admission source: a project-scoped cron
expression fires one ordinary ephemeral worker (profile + brief + scoped
secret env) per tick. The dispatcher is a host `BackgroundService`
(`ScheduledJobDispatcherWorker`) wired through `AddSchedulerPersistence`
and `AddSchedulerApplication` in `HostComposer.Compose`.

> Slice: S15 — Scheduled jobs / sentry (issue #44). Modules touched:
> `Scheduler`, `Host`, `Shared.Telemetry`.

## Surface

```
Scheduler:Sentry:Dsn (optional)
        │
        ▼
Program.cs → SchedulerSentryBootstrap.TryInitialize
        │  (no-op when DSN unset)
        ▼
HostComposer.Compose
   ├─ AddSchedulerPersistence     → SchedulerDbContext, ISchedulerStore,
   │                                IEnumerable<ISchedulerObserver>
   │                                (JournalSchedulerObserver, SentrySchedulerObserver)
   └─ AddSchedulerApplication     → ScheduledJobService, SchedulerOptions

Poll loop:
  ScheduledJobDispatcherWorker (BackgroundService)
    ├── ListDueAsync(now, batch) ── FOR UPDATE SKIP LOCKED (Postgres)
    ├── ISchedulerDispatcher.DispatchAsync(job) ── host-composed port
    ├── job.MarkFired(now) ── next-fire recompute
    ├── store.UpdateAsync(job)
    └── NotifyObserversAsync:
          ├── JournalSchedulerObserver  → IRunJournal.AppendAsync(runId,
          │                                "scheduler.job_fired", payload)
          └── SentrySchedulerObserver   → SentrySdk.CaptureEvent(event)
                                         (no-op when Scheduler:Sentry:Dsn is empty)
```

## Quick health checks

| Probe | What it tells you |
|-------|-------------------|
| `GET /health/ready` | Postgres reachable (existing `PostgresHealthCheck`); `ProxyKeysHealthCheck`. Scheduler has no dedicated health check yet — the dispatcher logged startup is the proxy. |
| Worker log line `Scheduled job dispatcher started (poll interval {N}s, batch {B})` | Worker booted. Default poll is 30s (`Scheduler:PollInterval`). |
| Worker log line `Scheduled job dispatcher fired {Count} job(s)` | A poll cycle fired at least one job. Count is per-cycle. |
| Worker log line `Scheduled job {JobId} fired run {RunId} (profile={ProfileKey}, cron={CronExpression})` | Per-fire trail. Joins on `RunId` land on the run's journal. |
| Worker warning `Scheduler observer {Type} threw; fire is unaffected` | An observer failed (journal write or Sentry capture). The fire itself succeeded. |
| `GET /api/v1/projects/{projectId}/schedules/{scheduleId}` | Job state — `enabled`, `nextFireAt`, `lastFiredAt`, `lastVerdict`. |

The host startup log includes the scheduler startup at the same `Information`
level as the other dispatchers (`LeaseReaper`, `RunArtifactPackager`,
`OidcStateSweeper`, `KnowledgeIngest`). No scheduler entry in
`/health/ready` is intentional; an unhealthy Postgres already trips the
`ready` probe and the dispatcher logs its own failures per cycle.

## Configuration

`SchedulerOptions` binds from the `Scheduler` section. The dispatcher
tunables:

| Key | Type | Default | Notes |
|-----|------|---------|-------|
| `Scheduler:PollInterval` | `TimeSpan` | `00:00:30` | Polling cadence. The dispatcher's `ExecuteAsync` cycle waits this long after every pass. `00:00:05` minimum recommended in tests; `00:01:00` for low-frequency health probes. |
| `Scheduler:BatchSize` | `int` | `50` | Per-cycle cap. The dispatcher never claims more than this in one transaction. |
| `Scheduler:Worker:Image` | `string` | `ghcr.io/comuki/worker:dev` | Worker image scheduled jobs claim on. Pinned per-project settings in a later slice. |
| `Scheduler:Worker:ProfilesRef` | `string` | `refs/heads/main` | Pinned git ref of the profiles repo. |

`Scheduler:Sentry` (the side-channel):

| Key | Type | Default | Notes |
|-----|------|---------|-------|
| `Scheduler:Sentry:Dsn` | `string?` | `null` | Sentry DSN. When null/whitespace the Sentry observer is a no-op and `SentrySdk.Init` is never called — no transport thread enters the process. |
| `Scheduler:Sentry:Environment` | `string?` | `null` | Optional. Forwarded to `SentrySdk.Init` so events land in the matching project stream (`production`, `staging`). |

### Sample appsettings.json snippet

```jsonc
{
  "Scheduler": {
    "PollInterval": "00:00:30",
    "BatchSize": 50,
    "Worker": {
      "Image": "ghcr.io/comuki/worker:dev",
      "ProfilesRef": "refs/heads/main"
    },
    "Sentry": {
      "Dsn": "https://key@example.com/1",
      "Environment": "production"
    }
  }
}
```

In dev / CI omit `Scheduler:Sentry` entirely — the dispatcher runs with
the journal observer alone and zero Sentry footprint.

## Enabling Sentry

1. **Create the project.** In Sentry UI, create a new project for
   `comuki-orchestrator` (platform: .NET). Copy the DSN — the URL
   starting with `https://…@…/…`.
2. **Wire the env var.** The host reads via `AddOptions<SchedulerSentryOptions>`
   bound from `Scheduler:Sentry:Dsn`. In compose deploys:

   ```yaml
   environment:
     Scheduler__Sentry__Dsn: "https://key@example.com/1"
     Scheduler__Sentry__Environment: production
   ```

   The double-underscore is the .NET environment-variable escape for
   `:` (see `~/.agents/rules/csharp/configuration-toml-env.md`).
3. **Verify the SDK initialised.** On boot, the host logs no Sentry
   line (the SDK initialises silently). Failure to initialise (invalid
   DSN, no DNS) is surfaced by the SDK itself in its own log
   channel — wire that to your log sink. The dispatcher continues
   regardless; the journal observer still fires.
4. **Confirm events land in Sentry.** Wait one poll interval, then in
   Sentry's Issues feed filter by tag `scheduler.profile_key=ops-sentry`
   (or whatever profile the dispatched run claimed). Every fire
   produces one `Info`-level event tagged with `scheduler.job_id`,
   `scheduler.project_id`, `scheduler.profile_key`. The event message
   is the literal string `scheduler.job_fired` so alert rules can
   pin to it.

If the DSN is empty or whitespace, `SchedulerSentryBootstrap.TryInitialize`
is a no-op and the Sentry observer's body short-circuits before
`SentrySdk.CaptureEvent`. Operators can confirm this is the case by
inspecting the `comuki.scheduler.dispatcher` log channel for the absence
of `Scheduler observer {Type} threw` warnings during a known fire — but
a cleaner probe is the absence of the Sentry SDK in process memory
(`pmap | grep sentry` shows no Sentry SDK mappings when the DSN is
empty).

## Disable the dispatcher (killswitch)

There is no per-project kill switch — the dispatcher is host-wide. To
stop firing globally:

- Set `Scheduler:PollInterval` to a very large value (e.g.
  `01:00:00`) — the dispatcher still boots but fires every hour.
- Or remove the `AddSchedulerPersistence` call from `HostComposer.Compose`
  and rebuild — the hosted service stops registering. This is a code
  change, not a config flip.

For a per-job killswitch, archive the job via `POST
/api/v1/projects/{projectId}/schedules/{scheduleId}/archive` — the
dispatcher skips archived jobs.

## Verifying Sentry delivery end-to-end

After wiring the env var, a one-shot cron job is the cleanest probe:

```bash
# Create a job that fires every minute with an empty profile list
# (the default ops-sentry profile is selected when omitted).
curl -X POST http://localhost:17173/api/v1/projects/$PROJECT_ID/schedules \
  -H 'Cookie: comuki.session=…' \
  -H 'Content-Type: application/json' \
  -d '{"name":"sentry-probe","cronExpression":"* * * * *","profileKey":"ops-sentry","briefJson":"{}","enabled":true,"secretEnvRefs":[]}'
```

Wait one minute. In Sentry, filter by `environment=production` (or whatever
you set) and look for `scheduler.job_fired`. If it lands, the wiring is
correct. If not, check:

1. `Scheduler:Sentry:Dsn` is non-empty (`env | grep Scheduler__Sentry`).
2. The host log channel for Sentry SDK init errors.
3. `Scheduler:PollInterval` — if it's larger than a minute the probe
   may not have fired yet.

Delete the probe job once verified:

```bash
curl -X POST http://localhost:17173/api/v1/projects/$PROJECT_ID/schedules/$JOB_ID/archive \
  -H 'Cookie: comuki.session=…'
```

## What the dispatcher writes to Sentry

Each fire is one event:

| Field | Value |
|---|
| `message` | `scheduler.job_fired` (literal — alert rules target this) |
| `level` | `info` |
| tag `scheduler.job_id` | UUID of the fired scheduled job |
| tag `scheduler.project_id` | UUID of the project |
| tag `scheduler.profile_key` | Catalog profile key the run claimed on |
| extra `scheduler.run_id` | UUID of the launched orchestration run |
| extra `scheduler.fired_at` | `DateTimeOffset` (UTC) the dispatcher stamped the fire at |
| `environment` | `Scheduler:Sentry:Environment` if set, otherwise the SDK default |

`info`-level events do not fire Sentry's default email/Slack alerts.
Operators who want noise on the channel wire a Sentry alert rule that
matches `level: info AND tags.scheduler.profile_key: ops-sentry` (or
whichever profiles are interesting).

## Alert policy (operator-side, not platform-owned)

The platform does not emit alerts on transition; the dispatcher is a
silent normal-events source. Operators who want a per-job "ok→fail
alerts" pipeline wire it on the Sentry side:

1. Subscribe to the events by `tags.scheduler.job_id`.
2. Track the previous `lastVerdict` (journal query) and emit a Sentry
   alert only when the verdict flips.

This keeps the scheduler dispatcher small (one capture per fire, no
state machine on the dispatcher side) and lets operators tune the
noisy / quiet boundary in Sentry without code changes. A subsequent
slice may add a verdict-pipeline notification sink on the host
(`schedule.verdict` events are already in the journal contract);
see `openspec/changes/add-scheduled-jobs/specs/runs/spec.md`.

## See also

- `openspec/changes/add-scheduled-jobs/specs/scheduled-jobs/spec.md` —
  the dispatch + journal contracts (Requirement: *Scheduler fire emits
  a journal event*, *Scheduler fire forwards to Sentry when DSN
  configured*).
- `openspec/changes/add-scheduled-jobs/specs/runs/spec.md` —
  `schedule.fired` / `schedule.skipped` / `schedule.verdict` payload
  shapes that share the dispatcher's downstream contract.
- `.agents/docs/operations/oauth-oidc.md` — OIDC login (sibling
  cross-cutting concern).
- `control-plane/profiles/ops-sentry.md` — the default profile
  scheduled jobs are pinned to.
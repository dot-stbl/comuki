## Why

Operators need Comuki to run a worker on a clock — Kafka lag, log greps,
health probes — without a long-lived agent. v1 only admits work from
chat, native tickets and tracker webhooks. Time as an admission source is
the missing intake. This is post-v1 (v1.1), not fleet (#11).

## What Changes

- New module `ScheduledJobs`: cron jobs on a project that enqueue a normal
  run (profile + brief + secret env) when due.
- Host dispatcher (`BackgroundService` + due-query), not Hangfire/Quartz.
- Control-plane profile `ops-sentry` (readonly, cheap model, short timeout).
- REST CRUD + enable/disable + fire-now + firing history.
- Permissions `schedule:read` / `schedule:write`.
- Journal types `schedule.fired` / `schedule.skipped` / `schedule.verdict`.
- Sticky alert policy: notify on ok↔fail, not every tick.

## Capabilities

### New Capabilities
- `scheduled-jobs`: cron jobs, dispatcher, overlap, verdicts, REST, secrets

### Modified Capabilities
- `identity`: `schedule:read` / `schedule:write` on the role matrix
- `runs`: schedule journal event types
- `control-plane`: `ops-sentry` profile
- `host`: dispatcher hosted service + composition

## Impact

- New `platform/src/modules/ScheduledJobs/` (Domain / Application /
  Infrastructure) + schema `scheduled_jobs`.
- Host: `ScheduledJobDispatcher`, `ScheduledJobRunLauncher`, controllers.
- Identity: two permission keys; project-admin+ write, viewer+ read.
- Control-plane: `profiles/ops-sentry.md`.
- Dashboard: mock-first page (follow-up, not this slice's DoD).
- Migrator: new DbContext in the schema loop.

## Non-goals

- Long-lived watch containers or in-worker cron.
- Hangfire / Quartz / Redis lock (Redis stays #11 multi-replica).
- Kafka/Grafana/k8s MCP tools (jobs use existing worker tools + injected
  env; domain MCP is a later slice).
- Auto-remediation that writes infra (sentry is diagnose + escalate).
- FE wire-up of the jobs page (API + mock seed only).
- Changing claim/lease/Translator/compute providers.

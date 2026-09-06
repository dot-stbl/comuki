## Context

See proposal.md for motivation. Today the only ways a run is born are
chat, native tickets and tracker webhooks (`IRunLauncher` composed in
the host). Time is not an admission source. Workers are ephemeral
functions `(brief + env) → report`; a long-lived “watch” container
would break that contract (heartbeat would mean liveness, not work).

Constraints that shape the design:

- One Host replica in v1; Redis lock is #11. `FOR UPDATE SKIP LOCKED`
  on the job row is enough for a future second replica.
- Modules must not reference the engine. Intake already launches runs
  through a host-composed port — scheduled jobs copy that seam.
- Secrets never live in Postgres (intake `secretEnvRef` pattern).
- `ComputeStartRequest.Env` already carries extra env — no compute
  contract change.
- Heavy schedulers (Hangfire/Quartz) are banned unless durable
  scheduling is declared. A `BackgroundService` + due-query is the
  pinned path (≤3 similar workers already exist: reaper, OIDC sweep,
  artifact packager).

## Goals / Non-Goals

**Goals:**

- Cron as a first-class admission source that reuses run / claim /
  lease / Translator / artifacts unchanged.
- Overlap-skip and sticky alerts so a 15-minute Kafka check cannot
  DDoS itself or spam.
- Secret env injected only at container start, never in brief/journal.
- Permission split so viewers see jobs, only project-admin+ mutate.

**Non-Goals (design-level):**

- In-worker cron or sidecar watchers.
- Domain MCP (kafka admin, grafana, kubectl) — the worker uses Bash /
  existing tools plus injected env. Tools come later as skills.
- Multi-replica fencing beyond SKIP LOCKED.
- Interpolating secrets into the brief template.
- Auto-remediation writes (restart broker, scale CG).

## Decisions

### 1. New module, not a new Intake provider

**Choice:** `Comuki.Modules.ScheduledJobs` with its own schema.

**Why:** Intake is tracker-shaped (`IncomingTicket`, signatures,
admission rules). A cron job has no external id, no webhook, no
sync-back. Forcing it through `SourceConnection` would distort both
models. The *shared* piece is the host run-launcher port, not the
ticket aggregate.

**Rejected:** “schedule” as an Intake provider kind. Would couple
cron validation, overlap and verdicts to webhook idempotency.

### 2. Dispatcher in Host, claim in SQL

**Choice:** `ScheduledJobDispatcher` hosted service, one pass per
`Host:Schedule:Interval` (default 15s). Pass: `SELECT … FROM jobs
WHERE enabled AND archived_at IS NULL AND next_fire_at <= now
FOR UPDATE SKIP LOCKED`, then launch or skip, then stamp
`next_fire_at`.

**Why:** Same pattern as `LeaseReaperWorker` / `ScaleSupervisorWorker`.
Cron next-occurrence is computed in-process (NCrontab or
Cronos — pick one library at implement time; do not hand-roll).
Timezone via `TimeZoneInfo.FindSystemTimeZoneById` (IANA on Linux,
Windows conversion through the same API on net10).

**Rejected:** Hangfire/Quartz (banned unless declared; adds a store).
Rejected: fire from inside the worker (overlap and secrets would live
in the container).

**Clock:** injected `TimeProvider`. `next_fire_at` stored as UTC.

### 3. Attribution: nullable `scheduled_job_id` on `runs`

**Choice:** add `scheduled_job_id uuid null` on `orchestration.runs`
(FK is logical, not cross-schema physical — modules don't share FK
across DbContexts). Overlap query: live runs with that id.

**Why:** one join, visible on the run timeline. A side table
`firings` still records every tick (including skips with `run_id`
null) so history does not depend on run rows.

**Rejected:** encoding the job id only in brief JSON — overlap would
parse jsonb.

### 4. Secrets: env-name list, resolved at fire

**Choice:** copy intake `secretEnvRef`, but a *list* (Kafka bootstrap
+ JAAS + truststore). Create/update validate names exist. Fire-time
miss → skip, not launch a half-credentialed worker.

**Injection:** host launcher copies resolved values into
`ComputeStartRequest.Env`. Scale supervisor already forwards `Env`.
Translator does not log env.

**Rejected:** Vault/file refs in v1.1 (env-only is the project
secret rule). Rejected: putting values in job settings jsonb.

### 5. Overlap = skip, not queue

**Choice:** one live run per job. Due-during-inflight → skip and
advance the clock.

**Why:** a 15-min check that takes 20 min must not pile up. Operators
who want catch-up use fire-now after the stall clears.

**Rejected:** `overlap=queue` (unbounded backlog). Rejected:
`overlap=cancel-previous` (kills a still-useful diagnosis).

### 6. Verdict is orchestrator-owned, with a worker hint

**Choice:** terminal run status maps to verdict (`Succeeded`→ok,
`Failed`/`Cancelled`→fail, timeout/other→error). The worker SHOULD
emit a structured `verdict` field in its report; when present and
valid it wins over the status mapping so a run that “succeeded” at
the process level but found lag>threshold can still be `fail`.

**Why:** “LLM proposes — system disposes”. We do not trust the model
alone, but we do let it classify *findings* when the process is
green. Timeout is always `error` (cancel path), ignoring the hint.

Alert: `on-transition` only. Notification sink in v1.1 is journal +
structured log; a later slice can fan-out to chat / tracker.

### 7. Profile `ops-sentry`, job may pin any profile

**Choice:** ship `control-plane/profiles/ops-sentry.md` (readonly,
`model: light`). Jobs default to that key on create if omitted, but
the column is a free catalog key so a team can reuse `explore-readonly`.

**Why:** sentry is a role, not a runtime. Allowed-tools live in the
profile pack (git-ref pinned per run, existing control-plane rule).

### 8. Permissions: write is project-admin+, not member

**Choice:** `schedule:read` from viewer up; `schedule:write` from
project-admin up. Member has `source:write` today but not schedule
write.

**Why:** cron + secret env names is closer to settings than to
opening a tracker. Member can still fire work via tickets/chat.

### 9. Timeout via existing cancel

**Choice:** dispatcher (or a tiny sibling pass) selects attributed
runs older than `timeout_seconds` and calls the host cancel adapter
already used by `POST /runs/{id}/cancel`.

**Why:** no new stop protocol. Translator already honors Stop.

### 10. Cron library

**Choice:** one maintained parser (Cronos preferred: IANA zones, no
seconds field by default — matches five-field cron). Pin in
Directory.Packages.props at implement time.

**Rejected:** 6-field cron with seconds (operators think in minutes).
Minimum interval enforced: next fire at least 60s after last fire
even if cron says `* * * * *`? **No** — allow `* * * * *` but document
cost; budget gate still applies. Overlap-skip is the backstop.

## Risks / Trade-offs

- **[Missed ticks while host is down]** → `next_fire_at` is in the
  past on boot; the next pass fires once, then advances. No catch-up
  storm. Operators who need the missed window use fire-now.
- **[Secret in extra env leaked via `docker inspect`]** → accepted for
  v1.1; same as worker token. Mitigate later with files in a tmpfs.
  Brief/journal/API remain clean.
- **[Worker “succeeds” but findings are red]** → structured verdict
  hint in the report; without it, process success = ok (false quiet).
  Prompt of `ops-sentry` makes the field mandatory in the output
  contract.
- **[Cost of noisy cron]** → default profile is `light`, timeout 5
  min, maxConcurrent 1, project hard budget still cancels. No
  per-job budget in v1.1.
- **[Windows vs IANA timezone]** → `TimeZoneInfo` on net10 converts;
  store IANA ids (`Europe/Moscow`). Reject Windows ids at validation.
- **[Query-filter vs dispatcher]** → `AsSystem("schedule-dispatcher")`
  required, same as the lease reaper. Missing it would silently
  select zero jobs.
- **[Cross-schema attribution]** → no physical FK from
  `orchestration.runs` to `scheduled_jobs.jobs`. Integrity is
  application-level; deleting a job (archive) leaves historical runs.

## Migration Plan

1. Add module + DbContext + tool-generated migration; register schema
   in Migrator.
2. Add nullable `scheduled_job_id` on runs via orchestration
   migration (`dotnet ef`, never hand-edit).
3. Identity: two permission keys + matrix rows + catalog test
   (count assertion will fail until updated).
4. Host: dispatcher, launcher, controllers, OpenAPI (Debug emission).
5. Control-plane: `ops-sentry.md`.
6. Deploy: document `Host:Schedule:Enabled` (default true) and that
   job secrets are host env vars, same as source secrets.

Rollback: `Host:Schedule:Enabled=false` stops firing; tables can stay.
Archive is enough to disable a single job.

## Open Questions

- Notification fan-out (chat message vs tracker comment) — v1.1 is
  journal + log only; sink is a later slice and does not change the
  verdict contract.
- Per-job budget cap — defer; project hard budget is the backstop.
- Whether fire-now should reset `nextFireAt` (currently: no, cron
  stays independent of manual fires).

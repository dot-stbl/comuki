# Storage

The orchestrator keeps three persistent stores. This document
records what lives where and the retention policy for each.

> Per-context Postgres schema layout and the Migrator loop that
> creates each one — see [database-schemas.md](./database-schemas.md).
> The bucket topology, lifecycle policy, and host config — see
> [minio.md](./minio.md).

## Postgres

The platform's transactional store. Holds runs, work items, the
run journal, identity, projects, costs, chat sessions, intake
metadata, memory embeddings and the artifact bookkeeping. Tables
live in eight per-DbContext schemas (see
[database-schemas.md](./database-schemas.md)).

- **Backups** — operator concern (pg_dump / WAL archiving);, not in
  scope for the platform.
- **Retention** — none enforced at the platform layer. Rows stay
  until the operator prunes.

## MinIO (S3) — `comuki-run-bundles` bucket

Per-run artifact bundles (issue #28). One bucket; every object key
is scoped by project + run id:

```
{projectId}/{runId}/brief.json
{projectId}/{runId}/result.json
{projectId}/{runId}/pins.json
```

`{projectId}/{runId}` is the canonical scope unit — the read path
of one project never reaches another's run namespace.

### v1 contents

Uploaded by the host-composed packager when a run reaches a terminal
status (succeeded / failed / cancelled / escalated):

- `brief.json` — the work item brief the worker runtime received.
- `result.json` — the worker terminal payload (result / failure
  detail). Absent when no work item drove the transition.
- `pins.json` — version pins (status, occurredAt) for the bundle.

### Retention — v1: never delete

The orchestrator does **not** enforce retention on the artifact
store. A run bundle is written exactly once, when the run goes
terminal, and stays until an operator prunes it.

**v1 commitment:** "never delete" — the platform keeps every
artifact indefinitely. The compose `minio-init` job still
configures a 30-day non-current-version lifecycle on the bucket
so accidentally-enabled versioning does not accumulate forever, but
no current-version object is expired by the platform.

This is the conservative choice: thousands of runs × heavy diffs
do not fit in a laptop SSD, but deleting a bundle early is
irreversible. Once we have observability on bundle size in
production (issue #28 follow-up), the retention policy tightens:

- **v2 candidate (post-#28):** keep the `brief.json` / `result.json` /
  `pins.json` trio for 90 days; keep the worker's git diff and log
  URIs (when wired) for 30 days; older bundles get tombstoned with
  a `retained-until` journal row and the binary objects deleted.
- **Operational knob:** `Artifacts:RetentionDays` in the host
  config; minio-init's lifecycle rule updates from the same source.

### Bootstrap (compose)

The `minio-init` job creates the bucket on first boot and applies
the 30-day non-current-version lifecycle rule. Idempotent: re-running
`podman compose up -d` does not error when the bucket already
exists.

## VictoriaMetrics / VictoriaLogs

Time-series and log retention live here. The compose deploy already
configures `--retentionPeriod=1` (one month). The platform does not
own retention tuning; the deploy image pins the policy.

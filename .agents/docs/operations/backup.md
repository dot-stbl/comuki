# Backup & restore (issue #10 T11.1)

Procedural companion to [runbook.md](./runbook.md). The on-call guide
references this file for the per-store backup commands; restore
sequences also live here.

> Run every backup script as the **same database / bucket user** the
> orchestrator uses. Postgres' `pg_dump` works without elevated
> privileges but writes a complete logical copy of every per-DbContext
> schema; MinIO's `mc mirror` operates on the bucket namespace the
> access key has access to.

## What to back up

| Component | Tool | Where it lives | Restored to |
|-----------|------|----------------|-------------|
| Postgres (orchestrator database) | `pg_dump` | every per-DbContext schema (`orchestration`, `identity`, `projects`, `memory`, `chat`, `intake`, `costs`, `artifacts`) | a fresh empty database; migrations apply on top |
| MinIO (`comuki-run-bundles` bucket) | `mc mirror` | one bucket; every `{projectId}/{runId}/{brief,result,pins}.json` | a fresh MinIO with the same access / secret keys |
| Host config (optional) | tar / git | the secret-bearing config files (`appsettings.json`, `.env`, k8s Secret manifest) | the orchestrator config storage (Vault / Secrets Manager / k8s) |

Secrets (OIDC client secrets, bootstrap admin password, MinIO access /
secret keys) are NOT part of the backup. The assumption is that the
config layer (Kubernetes Secrets, HashiCorp Vault, AWS Secrets
Manager) is already replicated. If `appsettings.json` carries the
secrets directly, treat that file as a sensitive artifact.

## Postgres — `pg_dump`

The orchestrator runs eight DbContext migrations history tables
(`orchestration.__ef_migrations_history`, `identity.__ef_migrations_history`,
`projects.__ef_migrations_history`, `memory.__ef_migrations_history`,
`chat.__ef_migrations_history`, `intake.__ef_migrations_history`,
`costs.__ef_migrations_history`, `artifacts.__ef_migrations_history`)
inside the same database. Each schema keeps its own history — the
migrator applies per-context migrations independently, so a partial
restore that loses only the migrations table for one context can be
recovered by running `Comuki.Migrator` against the restored database.

### Daily dump (cron, systemd timer, etc.)

```bash
DUMP_DIR=/var/backups/comuki/postgres
mkdir -p "$DUMP_DIR"

pg_dump \
  --host=localhost \
  --username=comuki \
  --dbname=comuki \
  --no-owner \
  --no-privileges \
  --format=custom \
  --file="$DUMP_DIR/comuki-$(date +%Y%m%d-%H%M%S).dump"
```

The `--format=custom` output is compressed and binary-safe — `pg_dump`
recognizes the schema layout correctly, and `pg_restore` reads the
output without further parsing. The output filename includes the
timestamp; a cron rotate-and-prune can keep the last 30 days on disk.

### Restoring

```bash
# 1. Create a clean target database
psql --host=localhost --username=postgres \
  -c "DROP DATABASE comuki;"
psql --host=localhost --username=postgres \
  -c "CREATE DATABASE comuki OWNER comuki;"

# 2. Load the dump
pg_restore \
  --host=localhost \
  --username=comuki \
  --dbname=comuki \
  --no-owner \
  --no-privileges \
  --clean --if-exists \
  /var/backups/comuki/postgres/comuki-20260904-020000.dump

# 3. Apply any pending migrations beyond the dump's schema
export COMUKI_DB="Host=localhost;Database=comuki;Username=comuki;Password=..."
dotnet run --project platform/src/host/Comuki.Migrator
```

`pg_restore --clean --if-exists` drops the per-DbContext schemas on
the target before loading — destructive. Always run against a fresh
or recovery-only database, never against a live host.

## MinIO — `mc mirror`

The `comuki-run-bundles` bucket is the only MinIO artifact the host
writes. Every object key is `{projectId}/{runId}/{relativePath}` —
the relative path is opaque to the host (it carries `brief.json`,
`result.json`, `pins.json`).

### Daily mirror

```bash
MINIO_ALIAS=local
BACKUP_DIR=/var/backups/comuki/minio

mkdir -p "$BACKUP_DIR"

mc alias set $MINIO_ALIAS http://minio:9000 \
  "$MINIO_ACCESS_KEY" "$MINIO_SECRET_KEY"

# Mirror every object in the run-bundles bucket to a local directory.
# The bucket names are configurable via deploy/.env's
# COMUKI_MINIO_ARTIFACTS_BUCKET — default is comuki-run-bundles.
mc mirror --remove --preserve \
  "$MINIO_ALIAS/comuki-run-bundles/" \
  "$BACKUP_DIR/comuki-run-bundles/$(date +%Y%m%d-%H%M%S)/"
```

`--preserve` keeps the source directory layout; `--remove` drops
local files that no longer exist in the source. The destination
directory includes the timestamp so consecutive mirrors don't
clobber each other.

> The `--remove` flag deletes files from the destination. If the
> backup target shares a filesystem with anything else, **move the
> backup out of the live path first**.

### Restoring

```bash
mc mirror --overwrite --remove \
  "$BACKUP_DIR/comuki-run-bundles/20260904-020000/" \
  "$MINIO_ALIAS/comuki-run-bundles/"
```

`--overwrite` matches the source against the destination by name —
existing objects are overwritten with the backup's content; missing
ones in the destination are added. `--remove` drops destination
objects the backup doesn't carry (destructive — keep a copy of the
destination before restoring if you might want to roll back).

## Configuration (optional)

If the deployment uses `appsettings.json` or a mounted `.env` to
carry secrets, back those files too. They contain real production
credentials and must not land in the same backup tarball as the
Postgres / MinIO dumps without encryption.

```bash
tar -czf /var/backups/comuki/config/$(date +%Y%m%d-%H%M%S).tgz \
  --exclude='*.log' \
  deploy/.env platform/src/host/Comuki.Host/appsettings.json
```

Encrypt before transport:

```bash
gpg --symmetric --cipher-algo AES256 \
  --output /var/backups/comuki/config/20260904-020000.tgz.gpg \
  /var/backups/comuki/config/20260904-020000.tgz
shred -u /var/backups/comuki/config/20260904-020000.tgz
```

The symmetric passphrase lives in the operator's secrets manager.
Restore requires the same passphrase.

## Backup verification

A backup is a backup only if it restores. Run the restore sequence
against a fresh database / MinIO instance monthly and confirm:

- The runs list endpoint returns the same rows on the restored host.
- The cost rollup endpoint returns the same totals.
- One arbitrary run's `/api/v1/projects/{id}/runs/{id}/artifacts`
  endpoint returns the same pointer list — and `mc stat local/comuki-run-bundles/{projectId}/{runId}/brief.json`
  returns the file content (proving the bucket restore succeeded).

A backup that hasn't been verified is a backup that hasn't been
backed up. Add the verification step to the same cron that runs the
backup, with an alert on verification failure.

## Retention

| Artifact | Retention | Rationale |
|----------|-----------|-----------|
| Postgres dumps | 30 daily, 12 monthly | Compliance + recent-recovery window |
| MinIO mirrors | 14 daily | The bucket is "never delete" — the mirror only needs to span the worst-case restore window |
| Config archives | 30 daily, indefinite monthly | The orchestrator's release cadence is monthly; every monthly archive pins a release's full config |

Tighter retention is appropriate when storage is constrained. The
host's `artifacts.run_bundles` table is the canonical pointer list;
even with short retention, restoring Postgres is enough to recover
the run-graph itself; the bucket only adds the binary content.
# Comuki operator runbook (issue #10 T11.1)

This is the on-call guide for a self-hosted Comuki deployment. It assumes
the local-dev compose stack from `deploy/docker-compose.yml`; production
deployments swap `podman compose` for systemd / Kubernetes equivalents,
but the operational primitives are the same.

> **Audience:** the human who is paged at 02:00. Read this once end-to-end
> before the first page. Bookmark the sections that map to your paging
> signal.

## Table of contents

- [Quick start](#quick-start)
- [Bootstrap admin](#bootstrap-admin)
- [OIDC setup](#oidc-setup)
- [Backup](#backup)
- [Restore](#restore)
- [Upgrade](#upgrade)
- [Troubleshooting](#troubleshooting)
- [Performance](#performance)

## Quick start

A fresh checkout → first runnable instance, end-to-end, in under ten
minutes on a developer laptop:

```bash
git clone https://github.com/dot-stbl/comuki.git
cd comuki
cp deploy/.env.example deploy/.env

# (optional) edit deploy/.env — the dev defaults ship with comuki_dev
# passwords. Replace COMUKI_*_PASSWORD and the MinIO credentials before
# any deploy beyond localhost.

cd deploy
podman compose up -d       # postgres + minio + nexus + victoria metrics/logs
cd ..

# apply migrations to the orchestrator database
export COMUKI_DB="Host=localhost;Database=comuki;Username=comuki;Password=comuki_dev"
dotnet run --project platform/src/host/Comuki.Migrator

# (first run only) provision a bootstrap admin — set the env vars
# before launching the host.
export COMUKI_BOOTSTRAP_ADMIN_EMAIL=admin@example.com
export COMUKI_BOOTSTRAP_ADMIN_PASSWORD='replace-with-a-real-password'

# launch the host
cd platform/src/host/Comuki.Host
dotnet run --urls http://localhost:17173
```

Open `https://localhost:17173` (dashboard SPA — see
[`.agents/docs/operations/fesettings.md`](./fesettings.md) for the FE
bring-up) and log in with the bootstrap admin email / password.

The host writes the bootstrap admin row idempotently: every restart is
a no-op when the account is already present. Deleting the row (and the
`platform-admin` role assignment) brings the bootstrap back into
effect on the next start.

## Bootstrap admin

`auth:bootstrap` is the single source of truth. The host resolves it as:

1. `auth:bootstrap:adminEmail` / `adminPassword` from `appsettings.json`
   (or env-var overrides `auth__bootstrap__adminEmail` etc.).
2. Fallback env vars `COMUKI_BOOTSTRAP_ADMIN_EMAIL` /
   `COMUKI_BOOTSTRAP_ADMIN_PASSWORD`.

Both halves must be set or neither. A half-set pair fails the boot
loudly rather than silently ignoring a half-configured credential.

The seeder runs through the same `CreateUserHandler` /
`GrantRoleHandler` any admin uses — it is not a back-door. The grant is
`platform-admin` at platform scope (the only role that bypasses the
project-scope query filter on the runs list).

### Rotating the bootstrap admin password

1. Log in as a different `platform-admin` user (or via the API key of
   one).
2. Reset the bootstrap user's password through the user-management
   surface (the password is stored as a bcrypt hash — see
   `Comuki.Modules.Identity.Infrastructure`).
3. To revoke the bootstrap user's role grant: navigate to
   `/api/v1/users/{id}/roles` and revoke `platform-admin`. The account
   remains; the next login will hit the same fail-closed 401 with the
   standard `auth.invalid_credentials` code.

If the bootstrap admin is the only `platform-admin` and the password is
lost, the recovery path is:

```bash
# connect to the postgres container and reset the password hash
psql -h localhost -U comuki -d comuki \
  -c "UPDATE identity.users SET password_hash = '$NEW_BCRYPT_HASH' WHERE email = 'admin@example.com';"
```

Generate `$NEW_BCRYPT_HASH` with `htpasswd -bnBC 12 '' password |
tr -d ':\n' | sed 's/\$2y/\$2a/'` — bcrypt cost 12 matches the host's
default.

## OIDC setup

The host accepts OIDC providers through `auth:oidc:providers[]`. One
provider, one config block, one shared secret referenced by env var.

```json
{
  "auth": {
    "oidc": {
      "providers": [
        {
          "name": "keycloak",
          "authority": "https://kc.example.com/realms/comuki",
          "clientId": "comuki-dashboard",
          "clientSecretEnv": "COMUKI_KEYCLOAK_CLIENT_SECRET",
          "requireHttps": true
        }
      ]
    }
  }
}
```

| Field | Notes |
|-------|-------|
| `name` | Becomes the URL segment in `/api/v1/auth/oidc/{name}/start` and `{name}/callback`. Lower-case, alphanumeric. |
| `authority` | Discovery document base URL. The host fetches `/.well-known/openid-configuration` from it on every login (no cache — discovery is cheap). |
| `clientId` | The client the IdP registered for the Comuki dashboard. |
| `clientSecretEnv` | **Env-var name**, not the secret itself. The host reads `Environment.GetEnvironmentVariable(clientSecretEnv)` at startup and refuses to start if the var is unset. |
| `requireHttps` | Defaults to `true`. Set to `false` only for local dev (`http://localhost:17026/...`) — the host logs a warning when false. |

### Adding a new provider

1. Register the client at the IdP. Capture the client secret.
2. Add the provider block to `auth:oidc:providers[]` (config file or
   env-var equivalents — every key has a `__`-nested env form).
3. Set the client-secret env var (`COMUKI_<PROVIDER>_CLIENT_SECRET`) on
   the host.
4. Restart the host. Boot fails with a setup hint if the env var is
   missing.

The provider is available at `/api/v1/auth/oidc/{name}/start` after
restart; the first OIDC login through it creates the local account
through `OidcAccountLinker` (the same `CreateUserHandler` path as the
bootstrap admin).

### Rotating the OIDC client secret

1. Rotate the secret at the IdP — most IdPs allow two valid secrets
   side-by-side for the rotation window.
2. Update `COMUKI_<PROVIDER>_CLIENT_SECRET` on the host.
3. Restart the host. The next login uses the new secret.
4. Remove the old secret at the IdP after the rotation window expires.

## Backup

Full backup procedure (Postgres + MinIO + the host config directory) is
in [`backup.md`](./backup.md). The host's own state fits in two
artifacts:

- **`pg_dump` of the orchestrator database** — every per-DbContext
  schema (`orchestration`, `identity`, `projects`, `memory`, `chat`,
  `intake`, `costs`, `artifacts`) with its own `__ef_migrations_history`
  table. Per-schema partitioning is documented in
  [`database-schemas.md`](./database-schemas.md).
- **MinIO `mc mirror` of the `comuki-run-bundles` bucket** — every
  `{projectId}/{runId}/{brief,result,pins}.json` triple the
  `RunArtifactPackager` ever wrote. v1 retention = "never delete";
  the bucket keeps everything.

The bootstrap admin password and OIDC client secrets are **not**
backed up here — the assumption is that the host's configuration
storage (Kubernetes Secret, HashiCorp Vault, AWS Secrets Manager) is
already replicated. If the deployment uses `appsettings.json` for
secrets, that file is a sensitive artifact and deserves the same
treatment as the database dump.

## Restore

The recovery path when both Postgres and MinIO are intact is
straightforward — point the host at the recovered database and bucket
and start. The interesting case is **partial loss**: one of the two
stores survives. Order of operations matters.

### Postgres intact, MinIO lost

1. Bring up a fresh MinIO with the same access / secret keys.
2. Restore the bucket via `mc mirror` from backup
   (`mc mirror --remove --preserve /backup/comuki-run-bundles/ local/comuki-run-bundles`).
4. Restart the host pointed at the new MinIO endpoint.

The host's reads (`GET /api/v1/projects/{id}/runs/{id}/artifacts`)
return 200 with an empty list until the packager repopulates the
bucket. The bookkeeping rows in `artifacts.run_bundles` keep the
canonical pointer list — the host rebuilds the bucket from those rows
on the next packager pass per terminal run. **No data loss.**

### MinIO intact, Postgres lost

1. Restore Postgres from `pg_dump`.
2. Apply migrations (the dump carries data but not pending schema
   migrations — `dotnet run --project platform/src/host/Comuki.Migrator`
   brings the schema forward).
3. Restart the host pointed at the recovered database.

MinIO holds the artifact bundles by `{projectId}/{runId}` key; the
bookkeeping rows in `artifacts.run_bundles` reference those keys. As
long as the bucket keys match, the artifact endpoint returns the same
pointer list. **No data loss.**

### Both lost

Restore Postgres first, then MinIO. Run migrations between the two —
a database restored from a pre-migration dump will have an older
schema than the host binary expects.

## Upgrade

Comuki ships a dedicated migrator tool (`Comuki.Migrator`) that brings
every per-DbContext schema forward to the latest migration in one
invocation:

```bash
export COMUKI_DB="<full connection string>"
dotnet run --project platform/src/host/Comuki.Migrator
```

The migrator reads the same `COMUKI_DB` env var the host does (and
falls back to `ConnectionStrings:Comuki`). Each context keeps its own
`__ef_migrations_history` table — the migrator applies pending
migrations per context, in order, and prints each one as it lands.

The Production gate (`RejectBlankPasswordInProduction`) refuses to
start the migrator with a blank password in Production — set
`COMUKI_MIGRATOR_DB_PASSWORD` before invoking.

### Upgrade procedure

1. **Stop the host** (and any workers pulling from the work-queue).
2. **Back up Postgres + MinIO** (see [Backup](#backup)). Always.
3. Pull the new image / commit / binary.
4. Run `Comuki.Migrator` — the output lists every migration per
   context. Verify nothing throws.
5. Start the host. The host picks up where the migrator left off; no
   host-side migration step.
6. (Optional) replay the migration through the host's migrator if the
   new release added migrations beyond the migrator binary's version
   — the host runs `dotnet ef` directly through the same paths.

### What migrations are added?

`dotnet ef migrations add` is the only sanctioned way to author
migrations (see `.agents/rules/csharp/ef-migrations.md`). New
migrations appear in `Migrations/<ContextName>/<Timestamp>_Name.cs`
on the relevant module's `Infrastructure` project. The migrator
applies them per the EF history table.

A pre-release review should diff the migrations directory against the
previous release's HEAD to enumerate schema changes — that's the
canonical changelog of the upgrade.

## Troubleshooting

Symptoms → cause → fix.

### `no subject scope`

```
exception: System.InvalidOperationException: no subject scope
  at Comuki.Shared.Kernel.Scoping.AsyncLocalSubjectScopeAccessor
```

The ambient `ISubjectScopeAccessor` is missing on the request thread.
**Cause:** `SubjectScopeMiddleware` is not in the pipeline, or the
host's `MapControllers` ran before `UseMiddleware<SubjectScopeMiddleware>()`.

**Fix:** confirm `HostComposer.Compose` calls
`app.UseMiddleware<SubjectScopeMiddleware>()` between
`UseAuthorization()` and `MapControllers()`. The order is enforced in
the host's composition; a custom host that reorders these will trigger
the same fault.

### MinIO 403 on every artifact write

The `MinioRunArtifactStore.EnsureBucketAsync` call returned a 403 on
bucket creation. **Cause:** the `Artifacts:Minio:AccessKey` /
`SecretKey` are wrong, or the bucket already exists under a different
account.

**Fix:** confirm `Artifacts:Minio:Endpoint`, `AccessKey`, `SecretKey`
match the running MinIO instance. `mc admin info` from the bucket
namespace is the simplest check.

### `refusing to start the host in Production: Artifacts:Minio:SecretKey is still on its committed dev default`

The Production-secret validator (issue #10 T11.4) caught a deploy with
the dev default still in place. **Cause:** the env var
`Artifacts__Minio__SecretKey` (or its `appsettings.json` equivalent)
still reads `comuki_dev`.

**Fix:** set the env var to a real secret. The commit refuses to start
in Production — the dev defaults are intentional lures.

### `migration history table is missing`

```
Microsoft.EntityFrameworkCore.DbUpdateException: relation
  "artifacts.__ef_migrations_history" does not exist
```

**Cause:** the migrator has not been run, or a partial restore left
the schema behind. Run `Comuki.Migrator` — it creates the per-schema
history table on first apply.

### Login returns 401 `auth.invalid_credentials`

Intentional — every credential failure (unknown email, wrong password,
disabled account, OIDC-only account) answers the same 401 with the
same `auth.invalid_credentials` code. There is no enumeration signal.

### SignalR `/hubs/runs` handshake fails

**Cause:** the connection is anonymous. `/hubs/runs` requires an
authenticated cookie or API-key bearer header (see
`openspec/specs/realtime/spec.md`).

**Fix:** confirm the dashboard SPA attaches the cookie or bearer
header on the SignalR negotiation request. The default
`@microsoft/signalr` client only attaches the cookie when `withCredentials`
is set on the `HubConnectionBuilder`.

## Performance

Where to look first when a Comuki host is slow, by signal.

### Postgres slow queries

Every per-DbContext schema has its own indexes; the hottest reads are
the runs list (`orchestration.runs` and `work_items`) and the cost
rollup (`costs.usage_events`). Run:

```bash
psql -h localhost -U comuki -d comuki -c "
  SELECT schemaname, relname, seq_scan, idx_scan, n_live_tup
  FROM pg_stat_user_tables
  WHERE seq_scan > idx_scan
  ORDER BY seq_scan DESC
  LIMIT 20;"
```

A high `seq_scan` count on `runs` / `work_items` means the partial
indexes aren't being hit — usually because the query is missing the
`status` filter that the partial index is keyed on.

For slow specific queries, enable statement logging:

```bash
psql -h localhost -U comuki -d comuki -c "
  ALTER SYSTEM SET log_min_duration_statement = '500ms';
  SELECT pg_reload_conf();"
```

### MinIO latency

The artifact packager reads + writes `brief.json`, `result.json`,
`pins.json` per terminal run. Slow MinIO manifests as elevated
`run.artifacts_bundled` journal lag (the journal event lands only
after the bucket write succeeds).

Check `mc admin trace` on the MinIO host for per-request latency, or
the host's OTel HTTP client metrics (the MinIO SDK is a plain HTTP
client — instrumentation is automatic).

### Journal event lag

The SignalR `RunEvent` broadcast (issue #7) is driven by the
orchestration `DbContext`'s `RunEventsBroadcastInterceptor`. The
interceptor flushes inside the same SaveChanges as the journal row.

A growing lag between the `run.status_changed` event and the
dashboard's signal means the interceptor queue is backed up. Check
the host logs for `IRunEventsBroadcaster` errors; the most common
cause is a stale SignalR group registration on the hub.

### Memory pressure

The host's `RunEventsBroadcastInterceptor` keeps an in-process
connection map keyed by `runId`. Long-lived runs with many signal-R
clients per project can grow this unboundedly. The connection map
is process-local and lost on restart — that's a feature, not a bug
(the journal survives).

If memory climbs monotonically across deploys and a hot reload does
not bring it down, the most likely cause is the SignalR connection
map retaining disconnected clients — restart the host, then audit
the dashboard's reconnect cadence.
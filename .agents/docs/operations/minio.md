# MinIO — run-artifact bucket

The platform writes per-run artifacts (`brief.json`, `result.json`,
`pins.json`) to a single MinIO bucket, scoped by project and run id.
The compose `minio-init` job creates the bucket and a 30-day
non-current-version lifecycle on first boot.

> Issue: #28 — Run artifact bundle in MinIO (S3) keyed by project/run-id.

## Bucket topology

```
s3://comuki-run-bundles/                           (bucket, single)
├── {projectId}/{runId}/brief.json
├── {projectId}/{runId}/result.json
└── {projectId}/{runId}/pins.json
```

`{projectId}/{runId}` is the canonical scope unit — the read path of
one project never reaches another's run namespace. Object keys are
built by `MinioRunArtifactStore.BuildObjectKey` from
`{projectId}/{runId}/{relativePath}`; the relative path is opaque to
the store (the application chooses `brief.json`, `result.json`, …).

The Artifacts module owns the write side; the host surfaces a thin
read API at `/api/v1/projects/{projectId}/runs/{runId}/artifacts`
(`ApiRoutes.RunArtifacts`, `RunArtifactsController`).

## Lifecycle policy

```json
{
  "Rules": [
    {
      "ID": "expire-non-current-30d",
      "Status": "Enabled",
      "Filter": { "Prefix": "" },
      "NoncurrentVersionExpiration": { "NoncurrentDays": 30 }
    }
  ]
}
```

The rule expires non-current versions after 30 days; current versions
stay forever (the platform's v1 commitment is "never delete"). The rule
is applied via `mc ilm import` in the `minio-init` job — idempotent on
re-runs.

## Compose topology

```yaml
# deploy/docker-compose.yml
minio:
  image: minio/minio:latest
  command: server /data --console-address ":9001"
  environment:
    MINIO_ROOT_USER: ${COMUKI_MINIO_ROOT_USER}
    MINIO_ROOT_PASSWORD: ${COMUKI_MINIO_ROOT_PASSWORD}
  ports: ["9000:9000", "9001:9001"]
  healthcheck:
    test: ["CMD", "curl", "-f", "http://localhost:9000/minio/health/live"]

minio-init:
  image: minio/mc:latest
  depends_on:
    minio: { condition: service_healthy }
  environment:
    MINIO_DEFAULT_BUCKETS: ${COMUKI_MINIO_ARTIFACTS_BUCKET:-comuki-run-bundles}
  entrypoint: >-
    /bin/sh -c "
      mc alias set local http://minio:9000 '$MINIO_ROOT_USER' '$MINIO_ROOT_PASSWORD';
      for bucket in $(echo $MINIO_DEFAULT_BUCKETS | tr ',' ' '); do
        if ! mc ls local/$bucket >/dev/null 2>&1; then
          mc mb local/$bucket;
        fi;
        mc ilm import local/$bucket <(printf '%s\n' '{...}');
      done;
    "
```

`minio-init` is a one-shot bring-up job; it does **not** stay running.
The bucket is created and the lifecycle is applied; subsequent stack
boots no-op because `mc ls` returns success and `mc ilm import` is
idempotent.

## Host configuration

```toml
# appsettings.json
[Artifacts.Minio]
endpoint           = "minio:9000"   # or "localhost:9000" outside compose
accessKey          = "comuki"       # == COMUKI_MINIO_ROOT_USER
secretKey          = "comuki_dev"   # == COMUKI_MINIO_ROOT_PASSWORD — see Anti-patterns
bucket             = "comuki-run-bundles"
useSSL             = true           # false for compose dev (HTTP)
autoCreateBucket   = false          # compose's minio-init owns it; flip on
                                    # for greenfield / integration tests
```

The option class is `ArtifactsOptions` (`platform/src/modules/Artifacts/.../Store/ArtifactsOptions.cs`).
All four of `Endpoint`, `AccessKey`, `SecretKey`, `Bucket` are
`[Required]` and `[MinLength(1)]` — `ValidateOnStart` rejects an
incomplete configuration at startup rather than the first artifact
write. `AutoCreateBucket` is a greenfield convenience: the in-process
`EnsureBucketAsync` runs once on first use when the bucket is missing.
Production should leave it off (bucket provisioning is an operator
concern); integration tests flip it on.

Env-var mapping (the env wins over `appsettings.json`):

| TOML key                 | Env var (double-underscore nesting via the platform's env provider) |
|--------------------------|----------------------------------------------------------------------|
| `Artifacts:Minio:Endpoint`           | `Artifacts__Minio__Endpoint`                                       |
| `Artifacts:Minio:AccessKey`          | `Artifacts__Minio__AccessKey`                                      |
| `Artifacts:Minio:SecretKey`          | `Artifacts__Minio__SecretKey`                                      |
| `Artifacts:Minio:Bucket`             | `Artifacts__Minio__Bucket`                                         |
| `Artifacts:Minio:UseSSL`             | `Artifacts__Minio__UseSSL`                                         |
| `Artifacts:Minio:AutoCreateBucket`   | `Artifacts__Minio__AutoCreateBucket`                               |

## What's inside the bundle

The `RunArtifactPackager` (`platform/src/modules/Artifacts/.../Packaging/`)
fires when a run reaches a terminal status (succeeded / failed /
cancelled / escalated):

| Object key                                | Content |
|-------------------------------------------|---------|
| `{projectId}/{runId}/brief.json`          | The work-item brief the worker runtime received. |
| `{projectId}/{runId}/result.json`         | The worker's terminal payload (result or failure detail). Absent when no work item drove the transition. |
| `{projectId}/{runId}/pins.json`           | Version pins (status, occurredAt) for the bundle. |

The `EfRunArtifactBundleStore` records the bookkeeping row
(`artifacts.run_bundles`) — the bundle itself is in MinIO, the
pointer is in Postgres. The run artifacts endpoint reads the
bookkeeping rows and returns pre-signed GET URLs back to the SPA.

## Operational notes

- **DNS** — inside compose the host talks to `minio:9000`. On a bare
  metal host, point `Artifacts:Minio:Endpoint` at `localhost:9000` or
  the external DNS name.
- **TLS** — production should leave `UseSSL=true` (default); dev compose
  flips to `false`. The MinIO server side terminates TLS; the client
  uses HTTPS when `UseSSL=true`.
- **Object key prefix** — `{projectId}/{runId}` is the unit the store
  uses for listing. A project-scoped read is `ListAsync(projectId, …)`
  which prefixes the request; cross-project reads are **not** a thing
  on the API surface.
- **Healthcheck** — `minio:9000/minio/health/live` returns `OK` when
  MinIO is ready. The `minio-init` job's `depends_on.condition:
  service_healthy` gates the bucket creation; the host waits for the
  same check before connecting.

## Sources

- `platform/src/modules/Artifacts/.../Store/MinioRunArtifactStore.cs` —
  the storage adapter; `BuildObjectKey`, `BuildObjectUri`.
- `platform/src/modules/Artifacts/.../Store/MinioClientFactory.cs` —
  singleton SDK client, transport selection (HTTP vs HTTPS).
- `platform/src/modules/Artifacts/.../Store/ArtifactsOptions.cs` —
  options class with `ValidateOnStart`.
- `platform/src/modules/Artifacts/.../Packaging/RunArtifactPackager.cs` —
  terminal-state bundle write.
- `platform/src/modules/Artifacts/.../Infrastructure/Persistence/ArtifactsDatabase.cs` —
  schema name (`artifacts`) and table names.
- `platform/src/host/Comuki.Host/Runs/Controllers/RunArtifactsController.cs` —
  read API.
- `deploy/docker-compose.yml` — `minio` + `minio-init` services.
- `deploy/.env.example` — `COMUKI_MINIO_*` env vars.

## Related

- [storage.md](./storage.md) — overall storage layout (Postgres +
  MinIO + Victoria), retention policies.
- [install.md](./install.md) — Migrator DB credentials; same
  in-env-var-secret discipline applies to `Artifacts:Minio:SecretKey`
  (don't commit the secret in `appsettings.json`).
- [database-schemas.md](./database-schemas.md) — `artifacts` schema,
  `run_bundles` bookkeeping table.
- [openapi-codegen.md](./openapi-codegen.md) — the artifacts list
  endpoint is in the kubb-generated client.

## Anti-patterns

- ❌ Committed `Artifacts:Minio:SecretKey` in `appsettings.json` —
  put it in env (compose secret, systemd `EnvironmentFile=`, k8s
  Secret). `deploy/.env.example` ships with `comuki_dev` for local dev
  only.
- ❌ `AutoCreateBucket=true` in production — the operator owns bucket
  provisioning; the flag exists for greenfield + integration tests.
- ❌ Cross-project reads by listing the whole bucket — the API
  surface scopes by `{projectId}`; raw bucket reads bypass the
  scoping discipline and any future authorization layer.
- ❌ Re-creating the `minio-init` job manually on every deploy — the
  job is idempotent; let `podman compose up -d` drive it.
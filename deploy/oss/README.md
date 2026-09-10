# Self-hosting Comuki

Comuki is a platform where a leading model (the brain) decomposes a
task and conducts a swarm of ephemeral worker containers. Shared
knowledge base and rules over MCP. This directory ships three
self-contained deployment paths — pick one and be up in ~15 minutes.

| Path | Use it when | Files |
|---|---|---|
| Docker Compose | single machine, laptop server, homelab | [`../compose/`](../compose/) |
| Helm chart | any Kubernetes >= 1.28 | [`../helm/`](../helm/) |
| Raw manifests | Kubernetes without Helm | [`../k8s/`](../k8s/) |

All three reference the same images:

| Image | Contents |
|---|---|
| `ghcr.io/dot-stbl/comuki` | one image, three entrypoints — `/app/host` (orchestrator), `/app/migrator` (migrations job), `/app/brain` (standalone brain, experimental) |
| `ghcr.io/dot-stbl/comuki-worker` | Translator + pi agent runtime — the container spawned per work item |
| `ghcr.io/dot-stbl/comuki-dashboard` | the dashboard SPA behind nginx |

From a git checkout you can build all of them locally (see
`deploy/compose/docker/*.Dockerfile`); set the tags to `local` in the
respective env/values files.

## Quick Start (Docker)

```bash
cd deploy/compose
cp .env.example .env           # defaults boot a localhost stack
docker compose up -d --build   # dashboard: http://localhost:17173
```

Log in with `COMUKI_BOOTSTRAP_ADMIN_EMAIL` / `COMUKI_BOOTSTRAP_ADMIN_PASSWORD`
from your `.env` (defaults `admin@example.com` / `comuki_dev` — change them).

## Quick Start (Helm)

```bash
helm install comuki ./deploy/helm \
  --set secrets.postgresPassword="$(openssl rand -hex 16)" \
  --set secrets.apiKeyPepper="$(openssl rand -hex 32)" \
  --set secrets.tokenPepper="$(openssl rand -hex 32)" \
  --set secrets.bootstrapAdminPassword='ChangeMe-2026!' \
  --set secrets.artifactsAccessKey=comuki-minio \
  --set secrets.artifactsSecretKey="$(openssl rand -hex 16)" \
  --set publicUrl=http://comuki.localhost

kubectl -n default port-forward svc/comuki-dashboard 8080:80
```

Prefer an Ingress? Add `--set ingress.enabled=true,ingress.host=comuki.example.com`
and set `publicUrl` to the same host. Bare-minimum values:
`helm install comuki ./deploy/helm -f deploy/helm/values-minimal.yaml ...`.

## Quick Start (kubectl)

```bash
cd deploy/k8s
kubectl apply -f namespace.yaml
cp secret.example.yaml secret.yaml && $EDITOR secret.yaml   # fill ${PLACEHOLDER}s
kubectl apply -f secret.yaml && rm secret.yaml
kubectl apply -f postgres.yaml -f minio.yaml -f configmap.yaml -f worker-role.yaml
kubectl apply -f migrator-job.yaml
kubectl wait --for=condition=complete job/comuki-migrator -n comuki --timeout=300s
kubectl apply -f deployment.yaml -f service.yaml -f dashboard.yaml
```

Detailed order + notes: [`../k8s/README.md`](../k8s/README.md).

## Architecture

```
                       ┌──────────────────────────┐
   browser ──────────► │  dashboard (nginx)       │  SPA + reverse proxy
                       │  /  /api  /realtime      │
                       └────────────┬─────────────┘
                                    ▼
                       ┌──────────────────────────┐        ┌────────────────┐
                       │  comuki host (API)       │ ◄────► │ Postgres       │
                       │  REST · SignalR · gRPC   │        │ 10 schemas     │
                       │  queue · scheduler ·     │        │ + pgvector     │
                       │  artifacts packager      │        └────────────────┘
                       └──────┬───────────┬───────┘
              spawns (docker  │           │ reads/writes run bundles
              socket or k8s   │           ▼
              batch Jobs)     │        ┌────────────────┐
                              └──────► │ MinIO (S3)     │
                                       └────────────────┘
   worker containers (ephemeral): Translator + pi agent runtime
   claim → run work items → stream results over gRPC → exit
```

The host is the single orchestrating process: it queues work, spawns
worker containers (Docker provider via the mounted socket, or
Kubernetes provider via batch/v1 Jobs), mints short-lived worker
tokens, and packages run artifacts into S3. The brain (leading model)
currently runs as an in-process component of the host; the standalone
brain host exists but is experimental.

## Configuration — key environment variables

Verified against the source (`platform/src/host/*`).

| Variable | Where | Required | Meaning |
|---|---|---|---|
| `COMUKI_DB` | host, migrator, brain | yes | full Npgsql connection string (all ten schemas live in one database) |
| `COMUKI_ENV` | host, migrator, brain | no | `production` (default) or `development`; `Production` enables the startup secret validator |
| `COMUKI_IDENTITY_APIKEY_PEPPER` | host | prod: yes | HMAC pepper for stored API keys — rotating invalidates all keys |
| `COMUKI_TOKEN_PEPPER` | host | prod: yes | HMAC pepper for worker tokens — rotating invalidates all tokens |
| `COMUKI_BOOTSTRAP_ADMIN_EMAIL` / `_PASSWORD` | host | both or neither | first operator account, seeded idempotently at startup |
| `COMUKI_PUBLIC_HOST_URL` | host | for OIDC | absolute public URL (scheme + host) used in OIDC redirects |
| `COMUKI_ARTIFACTS_ENDPOINT` / `_ACCESSKEY` / `_SECRETKEY` | host | yes | S3-compatible store for run bundles |
| `COMUKI_ARTIFACTS_BUCKET`, `_USESSL`, `_AUTOCREATEBUCKET` | host | no | `comuki-run-bundles` / `false` / `true` defaults |
| `COMUKI_COMPUTE_PROVIDER` | host | no | `docker` (default) or `kubernetes` |
| `COMUKI_COMPUTE_SCALE_WORKERIMAGE` | host | no | worker image spawned per work item |
| `COMUKI_COMPUTE_SCALE_ORCHESTRATORGRPCURL` | host | no | endpoint workers connect back to |
| `COMUKI_COMPUTE_DOCKER_NETWORKMODE` | host | docker provider | Docker network worker containers join |
| `COMUKI_COMPUTE_KUBERNETES_NAMESPACE` / `_SERVICEACCOUNT` / `_CPUREQUESTMILLIS` / `_MEMORYREQUESTMIB` | host | k8s provider | worker Job placement |
| `COMUKI_HOST_CORS_ALLOWEDORIGINS_0..N` | host | no | browser origins allowed on the API (default `http://localhost:17173`) |
| `COMUKI_HOST_RATELIMIT_*` | host | no | per-partition rate-limit budgets |
| `COMUKI_TELEMETRY_OTLPENDPOINT` | host | no | OTLP gRPC endpoint; unset = no OTel SDK |
| `COMUKI_BRAIN_GRPCPORT` | brain | no | gRPC listen port (default 17004) |
| `COMUKI_BRAIN_MODEL_ENDPOINT` / `_API_KEY` / `_MODEL_ID` | brain | for brain calls | any OpenAI-compatible endpoint |
| `COMUKI_CHAT_WORKER_IMAGE`, `COMUKI_SCHEDULER_WORKER_IMAGE` | host | no | worker image for chat/scheduler-dispatched items |

Env → config mapping: `COMUKI_A_B` sets `a:b` (single underscore is the
section separator; binding is case-insensitive). The same sections can
live in `config.toml` instead — see
[`deploy/config.example.toml`](../config.example.toml) and
[the env/config contract](../README.md#env--configtoml). `ASPNETCORE_*` /
`DOTNET_*` variables remain a quiet fallback, not the documented path.

Worker containers receive their environment from the orchestrator at
spawn time (`COMUKI_ORCH_HTTP`, `COMUKI_ORCH_GRPC`, `COMUKI_WORKER_TOKEN`,
`COMUKI_PROFILE_KEY`, `COMUKI_PROFILES_REF`, `COMUKI_WORKER_IMAGE`) —
you never set these by hand.

Postgres note: the migrator also accepts `COMUKI_DATABASE` (legacy
alias, warned) and `COMUKI_MIGRATOR_DB_PASSWORD` (fills a blank
`Password=` in config.toml). Prefer a full `COMUKI_DB`.

## Workers

Workers are **not** long-running services — never deploy one by hand.
The compute engine starts a worker container per claim and stops it
when idle:

- **Docker provider** (compose default): the host talks to the mounted
  `/var/run/docker.sock` and starts worker containers in
  `COMUKI_COMPUTE_DOCKER_NETWORKMODE`. They reach the host directly at
  `http://comuki-host:8080`.
- **Kubernetes provider** (helm/k8s default): the host creates
  `batch/v1` Jobs (`backoffLimit 0`, TTL-cleaned) in
  `COMUKI_COMPUTE_KUBERNETES_NAMESPACE` — needs the Job-creation Role the
  chart/manifests ship.

A worker claims work over REST, streams results over the bidi gRPC
worker channel, uploads artifacts, and exits. Worker auth tokens are
minted by the host (opaque, TTL'd) and stamped onto the container
environment automatically.

Model access for workers: set your provider key per worker image or
through the optional built-in proxy (`COMUKI_PROXY_ENABLED`,
`COMUKI_PROXY_VIRTUALKEYS_N_*`) — see `deploy/.env.example` for the shape.

## Troubleshooting

| Symptom | Cause / fix |
|---|---|
| Host crashes on boot: `refusing to start the host in Production: ...` | A secret is still on a dev default. Set real peppers / MinIO keys / bootstrap password (`ProductionSecretValidator`). |
| Host crashes: `connection string not found` | `COMUKI_DB` (or `connectionStrings.comuki` in config.toml) is unset. |
| `relation "artifacts.__ef_migrations_history" does not exist` | Migrator never ran (or ran against a different database). Run the migrator job. |
| No workers spawn (compose) | Host can't reach the Docker socket — check `DOCKER_GID` in `.env` (`stat -c '%g' /var/run/docker.sock`), and that the `worker-image` service built `comuki-worker:local` (`docker images \| grep comuki-worker`). |
| No workers spawn (k8s) | `COMUKI_COMPUTE_PROVIDER` must be `kubernetes` and the `comuki-worker-spawn` Role applied (`kubectl auth can-i create jobs -n comuki -as=system:serviceaccount:comuki:comuki-host`). |
| MinIO 403 on artifact writes | `COMUKI_ARTIFACTS_ACCESSKEY`/`_SECRETKEY` don't match MinIO's root credentials. |
| Login 401 `auth.invalid_credentials` | Same answer for unknown email / wrong password / disabled account — no enumeration signal. Check the bootstrap admin vars were set on first boot. |
| Dashboard loads but every request fails | The SPA's baked `VITE_API_BASE_URL` doesn't match the URL you opened — rebuild the dashboard image with the right `--build-arg VITE_API_BASE_URL`. |
| Migrator Job loops `connection refused` | Normal during Postgres startup (backoffLimit 12). Persistent failures: check `COMUKI_DB` / secret values. |

More operational depth (backup/restore, OIDC setup, bootstrap-admin
rotation): [`.agents/docs/operations/runbook.md`](../../.agents/docs/operations/runbook.md).

## Hardening checklist (before exposing beyond localhost)

1. `COMUKI_ENV=production` (validators on).
2. All secrets real: `openssl rand -hex 32` for peppers; strong admin
   password (>= 12 chars, digit + symbol).
3. `COMUKI_PUBLIC_HOST_URL` / `publicUrl` = the real HTTPS origin.
4. TLS at the edge (Ingress / reverse proxy), large body + long
   timeouts for SignalR and artifact uploads.
5. Postgres and MinIO not exposed to the internet; backups of both
   (see the runbook).

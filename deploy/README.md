# Comuki — local development infra

Five self-hosted services, matching `comuki-stack.md` § 04 and
`comuki-architecture.md` § 04/06. All bind to localhost-only ports
and persist data to named Docker volumes.

> **Not for production.** Credentials are dev defaults, no TLS, no
> auth beyond simple passwords. For anything beyond a laptop, swap
> to a real secrets manager and reverse-proxy with TLS.

## Self-hosting (deploy, don't develop)

Looking to RUN Comuki rather than develop it? Three self-contained
paths live next to this file — start at [`oss/README.md`](./oss/README.md):

| Path | What |
|---|---|
| [`compose/`](./compose/) | full local stack (postgres+pgvector, minio, host, dashboard, worker image) |
| [`helm/`](./helm/) | Helm chart, works on any k8s >= 1.28 |
| [`k8s/`](./k8s/) | raw `kubectl apply` manifests |

## What's here

| File | Purpose |
|---|---|
| `docker-compose.yml` | Core services + volumes + healthchecks + opt-in profiles (`worker`, `keycloak`, `grafana`) |
| `.env.example` | Copy to `.env` to override credentials (incl. Phase 4 worker keys) |
| `postgres/init.d/01-pgvector.sql` | Enables `vector` extension (Phase 5, `Comuki.Platform.Knowledge`) |
| `worker.Dockerfile` | Real minimal pi-coding-agent image (lands in Phase 4, Slice 0 step 0) |
| `scripts/test-pi-headless.{sh,ps1}` | Build worker, run pi in container, assert stream-json output |
| `grafana/` | Dashboards-as-code + provisioning (S8; profile `grafana`) |

## Bring it up

```bash
cd deploy

# copy env, edit if needed (defaults work for a clean laptop)
cp .env.example .env

# pick one:
podman compose --env-file .env up -d      # Linux / macOS / Windows with podman 4+
docker compose --env-file .env up -d      # if podman isn't set up

# watch it converge
podman compose logs -f
```

First boot pulls images (~1.5GB total). Nexus takes the longest
(~700MB) because it has to extract its built-in Karaf runtime on
first start. Postgres is fast.

## Health check

```bash
# Postgres (pg_isready exits 0 when accepting connections)
pg_isready -h localhost -p 5432 -U comuki

# MinIO S3 API
curl -fsS http://localhost:9000/minio/health/live

# Nexus (initial admin password from .env; UI at the same URL)
curl -fsS http://localhost:8081/service/rest/v1/status

# VictoriaMetrics (OTLP receiver on :8431, query/vmui on :8428)
curl -fsS http://localhost:8428/health

# VictoriaLogs (HTTP ingestion + vmui on :9428)
curl -fsS http://localhost:9428/health
```

## Endpoints cheat sheet

| Service | URL | Auth |
|---|---|---|
| Postgres | `localhost:5432` | `${COMUKI_POSTGRES_USER}` / `${COMUKI_POSTGRES_PASSWORD}` |
| MinIO S3 | `http://localhost:9000` | `${COMUKI_MINIO_ROOT_USER}` / `${COMUKI_MINIO_ROOT_PASSWORD}` |
| MinIO console | `http://localhost:9001` | same as S3 |
| Nexus UI | `http://localhost:8081` | `admin` / `${COMUKI_NEXUS_ADMIN_PASSWORD}` |
| VictoriaMetrics vmui | `http://localhost:8428/vmui` | none (localhost) |
| VictoriaMetrics OTLP | `localhost:8431` | none |
| VictoriaLogs vmui | `http://localhost:9428/vmui` | none (localhost) |
| VictoriaLogs HTTP | `http://localhost:9428` | none |
| Grafana (profile `grafana`) | `http://localhost:17027` | `${COMUKI_GRAFANA_ADMIN_USER}` / `${COMUKI_GRAFANA_ADMIN_PASSWORD}` |

## What connects to what

When the platform lands:

| Comuki piece | Talks to |
|---|---|
| `Comuki.Platform.Database.Runs` (EFCore, Phase 3) | Postgres :5432 |
| `Comuki.Platform.Artifacts` (Phase 7) | MinIO :9000 (S3 API) |
| Worker containers (Phase 3+) | pull deps from Nexus :8081 |
| `Comuki.Shared.Telemetry` / host (`AddComukiTelemetry`) | VictoriaMetrics OTLP :8431 when `Telemetry:OtlpEndpoint` is set |
| Grafana (profile `grafana`) | reads VictoriaMetrics :8428 via provisioned Prometheus datasource |

Per `comuki-decisions.md` § "Прокси (ключевое решение)" — workers in
containers only see a virtual URL + capability key from
`Comuki.Platform.Proxy`, never the real model API. That has its own
service, not here.

## Tear down

```bash
podman compose down        # keep volumes
podman compose down -v     # wipe data
```

Wiping data is fine in dev. In real life, do not.

## Env & config.toml

The platform speaks Comuki, not .NET (issue #54). Four binaries —
`comuki` (orchestrator host), `comuki-brain`, `comuki-translator`
(worker), `comuki-migrator` — one configuration contract:

| Concern | Comuki way | Quiet fallback (not documented as the path) |
|---|---|---|
| Environment | `COMUKI_ENV=development\|production` (default `production`) | `ASPNETCORE_ENVIRONMENT` / `DOTNET_ENVIRONMENT` |
| Config file | `config.toml` (see [`config.example.toml`](./config.example.toml)) | — |
| Config file path | `COMUKI_CONFIG_PATH` → `./config.toml` (cwd) → `/etc/comuki/config.toml` (Linux) | — |
| Env → config | `COMUKI_A_B` → `a:b` (single underscore = section separator, case-insensitive binding) | `Section__Key` double-underscore form is not read |
| Listen address | `[server] host/port` in config.toml, env override `COMUKI_SERVER_PORT` | `ASPNETCORE_URLS` / `ASPNETCORE_HTTP_PORTS` |
| Log level | `COMUKI_LOG_LEVEL=trace\|debug\|info\|warn\|error\|fatal` (wins over `[logging] level`) | `[logging] level` in config.toml |
| Log format | `COMUKI_LOG_FORMAT=text\|json` (console only; OTLP untouched) | text |

Notes:

- **No config.toml found → empty configuration, no error.** Build-time
  OpenAPI generation boots the hosts without one; secrets and endpoints
  arrive through env anyway.
- **Secrets never live in config.toml** — `COMUKI_DB`, peppers, MinIO
  keys, admin password, model API keys are env-only.
- **Logs**: one line per event —
  `2026-09-11T10:00:00.123Z info  comuki.host  listening addr=http://localhost:8080` —
  RFC3339 UTC with milliseconds, lowercase level, lowercase category,
  structured fields as `key=val`. Kestrel lifetime messages are
  rewritten to short `comuki.host` lines; no `Server:` header on
  responses. Under `COMUKI_LOG_FORMAT=json` the same fields render as
  one JSON object per line (`ts`/`level`/`category`/`message`, state
  fields stringified, `exception`, `rid`) — for collectors that prefer
  structured input. Every log line of a request carries `rid=…` (see
  below).
- The migrator's config.toml fallback section is
  `[connectionStrings] comuki = "…"` with a blank `Password=` filled
  from `COMUKI_MIGRATOR_DB_PASSWORD`.

## Operator CLI (issue #56)

All four binaries understand `version` — it prints the flat build line
and exits 0 before any bootstrap (no config, no DB needed):

```
$ comuki version
comuki version=1.0.0 sha=41d265deae51dbd82291ef85a376a4b123d0521c build=2026-09-11 mode=debug
```

The same line is the first log record of every starting host, and
`GET /api/v1/version` (anonymous, like `/health`) serves it as JSON for
the dashboard footer.

The orchestrator host adds first-run and diagnostics commands:

| Command | What it does | Exit codes |
|---|---|---|
| `comuki init [--env <development\|production>] [--force]` | Writes `./config.toml` from the built-in template plus a `./.env` skeleton: `COMUKI_ENV`, a `COMUKI_DB` placeholder and fresh random-hex HMAC peppers. Never overwrites without `--force`. | 0 written · 1 existing file skipped · 2 usage error |
| `comuki doctor` | Pre-flight checklist without booting the host: config.toml location (discovery chain), environment provenance (deprecated fallbacks and the legacy `COMUKI_DATABASE` alias warn), DB `SELECT 1` with a 2s timeout, production-secret posture. Pending migrations are not checked — the migrations line points at `comuki-migrator status`. | 0 no failures · 1 at least one fail (warnings pass) |
| `comuki config show` | The effective configuration — config.toml with `COMUKI_*` env overrides applied — as sorted `key = value` lines. Secret-marked keys (password/secret/pepper/token/key) print `****`; connection strings keep their shape with `Password=****`. | 0 |

The migrator has its own dry-run:

| Command | What it does | Exit codes |
|---|---|---|
| `comuki-migrator status` | Lists pending migrations per schema without applying anything and with zero DDL (`pending (orchestration): 20260911…` / `orchestration schema is up to date`); a not-yet-provisioned schema reads as pending (`schema not provisioned`) — for CI gates. | 0 all applied · 1 pending · 2 error |

### Correlation id

Every request to the orchestrator host carries an `X-Request-Id`: a valid
incoming header value (8–128 url-safe characters) is reused, anything
else is replaced by a generated 32-hex id. The id is echoed on the
response and stamped as `rid=…` on every log line of the request —
operators can jump from an access log to the exact request flow.

## Grafana dashboards-as-code (S8)

Opt in — Grafana is behind `profiles: ["grafana"]` so a bare `compose up`
stays lean:

```bash
cd deploy
podman compose --env-file .env --profile grafana up -d
# UI: http://localhost:17027  (admin from COMUKI_GRAFANA_*)
```

Layout:

| Path | Role |
|---|---|
| `grafana/provisioning/datasources/victoria.yml` | Prometheus datasource → `http://victoria-metrics:8428` |
| `grafana/provisioning/dashboards/dashboards.yml` | file provider → `/var/lib/grafana/dashboards` |
| `grafana/dashboards/comuki-runs.json` | runs started / work items / claim latency |
| `grafana/dashboards/comuki-workers.json` | worker start/stop + claim outcomes |
| `grafana/dashboards/comuki-cost.json` | placeholder until S9 cost/budgets |

### OTel → Victoria → Grafana wiring

1. Compose already starts VictoriaMetrics with `--enableOTLPReceiver`
   (OTLP gRPC on host `:8431`, query/vmui on `:8428`).
2. Host: set `telemetry.otlpEndpoint` in config.toml (or env
   `COMUKI_TELEMETRY_OTLPENDPOINT`) to
   `http://localhost:8431`. `HostComposer` calls `AddComukiTelemetry` —
   options always `ValidateOnStart`; the OTLP SDK wires only when the
   endpoint is set (otherwise instruments stay cheap no-ops).
3. Grafana provisioned datasource scrapes / queries VictoriaMetrics as
   Prometheus. Metric names follow OTel→Prometheus translation
   (`comuki.runs.started` → `comuki_runs_started_total`, etc.).

VictoriaLogs (`:9428`) stays log-only for now — no Grafana Loki
datasource until a log dashboard lands.

## Known gaps (deferred to later phases)

- **No reverse proxy / TLS.** Add `caddy` or `traefik` for staging.
- **No secrets manager.** Real deploys go through Vault / AWS SM.
- **No backup strategy.** Postgres `pg_dump` and MinIO lifecycle
  rules are on the MVP Polish (Phase 8) backlog.
- **Worker image is minimal in Phase 4 (pi + bun, no Translator yet).**
  Real two-stage build (with `Comuki.Platform.Translator` AOT-deferred
  binary) lands in 04-03.

## Phase 4 — worker image & headless pi sanity check

The `worker` service in `docker-compose.yml` is **not started by default**
(it's behind `profiles: ["worker"]`). Opt in with `--profile worker` to
build, run, or test it.

### Build the image

```bash
cd deploy
podman compose --env-file .env --profile worker build worker
```

Image is tagged `comuki/worker:dev`. Contents: `oven/bun:1.3.10-bookworm-slim`
base + `@earendil-works/pi-coding-agent` installed globally. ENTRYPOINT is
`pi` so the container is invokable as
`podman run --rm comuki/worker:dev -p "..." --output-format stream-json`.

### Run pi headless and assert stream-json output (Slice 0 step 0)

```bash
cd deploy
bash scripts/test-pi-headless.sh        # POSIX
# or
powershell -ExecutionPolicy Bypass -File scripts/test-pi-headless.ps1
```

The script:
1. Verifies `ANTHROPIC_API_KEY` is set in `deploy/.env`
2. Builds the worker image
3. Runs `pi -p "$PI_TEST_PROMPT" --output-format stream-json` in the container
4. Greps for at least one `{"type": ...}` line in the output
5. Exits 0 on success, 1 on any failure with a descriptive error

If pi does not behave as a headless stream-json emitter, the rest of
Slice 0 has no foundation — this is the cheapest place to find out.

### Manual interactive use

```bash
podman run --rm -it \
  -e ANTHROPIC_API_KEY="$ANTHROPIC_API_KEY" \
  comuki/worker:dev \
  pi -p "What is 2+2?" --output-format stream-json
```

# Comuki — local development infra

Five self-hosted services, matching `comuki-stack.md` § 04 and
`comuki-architecture.md` § 04/06. All bind to localhost-only ports
and persist data to named Docker volumes.

> **Not for production.** Credentials are dev defaults, no TLS, no
> auth beyond simple passwords. For anything beyond a laptop, swap
> to a real secrets manager and reverse-proxy with TLS.

## What's here

| File | Purpose |
|---|---|
| `docker-compose.yml` | All five services + their volumes + healthchecks |
| `.env.example` | Copy to `.env` to override credentials |
| `postgres/init.d/01-pgvector.sql` | Enables `vector` extension (Phase 5, `Comuki.Platform.Knowledge`) |
| `worker.Dockerfile` | Sentinel — real worker image lands in Phase 3 (Slice 0 Step 2) |

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

## What connects to what

When the platform lands:

| Comuki piece | Talks to |
|---|---|
| `Comuki.Platform.Database.Runs` (EFCore, Phase 3) | Postgres :5432 |
| `Comuki.Platform.Artifacts` (Phase 7) | MinIO :9000 (S3 API) |
| Worker containers (Phase 3+) | pull deps from Nexus :8081 |
| `Comuki.Platform.Logging` (Phase 8) | VictoriaLogs :9428 (OTel logs) + VictoriaMetrics :8431 (OTel metrics) |

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

## Known gaps (deferred to later phases)

- **No reverse proxy / TLS.** Add `caddy` or `traefik` for staging.
- **No secrets manager.** Real deploys go through Vault / AWS SM.
- **No backup strategy.** Postgres `pg_dump` and MinIO lifecycle
  rules are on the MVP Polish (Phase 8) backlog.
- **Worker.Dockerfile is a sentinel.** Real image lands in
  Phase 3 (Slice 0 Step 2) — see file for the planned two-stage
  build.

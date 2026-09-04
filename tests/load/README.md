# Load test suite (issue #10 T11.5)

k6 scenarios that exercise the orchestrator host's three hottest paths
against a real or disposable deployment. The scripts are pure data files
— there is no project glue to compile or ship.

## Scripts

| File | Scenario | Default load | Default duration |
|------|----------|--------------|------------------|
| `login-then-list-runs.js` | 100 concurrent VUs: login → list runs | 100 VUs | 60 s |
| `webhook-ingest.js` | 50 RPS of fake GitHub issue payloads | 50 RPS | 30 s |
| `run-decisions.js` | 200 approve/cancel POSTs (split 50/50) | 10 VUs × 20 iters | up to 5 min |

The scripts use env-var configuration — defaults match the local
`podman compose up -d` deploy (`localhost:17173`). Override per-run:

```bash
BASE_URL=https://comuki.example.com \
EMAIL=load-test@comuki.test PASSWORD='load-test-pass' \
HOOK_KEY=<the per-provider key from deploy/.env> \
RUN_IDS=<comma-separated run uuids> \
k6 run tests/load/<script>.js
```

k6 is the only runtime dependency. Install once per box that runs load
tests (`brew install k6`, `winget install k6`, `apt install k6`) — no
npm, no Python.

## SLO thresholds

Each script encodes a `thresholds:` block. k6 aborts the run on SLO
breach — the exit code is non-zero so a CI gate can use the same
"non-zero = failure" rule as the unit / integration suites:

| Path | p95 budget | Notes |
|------|------------|-------|
| `POST /api/v1/auth/login` | 500 ms | bcrypt verification is the dominant cost; Set-Cookie header rides on the response. |
| `GET /api/v1/runs` | 200 ms | Read hot path. EF Core `AsNoTracking()` plus the filter DSL keeps this well below budget on warm data. |
| `POST /api/hooks/{provider}/{key}` | 200 ms | Anonymous, HMAC-verified, no auth handshake. |
| `POST /api/v1/runs/{id}/{approve,cancel}` | 150 ms | Single-row guarded update; 409 conflict responses are expected and budget the same as 204. |

All thresholds are `rate<0.01` (login / webhook) or `rate<0.05`
(decisions — the 409 conflict share inflates the error rate without
indicating a real failure).

## Disposable target with Testcontainers

The scripts speak HTTP; any host that satisfies `BASE_URL` works. For
local load runs without poisoning shared infra, spin up a one-shot
Testcontainers target:

```bash
# one-shot Postgres + MinIO
podman run -d --name comuki-load-postgres \
  -e POSTGRES_USER=comuki -e POSTGRES_PASSWORD=loadtest \
  -e POSTGRES_DB=comuki -p 5432:5432 \
  postgres:16-alpine

podman run -d --name comuki-load-minio \
  -e MINIO_ROOT_USER=test-access-key \
  -e MINIO_ROOT_PASSWORD=test-secret-key-with-enough-entropy \
  -p 9000:9000 -p 9001:9001 \
  minio/minio:latest server /data --console-address ":9001"

# apply migrations
export COMUKI_DB="Host=localhost;Database=comuki;Username=comuki;Password=loadtest"
dotnet run --project platform/src/host/Comuki.Migrator

# start the host pointed at the disposable stack
export COMUKI_BOOTSTRAP_ADMIN_EMAIL=load-test@comuki.test
export COMUKI_BOOTSTRAP_ADMIN_PASSWORD=load-test-pass-1
export ASPNETCORE_ENVIRONMENT=Production
dotnet run --project platform/src/host/Comuki.Host --urls http://localhost:17173 &
```

The k6 scripts can now point at `BASE_URL=http://localhost:17173` and
exercise the full surface. **Tear the stack down afterwards** — the
fixtures above carry dev credentials; reusing them past a load run is
a security footgun.

## CI integration

The scripts are authored without execution in this commit — CI wiring
is a follow-up. The shape the workflow should target:

```yaml
load-test:
  runs-on: [ubuntu-latest]
  steps:
    - uses: actions/checkout@v4
    - name: Install k6
      run: sudo apt-get install -y k6
    - name: Spin up Testcontainers target
      run: podman compose -f tests/load/compose.yml up -d
    - name: Wait for host health
      run: until curl -fs http://localhost:17173/health; do sleep 1; done
    - name: Run login + list-runs scenario
      run: k6 run tests/load/login-then-list-runs.js
    - name: Tear down
      run: podman compose -f tests/load/compose.yml down -v
```

The `tests/load/compose.yml` target is part of the follow-up PR.
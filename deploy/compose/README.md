# Comuki self-host — Docker Compose

Five-minute local stack: Postgres (pgvector) + MinIO + one-shot
migrator + the orchestrator host + the dashboard behind nginx. The
`worker-image` one-shot service builds the worker image the host
spawns through the Docker compute provider.

```bash
cp .env.example .env          # defaults boot a localhost stack
docker compose up -d --build  # first build: dotnet + bun, grab coffee
open http://localhost:17173   # log in with the bootstrap admin from .env
```

Teardown: `docker compose down` (keep data) / `docker compose down -v` (wipe).

Full documentation: [`../oss/README.md`](../oss/README.md).

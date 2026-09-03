# Operations notes — install

> New here? Read [`storage.md`](./storage.md) for the overall storage
> layout, [`database-schemas.md`](./database-schemas.md) for the eight
> per-DbContext Postgres schemas, and [`../index.md`](../index.md) for
> the rest of the operations docs (OIDC, MinIO, OpenAPI codegen, FE
> settings).

## Migrator — database credentials (issue #21)

The migrator's committed `appsettings.json` carries an empty password
(`Password=`). Deployers MUST supply credentials at runtime via one of:

- **Full connection string** — set `COMUKI_DB` to the entire Npgsql
  connection string. Wins over everything else.
- **Legacy alias** — `COMUKI_DATABASE` is honored with a console warning;
  rename to `COMUKI_DB` at the next deploy.
- **Password override** — leave `ConnectionStrings:Comuki` at its
  appsettings default and set `COMUKI_MIGRATOR_DB_PASSWORD`. Used to
  fill the empty `Password=` slot in the appsettings connection string.

`Production` startup refuses to run when the resolved connection string
has no password — the migrator exits with a setup hint pointing at
`COMUKI_MIGRATOR_DB_PASSWORD` and `COMUKI_DB`.

## Related

- [storage.md](./storage.md) — Postgres + MinIO + Victoria layout
  and retention policies.
- [database-schemas.md](./database-schemas.md) — per-context schemas
  (`orchestration`, `identity`, `projects`, `memory`, `chat`, `intake`,
  `costs`, `artifacts`), per-schema `__ef_migrations_history`, Migrator
  loop semantics.
- [minio.md](./minio.md) — `Artifacts:Minio:*` env-var mapping; same
  env-only-secret discipline as `COMUKI_MIGRATOR_DB_PASSWORD`.
- [oauth-oidc.md](./oauth-oidc.md) — `COMUKI_OIDC_CLIENT_SECRET` and
  the per-provider `ClientSecretEnv` pattern.

# Operations notes — install

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

# Postgres schemas (one per DbContext)

Every `DbContext` owns a Postgres schema. Eight modules, eight
schemas. The Migrator runs `EnsureSchema` → `MigrateAsync` per
context — applications cannot collide because each context also has
its own per-schema `__ef_migrations_history` table.

> Issue: #26 — Introduce real Postgres schemas per DbContext
> (orchestration / identity / projects / memory / chat / intake /
> costs / artifacts).

## Schema map

| Schema           | Owning context        | Module              | Tables |
|------------------|-----------------------|---------------------|--------|
| `orchestration`  | `OrchestrationDbContext` | `Comuki.Engine.Orchestration` | `runs`, `work_items`, `work_item_dependencies`, `run_events` |
| `identity`       | `IdentityDbContext`      | `Comuki.Modules.Identity`     | `users`, `api_keys`, `role_assignments`, `oidc_links` |
| `projects`       | `ProjectsDbContext`      | `Comuki.Modules.Projects`     | `projects`, `project_settings` |
| `memory`         | `MemoryDbContext`        | `Comuki.Modules.Memory`       | `chat_messages`, `chat_checkpoints`, `memory_facts`, `learning_candidates` |
| `chat`           | `ChatDbContext`          | `Comuki.Modules.Chat`         | `chat_sessions`, `chat_messages`, `chat_checkpoints` |
| `intake`         | `IntakeDbContext`        | `Comuki.Modules.Intake`       | `intake_tickets`, `intake_deliveries`, `source_connections`, `admission_rules`, `sync_jobs` |
| `costs`          | `CostsDbContext`         | `Comuki.Modules.Costs`        | `usage_events` |
| `artifacts`      | `ArtifactsDbContext`     | `Comuki.Modules.Artifacts`    | `run_bundles` |

> **Note on `chat` vs `memory`.** The Chat module uses Voluta for the
> graph engine; its domain `ChatSession`/`ChatMessage`/`ChatCheckpoint`
> live in the `chat` schema (a fork of Voluta's defaults onto the
> memory-contract name `chat_checkpoints`). The Memory module's
> embeddings, long-term facts, and learning candidates live in
> `memory`. The two write `chat_messages` independently — the table
> names happen to match because the contract is shared, but they are
> different physical tables in different schemas.

## Per-context `<Database>` static class

Each module exposes a `*Database` static class that holds the
schema name and table-name constants. Single source every
`IEntityTypeConfiguration` reads; **no magic strings** in
`builder.ToTable(…)`.

```csharp
// platform/src/modules/Projects/.../Persistence/ProjectsDatabase.cs
public static class ProjectsDatabase
{
    public const string Schema          = "projects";
    public const string Projects        = "projects";
    public const string ProjectSettings = "project_settings";
}
```

```csharp
// platform/src/modules/Artifacts/.../Persistence/ArtifactsDatabase.cs
public static class ArtifactsDatabase
{
    public const string Schema     = "artifacts";
    public const string RunBundles = "run_bundles";
}
```

…and similarly for `OrchestrationDatabase`, `IdentityDatabase`,
`MemoryDatabase`, `ChatDatabase`, `IntakeDatabase`, `CostsDatabase`.
Every `IEntityTypeConfiguration` does `builder.ToTable(<XxxDatabase>.X, <XxxDatabase>.Schema)` and never types the literal.

## Per-context migration history

EF Core's Postgres convention writes `__ef_migrations_history` to
`public` by default. We pin it to the schema via
`npgsql.MigrationsHistoryTable(name, schema)` in every context's
`ApplyOptions`:

```csharp
// Comuki.Modules.Projects.Infrastructure/Persistence/ProjectsDbContext.cs
public static void ApplyOptions(DbContextOptionsBuilder builder, string connectionString)
{
    builder
        .UseNpgsql(connectionString, static npgsql =>
            npgsql.MigrationsHistoryTable("__ef_migrations_history", ProjectsDatabase.Schema))
        .UseSnakeCaseNamingConvention();
}
```

`__ef_migrations_history` is the framework's hardcoded table name;
the schema is the only knob. **All eight contexts use this pattern;
none of them share history.** That's what lets the Migrator loop
through every context independently — adding a new migration to
Memory doesn't touch the Projects history table.

## Migrator loop (`Comuki.Migrator/Program.cs`)

```csharp
foreach (var (db, label) in new (DbContext, string)[] {
    (orchestrationDb, "orchestration"),
    (identityDb,     "identity"),
    (projectsDb,     "projects"),
    (memoryDb,       "memory"),
    (chatDb,         "chat"),
    (intakeDb,       "intake"),
    (costsDb,        "costs"),
    (artifactsDb,    "artifacts"),
})
{
    await DatabaseSchemaEnsurer.EnsureAsync(connectionString, SchemaFor(label), ct);
    await ApplyAsync(db, label);
}
```

`DatabaseSchemaEnsurer` does `CREATE SCHEMA IF NOT EXISTS <name>;` —
idempotent. `ApplyAsync` reads `GetPendingMigrationsAsync()`, runs
`MigrateAsync()`, and logs the result. The Migrator prints one
line per applied migration (`applied (<label>): <MigrationName>`) so
operators see exactly which schemas moved.

### `--recreate`

`dotnet run --project platform/src/host/Comuki.Migrator -- --recreate`
drops the orchestration database before the per-schema migration
loop. Only the orchestration context owns `EnsureDeletedAsync()`
because that's the one whose `__ef_migrations_history` is the
authoritative schema-creation gate; dropping the database recreates
the empty Postgres database, then `EnsureSchema` + `MigrateAsync`
re-establishes every schema and table.

## Application code surface

Application code that talks to the database uses the schema constant:

```csharp
// Entity configuration — schema + table names come from <Database>.
builder.ToTable(ProjectsDatabase.Projects, ProjectsDatabase.Schema);

// SQL — schema literal only inside mapper / migration code.
$"""
SELECT p.id
FROM {ProjectsDatabase.Schema}.{ProjectsDatabase.Projects} p
WHERE ...
"""
```

Raw SQL is rare (`ef-core.md` — `ExecuteUpdate`/`ExecuteDelete` is
the default for writes); when it does appear, the schema and table
names come from the `<Database>` static class.

## Migrations

Each context's migrations live under
`<Module>.Infrastructure/Migrations/<ContextName>/`:

```
platform/src/modules/Projects/.../Migrations/
├── 20260831122910_InitialProjectsSchema.cs
├── 20260902080854_AddProjectBudgetCaps.cs
└── 20260903085512_UseSchemas.cs
```

The `UseSchemas` migration per context is the one that ran as part
of issue #26 — it transitioned from the pre-#26 layout (all tables
in `public`) to the per-schema layout (each table in its module's
schema). Each context ships one `UseSchemas` migration; the order is
the standard `Initial → AddProjectBudgetCaps → UseSchemas` chain
(see `ef-migrations.md` for the tool-generated-only rule and the
snapshot-coupling explanation).

## Sources

- `platform/src/host/Comuki.Migrator/Program.cs` — loop, `ApplyAsync`,
  `--recreate` handler.
- `platform/src/host/Comuki.Migrator/DatabaseSchemaEnsurer.cs` —
  `CREATE SCHEMA IF NOT EXISTS` idempotent bootstrap.
- `platform/src/modules/Projects/.../Persistence/ProjectsDatabase.cs`,
  `IdentityDatabase.cs`, `MemoryDatabase.cs`, `ChatDatabase.cs`,
  `IntakeDatabase.cs`, `CostsDatabase.cs`, `ArtifactsDatabase.cs` —
  per-context schema + table constants.
- `platform/src/modules/Projects/.../Persistence/ProjectsDbContext.cs`
  (and the other seven `XxxDbContext.cs`) — `ApplyOptions` with
  `MigrationsHistoryTable("__ef_migrations_history", Schema)`.
- `platform/src/engine/Comuki.Engine.Orchestration/Infrastructure/Persistence/OrchestrationDatabase.cs`
  — the engine-side schema name + table constants.

## Related

- [storage.md](./storage.md) — Postgres (this doc), MinIO, Victoria stack
  and retention.
- [install.md](./install.md) — Migrator DB credentials; how `COMUKI_DB` /
  `COMUKI_MIGRATOR_DB_PASSWORD` flow through the connection-string
  resolution.
- `~/.agents/rules/csharp/ef-core.md` — entity configuration rules,
  snake_case, `IEntityTypeConfiguration<T>` per entity.
- `~/.agents/rules/csharp/ef-migrations.md` — tool-generated-only
  migrations, snapshot coupling, recovery procedure.

## Anti-patterns

- ❌ Writing the schema name as a string literal anywhere — `builder.ToTable("projects", "projects")`
  is a `ef-core.md` no-no. Use `<XxxDatabase>.Schema`.
- ❌ Sharing `__ef_migrations_history` between contexts — they each
  own their own per-schema history table. Cross-context sharing
  would couple their migration lifecycles.
- ❌ Reading or writing across schemas in domain code — the schema
  boundary is the persistence boundary; cross-schema queries belong in
  the host's adapter layer, not in module code.
- ❌ Hand-editing migrations under `<Module>.Infrastructure/Migrations/<ContextName>/`
  — `dotnet ef migrations add/remove` only. The snapshot is
  whitespace- and comment-sensitive (see `ef-migrations.md` §"Hard rule").
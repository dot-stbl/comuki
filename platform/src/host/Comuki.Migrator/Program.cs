using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Migrator;
using Comuki.Migrator.Sources;
using Comuki.Modules.Artifacts.Infrastructure.Persistence;
using Comuki.Modules.Chat.Infrastructure.Persistence;
using Comuki.Modules.Costs.Infrastructure.Persistence;
using Comuki.Modules.Identity.Infrastructure.Persistence;
using Comuki.Modules.Intake.Infrastructure.Persistence;
using Comuki.Modules.Memory.Infrastructure.Persistence;
using Comuki.Modules.Projects.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var recreate = args.Contains("--recreate", StringComparer.Ordinal);

var connectionString = ConnectionStringSource.TryResolve(out var fromLegacyAlias);
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine(
        $"connection string not found: set the {ConnectionStringSource.EnvVariable} env var "
        + "or ConnectionStrings:Comuki in appsettings.json");
    return 1;
}

if (fromLegacyAlias)
{
    Console.Error.WriteLine(
        $"warning: connection string resolved from the legacy {ConnectionStringSource.LegacyEnvVariable} env var; "
        + $"rename it to {ConnectionStringSource.EnvVariable}");
}

if (recreate)
{
    var dropOptions = new DbContextOptionsBuilder<OrchestrationDbContext>();
    OrchestrationDbContext.ApplyOptions(dropOptions, connectionString);
    await using var forDrop = new OrchestrationDbContext(dropOptions.Options);

    await forDrop.Database.EnsureDeletedAsync();
    Console.WriteLine("database dropped (--recreate)");
}

// All module contexts migrate the same database; each keeps its own
// schema and per-schema migration history table
// (orchestration.__ef_migrations_history, identity.__ef_migrations_history,
// projects.__ef_migrations_history, memory.__ef_migrations_history,
// chat.__ef_migrations_history, intake.__ef_migrations_history,
// costs.__ef_migrations_history, artifacts.__ef_migrations_history),
// so the applications cannot collide.
var orchestrationOptions = new DbContextOptionsBuilder<OrchestrationDbContext>();
OrchestrationDbContext.ApplyOptions(orchestrationOptions, connectionString);
await using var orchestrationDb = new OrchestrationDbContext(orchestrationOptions.Options);
await DatabaseSchemaEnsurer.EnsureAsync(connectionString, OrchestrationDatabase.Schema, CancellationToken.None);
await ApplyAsync(orchestrationDb, "orchestration");

var identityOptions = new DbContextOptionsBuilder<IdentityDbContext>();
IdentityDbContext.ApplyOptions(identityOptions, connectionString);
await using var identityDb = new IdentityDbContext(identityOptions.Options);
await DatabaseSchemaEnsurer.EnsureAsync(connectionString, IdentityDatabase.Schema, CancellationToken.None);
await ApplyAsync(identityDb, "identity");

var projectsOptions = new DbContextOptionsBuilder<ProjectsDbContext>();
ProjectsDbContext.ApplyOptions(projectsOptions, connectionString);
await using var projectsDb = new ProjectsDbContext(projectsOptions.Options);
await DatabaseSchemaEnsurer.EnsureAsync(connectionString, ProjectsDatabase.Schema, CancellationToken.None);
await ApplyAsync(projectsDb, "projects");

var memoryOptions = new DbContextOptionsBuilder<MemoryDbContext>();
MemoryDbContext.ApplyOptions(memoryOptions, connectionString);
await using var memoryDb = new MemoryDbContext(memoryOptions.Options);
await DatabaseSchemaEnsurer.EnsureAsync(connectionString, MemoryDatabase.Schema, CancellationToken.None);
await ApplyAsync(memoryDb, "memory");

var chatOptions = new DbContextOptionsBuilder<ChatDbContext>();
ChatDbContext.ApplyOptions(chatOptions, connectionString);
await using var chatDb = new ChatDbContext(chatOptions.Options);
await DatabaseSchemaEnsurer.EnsureAsync(connectionString, ChatDatabase.Schema, CancellationToken.None);
await ApplyAsync(chatDb, "chat");

var intakeOptions = new DbContextOptionsBuilder<IntakeDbContext>();
IntakeDbContext.ApplyOptions(intakeOptions, connectionString);
await using var intakeDb = new IntakeDbContext(intakeOptions.Options);
await DatabaseSchemaEnsurer.EnsureAsync(connectionString, IntakeDatabase.Schema, CancellationToken.None);
await ApplyAsync(intakeDb, "intake");

var costsOptions = new DbContextOptionsBuilder<CostsDbContext>();
CostsDbContext.ApplyOptions(costsOptions, connectionString);
await using var costsDb = new CostsDbContext(costsOptions.Options);
await DatabaseSchemaEnsurer.EnsureAsync(connectionString, CostsDatabase.Schema, CancellationToken.None);
await ApplyAsync(costsDb, "costs");

var artifactsOptions = new DbContextOptionsBuilder<ArtifactsDbContext>();
ArtifactsDbContext.ApplyOptions(artifactsOptions, connectionString);
await using var artifactsDb = new ArtifactsDbContext(artifactsOptions.Options);
await DatabaseSchemaEnsurer.EnsureAsync(connectionString, ArtifactsDatabase.Schema, CancellationToken.None);
await ApplyAsync(artifactsDb, "artifacts");

return 0;

static async Task ApplyAsync(DbContext db, string label)
{
    var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
    await db.Database.MigrateAsync();

    foreach (var migration in pending)
    {
        Console.WriteLine($"applied ({label}): {migration}");
    }

    var total = (await db.Database.GetAppliedMigrationsAsync()).ToList();
    Console.WriteLine($"{label} schema is up to date ({total.Count} migration(s) in history)");
}

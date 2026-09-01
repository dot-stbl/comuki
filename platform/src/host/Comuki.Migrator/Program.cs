using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Migrator;
using Comuki.Modules.Chat.Infrastructure.Persistence;
using Comuki.Modules.Identity.Infrastructure.Persistence;
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

    _ = await forDrop.Database.EnsureDeletedAsync();
    Console.WriteLine("database dropped (--recreate)");
}

// All module contexts migrate the same database; each keeps its own
// migrations history table (identity uses __comuki_identity, projects
// uses __comuki_projects), so the applications cannot collide.
var orchestrationOptions = new DbContextOptionsBuilder<OrchestrationDbContext>();
OrchestrationDbContext.ApplyOptions(orchestrationOptions, connectionString);
await using var orchestrationDb = new OrchestrationDbContext(orchestrationOptions.Options);
await ApplyAsync(orchestrationDb, "orchestration");

var identityOptions = new DbContextOptionsBuilder<IdentityDbContext>();
IdentityDbContext.ApplyOptions(identityOptions, connectionString);
await using var identityDb = new IdentityDbContext(identityOptions.Options);
await ApplyAsync(identityDb, "identity");

var projectsOptions = new DbContextOptionsBuilder<ProjectsDbContext>();
ProjectsDbContext.ApplyOptions(projectsOptions, connectionString);
await using var projectsDb = new ProjectsDbContext(projectsOptions.Options);
await ApplyAsync(projectsDb, "projects");

var chatOptions = new DbContextOptionsBuilder<ChatDbContext>();
ChatDbContext.ApplyOptions(chatOptions, connectionString);
await using var chatDb = new ChatDbContext(chatOptions.Options);
await ApplyAsync(chatDb, "chat");

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

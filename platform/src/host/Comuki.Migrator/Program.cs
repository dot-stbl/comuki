using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Migrator;
using Comuki.Modules.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var recreate = args.Contains("--recreate", StringComparer.Ordinal);

var connectionString = ConnectionStringSource.Resolve();
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine(
        $"connection string not found: set the {ConnectionStringSource.EnvVariable} env var "
        + "or ConnectionStrings:Comuki in appsettings.json");
    return 1;
}

if (recreate)
{
    var dropOptions = new DbContextOptionsBuilder<OrchestrationDbContext>();
    OrchestrationDbContext.ApplyOptions(dropOptions, connectionString);
    await using var forDrop = new OrchestrationDbContext(dropOptions.Options);

    _ = await forDrop.Database.EnsureDeletedAsync();
    Console.WriteLine("database dropped (--recreate)");
}

// Both module contexts migrate the same database; each keeps its own
// migrations history table (identity uses __comuki_identity), so the two
// applications cannot collide.
var orchestrationOptions = new DbContextOptionsBuilder<OrchestrationDbContext>();
OrchestrationDbContext.ApplyOptions(orchestrationOptions, connectionString);
await using var orchestrationDb = new OrchestrationDbContext(orchestrationOptions.Options);
await ApplyAsync(orchestrationDb, "orchestration");

var identityOptions = new DbContextOptionsBuilder<IdentityDbContext>();
IdentityDbContext.ApplyOptions(identityOptions, connectionString);
await using var identityDb = new IdentityDbContext(identityOptions.Options);
await ApplyAsync(identityDb, "identity");

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

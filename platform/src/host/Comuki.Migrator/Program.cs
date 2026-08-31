using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Migrator;
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

var options = new DbContextOptionsBuilder<OrchestrationDbContext>();
OrchestrationDbContext.ApplyOptions(options, connectionString);

await using var db = new OrchestrationDbContext(options.Options);

if (recreate)
{
    await db.Database.EnsureDeletedAsync();
    Console.WriteLine("database dropped (--recreate)");
}

var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
await db.Database.MigrateAsync();

foreach (var migration in pending)
{
    Console.WriteLine($"applied: {migration}");
}

var total = (await db.Database.GetAppliedMigrationsAsync()).ToList();
Console.WriteLine($"orchestration schema is up to date ({total.Count} migration(s) in history)");

return 0;

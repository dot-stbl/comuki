using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Migrator;
using Comuki.Migrator.Sources;
using Comuki.Modules.Artifacts.Infrastructure.Persistence;
using Comuki.Modules.Chat.Infrastructure.Persistence;
using Comuki.Modules.Costs.Infrastructure.Persistence;
using Comuki.Modules.Identity.Infrastructure.Persistence;
using Comuki.Modules.Intake.Infrastructure.Persistence;
using Comuki.Modules.Knowledge.Infrastructure.Persistence;
using Comuki.Modules.Memory.Infrastructure.Persistence;
using Comuki.Modules.Projects.Infrastructure.Persistence;
using Comuki.Modules.Scheduler.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var recreate = args.Contains("--recreate", StringComparer.Ordinal);

var connectionString = ConnectionStringSource.TryResolve(out var fromLegacyAlias);
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine(
        $"connection string not found: set the {ConnectionStringSource.EnvVariable} env var "
        + "or connectionStrings.comuki in config.toml");
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

foreach (var target in MigratorTargets.All)
{
    await target.RunAsync(connectionString, CancellationToken.None);
}

return 0;

/// <summary>
/// One module migration step: creates the module context, ensures its
/// schema exists, then applies pending migrations with per-schema
/// reporting.
/// </summary>
file sealed class MigratorTarget(string label, string schema, Func<string, DbContext> createContext)
{
    public async Task RunAsync(string connectionString, CancellationToken cancellationToken)
    {
        var context = createContext(connectionString);
        await using (context)
        {
            await DatabaseSchemaEnsurer.EnsureAsync(connectionString, schema, cancellationToken);

            var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
            await context.Database.MigrateAsync(cancellationToken);

            foreach (var migration in pending)
            {
                Console.WriteLine($"applied ({label}): {migration}");
            }

            var total = (await context.Database.GetAppliedMigrationsAsync(cancellationToken)).ToList();
            Console.WriteLine($"{label} schema is up to date ({total.Count} migration(s) in history)");
        }
    }
}

/// <summary>
/// The module contexts in migration order. All of them migrate the same
/// database; each keeps its own schema and per-schema migration history
/// table (orchestration.__ef_migrations_history,
/// identity.__ef_migrations_history, …) so the applications cannot
/// collide.
/// </summary>
file static class MigratorTargets
{
    public static readonly MigratorTarget[] All =
    [
        new("orchestration", OrchestrationDatabase.Schema, static connectionString =>
        {
            var builder = new DbContextOptionsBuilder<OrchestrationDbContext>();
            OrchestrationDbContext.ApplyOptions(builder, connectionString);
            return new OrchestrationDbContext(builder.Options);
        }),
        new("identity", IdentityDatabase.Schema, static connectionString =>
        {
            var builder = new DbContextOptionsBuilder<IdentityDbContext>();
            IdentityDbContext.ApplyOptions(builder, connectionString);
            return new IdentityDbContext(builder.Options);
        }),
        new("projects", ProjectsDatabase.Schema, static connectionString =>
        {
            var builder = new DbContextOptionsBuilder<ProjectsDbContext>();
            ProjectsDbContext.ApplyOptions(builder, connectionString);
            return new ProjectsDbContext(builder.Options);
        }),
        new("memory", MemoryDatabase.Schema, static connectionString =>
        {
            var builder = new DbContextOptionsBuilder<MemoryDbContext>();
            MemoryDbContext.ApplyOptions(builder, connectionString);
            return new MemoryDbContext(builder.Options);
        }),
        new("knowledge", KnowledgeDatabase.Schema, static connectionString =>
        {
            var builder = new DbContextOptionsBuilder<KnowledgeDbContext>();
            KnowledgeDbContext.ApplyOptions(builder, connectionString);
            return new KnowledgeDbContext(builder.Options);
        }),
        new("chat", ChatDatabase.Schema, static connectionString =>
        {
            var builder = new DbContextOptionsBuilder<ChatDbContext>();
            ChatDbContext.ApplyOptions(builder, connectionString);
            return new ChatDbContext(builder.Options);
        }),
        new("intake", IntakeDatabase.Schema, static connectionString =>
        {
            var builder = new DbContextOptionsBuilder<IntakeDbContext>();
            IntakeDbContext.ApplyOptions(builder, connectionString);
            return new IntakeDbContext(builder.Options);
        }),
        new("costs", CostsDatabase.Schema, static connectionString =>
        {
            var builder = new DbContextOptionsBuilder<CostsDbContext>();
            CostsDbContext.ApplyOptions(builder, connectionString);
            return new CostsDbContext(builder.Options);
        }),
        new("artifacts", ArtifactsDatabase.Schema, static connectionString =>
        {
            var builder = new DbContextOptionsBuilder<ArtifactsDbContext>();
            ArtifactsDbContext.ApplyOptions(builder, connectionString);
            return new ArtifactsDbContext(builder.Options);
        }),
        new("scheduler", SchedulerDatabase.Schema, static connectionString =>
        {
            var builder = new DbContextOptionsBuilder<SchedulerDbContext>();
            SchedulerDbContext.ApplyOptions(builder, connectionString);
            return new SchedulerDbContext(builder.Options);
        }),
    ];
}

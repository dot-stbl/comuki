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
using Comuki.Shared.Bootstrap.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

// Comuki-native log surface (issue #54): one logger over the comuki
// console formatter; Console.WriteLine is gone.
using var loggerFactory = LoggerFactory.Create(static logging => logging.AddComukiConsole());
var logger = loggerFactory.CreateLogger("comuki.migrator");

var recreate = args.Contains("--recreate", StringComparer.Ordinal);

var connectionString = ConnectionStringSource.TryResolve(out var fromLegacyAlias);
if (string.IsNullOrWhiteSpace(connectionString))
{
    logger.LogError(
        "connection string not found: set the {EnvVariable} env var or connectionStrings.comuki in config.toml",
        ConnectionStringSource.EnvVariable);
    return 1;
}

if (fromLegacyAlias)
{
    logger.LogWarning(
        "connection string resolved from the legacy {LegacyEnvVariable} env var; rename it to {EnvVariable}",
        ConnectionStringSource.LegacyEnvVariable,
        ConnectionStringSource.EnvVariable);
}

if (recreate)
{
    var dropOptions = new DbContextOptionsBuilder<OrchestrationDbContext>();
    OrchestrationDbContext.ApplyOptions(dropOptions, connectionString);
    await using var forDrop = new OrchestrationDbContext(dropOptions.Options);

    await forDrop.Database.EnsureDeletedAsync();
    logger.LogInformation("database dropped (--recreate)");
}

// All module contexts migrate the same database; each keeps its own
// schema and per-schema migration history table
// (orchestration.__ef_migrations_history, identity.__ef_migrations_history,
// projects.__ef_migrations_history, memory.__ef_migrations_history,
// chat.__ef_migrations_history, intake.__ef_migrations_history,
// costs.__ef_migrations_history, artifacts.__ef_migrations_history,
// knowledge.__ef_migrations_history),
// so the applications cannot collide.
var orchestrationOptions = new DbContextOptionsBuilder<OrchestrationDbContext>();
OrchestrationDbContext.ApplyOptions(orchestrationOptions, connectionString);
await using var orchestrationDb = new OrchestrationDbContext(orchestrationOptions.Options);
await DatabaseSchemaEnsurer.EnsureAsync(connectionString, OrchestrationDatabase.Schema, CancellationToken.None);
await ApplyAsync(logger, orchestrationDb, "orchestration");

var identityOptions = new DbContextOptionsBuilder<IdentityDbContext>();
IdentityDbContext.ApplyOptions(identityOptions, connectionString);
await using var identityDb = new IdentityDbContext(identityOptions.Options);
await DatabaseSchemaEnsurer.EnsureAsync(connectionString, IdentityDatabase.Schema, CancellationToken.None);
await ApplyAsync(logger, identityDb, "identity");

var projectsOptions = new DbContextOptionsBuilder<ProjectsDbContext>();
ProjectsDbContext.ApplyOptions(projectsOptions, connectionString);
await using var projectsDb = new ProjectsDbContext(projectsOptions.Options);
await DatabaseSchemaEnsurer.EnsureAsync(connectionString, ProjectsDatabase.Schema, CancellationToken.None);
await ApplyAsync(logger, projectsDb, "projects");

var memoryOptions = new DbContextOptionsBuilder<MemoryDbContext>();
MemoryDbContext.ApplyOptions(memoryOptions, connectionString);
await using var memoryDb = new MemoryDbContext(memoryOptions.Options);
await DatabaseSchemaEnsurer.EnsureAsync(connectionString, MemoryDatabase.Schema, CancellationToken.None);
await ApplyAsync(logger, memoryDb, "memory");

var knowledgeOptions = new DbContextOptionsBuilder<KnowledgeDbContext>();
KnowledgeDbContext.ApplyOptions(knowledgeOptions, connectionString);
await using var knowledgeDb = new KnowledgeDbContext(knowledgeOptions.Options);
await DatabaseSchemaEnsurer.EnsureAsync(connectionString, KnowledgeDatabase.Schema, CancellationToken.None);
await ApplyAsync(logger, knowledgeDb, "knowledge");

var chatOptions = new DbContextOptionsBuilder<ChatDbContext>();
ChatDbContext.ApplyOptions(chatOptions, connectionString);
await using var chatDb = new ChatDbContext(chatOptions.Options);
await DatabaseSchemaEnsurer.EnsureAsync(connectionString, ChatDatabase.Schema, CancellationToken.None);
await ApplyAsync(logger, chatDb, "chat");

var intakeOptions = new DbContextOptionsBuilder<IntakeDbContext>();
IntakeDbContext.ApplyOptions(intakeOptions, connectionString);
await using var intakeDb = new IntakeDbContext(intakeOptions.Options);
await DatabaseSchemaEnsurer.EnsureAsync(connectionString, IntakeDatabase.Schema, CancellationToken.None);
await ApplyAsync(logger, intakeDb, "intake");

var costsOptions = new DbContextOptionsBuilder<CostsDbContext>();
CostsDbContext.ApplyOptions(costsOptions, connectionString);
await using var costsDb = new CostsDbContext(costsOptions.Options);
await DatabaseSchemaEnsurer.EnsureAsync(connectionString, CostsDatabase.Schema, CancellationToken.None);
await ApplyAsync(logger, costsDb, "costs");

var artifactsOptions = new DbContextOptionsBuilder<ArtifactsDbContext>();
ArtifactsDbContext.ApplyOptions(artifactsOptions, connectionString);
await using var artifactsDb = new ArtifactsDbContext(artifactsOptions.Options);
await DatabaseSchemaEnsurer.EnsureAsync(connectionString, ArtifactsDatabase.Schema, CancellationToken.None);
await ApplyAsync(logger, artifactsDb, "artifacts");

var schedulerOptions = new DbContextOptionsBuilder<SchedulerDbContext>();
SchedulerDbContext.ApplyOptions(schedulerOptions, connectionString);
await using var schedulerDb = new SchedulerDbContext(schedulerOptions.Options);
await DatabaseSchemaEnsurer.EnsureAsync(connectionString, SchedulerDatabase.Schema, CancellationToken.None);
await ApplyAsync(logger, schedulerDb, "scheduler");

return 0;

static async Task ApplyAsync(ILogger logger, DbContext db, string label)
{
    var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
    await db.Database.MigrateAsync();

    foreach (var migration in pending)
    {
        logger.LogInformation("applied ({Schema}): {Migration}", label, migration);
    }

    var total = (await db.Database.GetAppliedMigrationsAsync()).ToList();
    logger.LogInformation("{Schema} schema is up to date ({Count} migration(s) in history)", label, total.Count);
}

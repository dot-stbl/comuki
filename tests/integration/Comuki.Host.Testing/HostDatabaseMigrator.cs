using Comuki.Engine.Orchestration.Infrastructure.Persistence;
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

namespace Comuki.Host.Testing;

/// <summary>
/// Migrates every module's EF Core context against one Postgres connection
/// string — the same ten contexts <c>Comuki.Migrator</c> applies before
/// production boots (same order, same one-context-per-schema contract).
/// </summary>
/// <remarks>
/// Before this type existed, each integration harness hand-listed which
/// contexts to migrate, and every one of them fell behind as the host grew
/// modules: <c>Host.Integration.Auth</c> migrated three of ten,
/// <c>Host.Integration.Intake</c> five of ten — both failed at runtime with
/// <c>42P01: relation "…" does not exist</c> the moment a hosted service
/// the host wires unconditionally (schedulers, sweepers, packagers) touched
/// a module's table the harness never migrated. <see cref="targets"/> is
/// now the single place a new module's context joins the migrated set — an
/// eleventh module means one new entry here, not an edit to every
/// <c>Host*Server.cs</c> fixture in <c>tests/integration/</c>.
/// </remarks>
public static class HostDatabaseMigrator
{
    /// <summary>Applies every module's pending migrations against <paramref name="connectionString"/>, one context at a time.</summary>
    /// <param name="connectionString">Postgres connection string (the Testcontainers instance the harness just started).</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    public static async Task MigrateAllAsync(string connectionString, CancellationToken cancellationToken)
    {
        foreach (var migrate in targets)
        {
            await migrate(connectionString, cancellationToken);
        }
    }

    /// <summary>
    /// One migration step per module context, in the same order
    /// <c>Comuki.Migrator</c> applies them. Each context owns its own
    /// schema and its own <c>__ef_migrations_history</c> table, so the
    /// contexts never collide even though they share one database.
    /// </summary>
    private static readonly IReadOnlyList<Func<string, CancellationToken, Task>> targets =
    [
        Target<OrchestrationDbContext>(OrchestrationDbContext.ApplyOptions, static options => new OrchestrationDbContext(options)),
        Target<IdentityDbContext>(IdentityDbContext.ApplyOptions, static options => new IdentityDbContext(options)),
        Target<ProjectsDbContext>(ProjectsDbContext.ApplyOptions, static options => new ProjectsDbContext(options)),
        Target<MemoryDbContext>(MemoryDbContext.ApplyOptions, static options => new MemoryDbContext(options)),
        Target<KnowledgeDbContext>(KnowledgeDbContext.ApplyOptions, static options => new KnowledgeDbContext(options)),
        Target<ChatDbContext>(ChatDbContext.ApplyOptions, static options => new ChatDbContext(options)),
        Target<IntakeDbContext>(IntakeDbContext.ApplyOptions, static options => new IntakeDbContext(options)),
        Target<CostsDbContext>(CostsDbContext.ApplyOptions, static options => new CostsDbContext(options)),
        Target<ArtifactsDbContext>(ArtifactsDbContext.ApplyOptions, static options => new ArtifactsDbContext(options)),
        Target<SchedulerDbContext>(SchedulerDbContext.ApplyOptions, static options => new SchedulerDbContext(options)),
    ];

    /// <summary>
    /// Builds one migration step: applies the context's own
    /// <c>ApplyOptions</c> connection-string wiring, constructs it (a
    /// migration pass is system semantics — no <c>ISubjectScopeAccessor</c>
    /// — so <paramref name="createContext"/> takes the options alone), and
    /// migrates it.
    /// </summary>
    private static Func<string, CancellationToken, Task> Target<TContext>(
        Action<DbContextOptionsBuilder, string> applyOptions,
        Func<DbContextOptions<TContext>, TContext> createContext)
        where TContext : DbContext
    {
        return async (connectionString, cancellationToken) =>
        {
            var builder = new DbContextOptionsBuilder<TContext>();
            applyOptions(builder, connectionString);

            await using var context = createContext(builder.Options);
            await context.Database.MigrateAsync(cancellationToken);
        };
    }
}

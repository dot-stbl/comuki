using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Modules.Artifacts.Infrastructure.Persistence;
using Comuki.Modules.Chat.Infrastructure.Persistence;
using Comuki.Modules.Costs.Infrastructure.Persistence;
using Comuki.Modules.Identity.Infrastructure.Persistence;
using Comuki.Modules.Intake.Infrastructure.Persistence;
using Comuki.Modules.Knowledge.Infrastructure.Persistence;
using Comuki.Modules.Memory.Infrastructure.Persistence;
using Comuki.Modules.Projects.Infrastructure.Persistence;
using Comuki.Modules.Repositories.Infrastructure.Persistence;
using Comuki.Modules.Scheduler.Infrastructure.Persistence;
using Comuki.Modules.Verify.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Comuki.Shared.Migrations.Targets;

/// <summary>
/// The module contexts in migration order. All of them migrate the same
/// database; each keeps its own schema and per-schema migration history
/// table (orchestration.__ef_migrations_history,
/// identity.__ef_migrations_history, …) so the applications cannot
/// collide. This is the single list the Migrator exe, the host's
/// boot-time auto-migrate and the integration-test harness share — a
/// new module means one new entry here.
/// </summary>
public static class MigrationTargets
{
    /// <summary>The module migration targets in execution order.</summary>
    public static readonly MigrationTarget[] All =
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
        new("verify", VerifyDatabase.Schema, static connectionString =>
        {
            var builder = new DbContextOptionsBuilder<VerifyDbContext>();
            VerifyDbContext.ApplyOptions(builder, connectionString);
            return new VerifyDbContext(builder.Options);
        }),
        new("repositories", RepositoriesDatabase.Schema, static connectionString =>
        {
            var builder = new DbContextOptionsBuilder<RepositoriesDbContext>();
            RepositoriesDbContext.ApplyOptions(builder, connectionString);
            return new RepositoriesDbContext(builder.Options);
        }),
    ];
}

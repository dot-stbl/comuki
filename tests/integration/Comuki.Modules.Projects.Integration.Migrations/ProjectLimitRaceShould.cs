using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Modules.Projects.Application;
using Comuki.Modules.Projects.Application.Projects.Create;
using Comuki.Modules.Projects.Infrastructure;
using Comuki.Modules.Projects.Infrastructure.Persistence;
using Comuki.Shared.Editions.Edition;
using Comuki.Shared.Kernel.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Testcontainers.PostgreSql;
using Xunit;

namespace Comuki.Modules.Projects.Integration.Migrations;

/// <summary>
/// Proves the project-count quota is enforced under concurrent writers
/// on a real Postgres: two parallel <see cref="CreateProjectHandler"/>
/// calls with <c>cap = 1</c> must serialise on the
/// <c>pg_advisory_xact_lock(hashtext('limit:projects'))</c> — the first
/// commits, the second sees the new row inside its count and refuses
/// with <see cref="ProviderForbiddenException"/>(<c>edition.limit_exceeded</c>).
/// Before the transactional enforcement, the gate-side
/// <see cref="Application.Editions.ProjectCountLimitUsageProvider"/>
/// raced with the handler insert (read-then-write TOCTOU), so two
/// callers at <c>cap = 1</c> both saw <c>current = 0</c> and both inserted
/// — a permission-style hole the count-quota axis must not have.
/// <para>
/// Same container / migration setup as
/// <see cref="ProjectsMigrationsShould"/>: real Postgres, real EF
/// migrations, real <see cref="CreateProjectHandler"/>. The only
/// override on top of the production DI is the <see cref="IEdition"/>
/// registration — replaced with a fixed-cap stub so the test pins
/// <c>cap = 1</c> without dragging the licensing layer in.
/// </para>
/// </summary>
public sealed class ProjectLimitRaceShould : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    /// <summary>
    /// boundary: initialised in InitializeAsync before any test runs
    /// </summary>
    private ServiceProvider provider = null!;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await container.StartAsync(cancellationToken);

        var connectionString = container.GetConnectionString();

        // The migrator's contract: every module context migrates the same
        // database, each with its own migrations history table.
        var orchestrationOptions = new DbContextOptionsBuilder<OrchestrationDbContext>();
        OrchestrationDbContext.ApplyOptions(orchestrationOptions, connectionString);
        await using var orchestrationDb = new OrchestrationDbContext(orchestrationOptions.Options);
        await orchestrationDb.Database.MigrateAsync(cancellationToken);

        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddProjectsPersistence(connectionString);
        services.AddProjectsApplication();

        // Override IEdition with a fixed-cap stub so the test pins
        // cap = 1 without the licensing layer (ProductionEd25519PublicKey
        // is a placeholder key the test cannot mint against). The stub
        // reports Community/Absent for everything else — limit
        // resolution still uses the catalog so the handler count + cap
        // path runs against the real Limits.Projects entry.
        services.AddSingleton<IEdition>(static _ => new FixedCapEdition(cap: 1));

        provider = services.BuildServiceProvider();

        var db = provider.GetRequiredService<ProjectsDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await provider.DisposeAsync();
        await container.DisposeAsync();
    }

    [Fact(DisplayName = "Given cap=1, when two creates run in parallel, exactly one succeeds and one throws ProviderForbiddenException(edition.limit_exceeded)")]
    public async Task SerialiseOnProjectCountLimitAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        // Each call opens its own scope → its own DbContext → its own
        // connection. The pg_advisory_xact_lock serialises the two
        // transactions on the same lock hash; the first commits before
        // the second's count runs, so the second refuses.
        async Task<string?> TryCreateAsync(string name, string slug)
        {
            await using var scope = provider.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredService<CreateProjectHandler>();
            try
            {
                await handler.HandleAsync(new CreateProjectCommand(name, slug, null, null, null), cancellationToken);
                return null;
            }
            catch (ProviderForbiddenException exception)
            {
                return exception.Code;
            }
        }

        var outcomes = await Task.WhenAll(
            Task.Run(() => TryCreateAsync("First", "first-slug")),
            Task.Run(() => TryCreateAsync("Second", "second-slug")));

        outcomes.Count(static outcome => outcome is null).ShouldBe(1, "exactly one writer must commit at cap=1");
        outcomes.Count(static outcome => outcome == "edition.limit_exceeded").ShouldBe(1, "exactly one writer must refuse with edition.limit_exceeded");
    }
}

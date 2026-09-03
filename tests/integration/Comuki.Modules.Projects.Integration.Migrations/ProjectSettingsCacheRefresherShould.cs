using Comuki.Modules.Projects.Application.Settings;
using Comuki.Modules.Projects.Domain.Settings;
using Comuki.Modules.Projects.Infrastructure.Persistence;
using Comuki.Modules.Projects.Infrastructure.Persistence.Stores;
using Comuki.Shared.Kernel.Ids;
using Comuki.Shared.Kernel.Scoping;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Testcontainers.PostgreSql;
using Xunit;

namespace Comuki.Modules.Projects.Integration.Migrations;

/// <summary>
/// <see cref="ProjectSettingsCacheRefresher"/> over a real Testcontainers
/// Postgres + IDbContextFactory: the warmer pulls every settings row and
/// re-fills the in-process cache. Each project gets a fresh row seeded so
/// the cache-warming loop has something to iterate; an empty database is
/// also exercised as the "no rows, no-op" branch.
/// </summary>
public sealed class ProjectSettingsCacheRefresherShould : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    private IServiceProvider services = null!;
    private ProjectSettingsCache cache = null!;
    private IDbContextFactory<ProjectsDbContext> dbFactory = null!;
    private string connectionString = string.Empty;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await container.StartAsync(cancellationToken);
        connectionString = container.GetConnectionString();

        var optionsBuilder = new DbContextOptionsBuilder<ProjectsDbContext>();
        ProjectsDbContext.ApplyOptions(optionsBuilder, connectionString);

        await using (var db = new ProjectsDbContext(optionsBuilder.Options))
        {
            await db.Database.MigrateAsync(cancellationToken);
        }

        var provider = new ServiceCollection()
            .AddLogging()
            .AddMemoryCache()
            .AddSingleton(TimeProvider.System)
            .AddSingleton<ISubjectScopeAccessor, AsyncLocalSubjectScopeAccessor>()
            .AddDbContextFactory<ProjectsDbContext>(builder => ProjectsDbContext.ApplyOptions(builder, connectionString))
            .AddSingleton<ProjectSettingsCache>()
            .AddSingleton<ProjectSettingsCacheRefresher>()
            .BuildServiceProvider();

        services = provider;
        dbFactory = provider.GetRequiredService<IDbContextFactory<ProjectsDbContext>>();
        cache = provider.GetRequiredService<ProjectSettingsCache>();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (services is ServiceProvider provider)
        {
            await provider.DisposeAsync();
        }

        await container.DisposeAsync();
    }

    /// <summary>Wipes the data between tests so each one starts from a clean slate.</summary>
    private async Task ResetAsync()
    {
        await using var db = NewSystemContext();
        await db.ProjectSettings.ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        await db.Projects.ExecuteDeleteAsync(TestContext.Current.CancellationToken);
    }

    private async Task<ProjectSettings> SeedSettingsAsync(ProjectId projectId, DateTimeOffset now)
    {
        await using var db = NewSystemContext();

        // The argument projectId is the caller's intent; Project.Create
        // stamps its own id, so we capture it and reuse for ProjectSettings
        // to satisfy the FK on Project.Id.
        var project = Domain.Projects.Project.Create(
            $"name-{projectId.Value:N}"[..16],
            $"slug-{projectId.Value:N}"[..16],
            null,
            null,
            null,
            now);
        db.Projects.Add(project);

        var settings = ProjectSettings.CreateDefaults(project.Id, now);
        db.ProjectSettings.Add(settings);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return settings;
    }

    /// <summary>
    /// Direct construction with <c>null</c> scope accessor — the context
    /// falls back to <see cref="ProjectsDbContext.ScopeUnrestricted"/> and
    /// sees every row, sidestepping the no-scope guard the scoped DI path
    /// enforces. The factory-bound context used by the refresher keeps its
    /// own scope channel; the fixture-side writes never share state with it.
    /// </summary>
    private ProjectsDbContext NewSystemContext()
    {
        var builder = new DbContextOptionsBuilder<ProjectsDbContext>();
        ProjectsDbContext.ApplyOptions(builder, connectionString);
        return new ProjectsDbContext(builder.Options);
    }

    [Fact(DisplayName = "Given multiple seeded projects, when RefreshAllAsync runs, then every settings row is warmed into the cache")]
    public async Task RefreshAllWarmsEveryRowAsync()
    {
        await ResetAsync();
        var first = await SeedSettingsAsync(ProjectId.New(), DateTimeOffset.UtcNow);
        var second = await SeedSettingsAsync(ProjectId.New(), DateTimeOffset.UtcNow);

        var refresher = services.GetRequiredService<ProjectSettingsCacheRefresher>();
        await refresher.RefreshAllAsync(TestContext.Current.CancellationToken);

        cache.Get(first.ProjectId).ShouldNotBeNull();
        cache.Get(second.ProjectId).ShouldNotBeNull();
        cache.Get(first.ProjectId)!.ProjectId.ShouldBe(first.ProjectId);
        cache.Get(second.ProjectId)!.ProjectId.ShouldBe(second.ProjectId);
    }

    [Fact(DisplayName = "Given an empty projects schema, when RefreshAllAsync runs, then no cache entry is created")]
    public async Task RefreshAllOnEmptyDatabaseIsNoOpAsync()
    {
        await ResetAsync();

        var refresher = services.GetRequiredService<ProjectSettingsCacheRefresher>();
        await refresher.RefreshAllAsync(TestContext.Current.CancellationToken);

        cache.Get(ProjectId.New()).ShouldBeNull();
    }
}

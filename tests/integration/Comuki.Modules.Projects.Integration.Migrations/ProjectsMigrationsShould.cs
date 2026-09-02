using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Modules.Projects.Application;
using Comuki.Modules.Projects.Application.Ports;
using Comuki.Modules.Projects.Application.Projects.Archive;
using Comuki.Modules.Projects.Application.Projects.Create;
using Comuki.Modules.Projects.Application.Projects.Queries;
using Comuki.Modules.Projects.Application.Projects.Update;
using Comuki.Modules.Projects.Application.Settings;
using Comuki.Modules.Projects.Application.Settings.Update;
using Comuki.Modules.Projects.Domain.Projects;
using Comuki.Modules.Projects.Domain.Settings;
using Comuki.Modules.Projects.Infrastructure;
using Comuki.Modules.Projects.Infrastructure.Persistence;
using Comuki.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Testcontainers.PostgreSql;
using Xunit;

namespace Comuki.Modules.Projects.Integration.Migrations;

/// <summary>
/// Proves the Projects EF migrations (applied alongside the orchestration
/// context on one real Postgres) create the expected schema — tables, the
/// module-private migrations history, the unique slug index — and the
/// settings live-reload contract: create → read → update through the
/// handlers is visible to every reader immediately (shared cache, change
/// token), with the optimistic version refusing stale writers.
/// </summary>
public sealed class ProjectsMigrationsShould : IAsyncLifetime
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
        _ = services.AddProjectsPersistence(connectionString);
        _ = services.AddProjectsApplication();
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

    [Fact(DisplayName = "Given an empty database, when both contexts migrate it, then projects and orchestration tables coexist with separate histories")]
    public async Task CreateProjectsTablesAlongsideOrchestrationAsync()
    {
        var tables = await QuerySingleColumnAsync(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' ORDER BY table_name");
        var histories = await QuerySingleColumnAsync(
            "SELECT table_name FROM information_schema.tables "
            + "WHERE table_schema = 'public' AND table_name LIKE '\\_\\_%' ORDER BY table_name");

        tables.ShouldContain(ProjectsTables.Projects);
        tables.ShouldContain(ProjectsTables.ProjectSettings);
        tables.ShouldContain(OrchestrationTables.Runs);

        // module-private history, distinct from the orchestration default
        histories.ShouldContain(ProjectsTables.MigrationsHistory);
        histories.ShouldContain("__EFMigrationsHistory");
    }

    [Fact(DisplayName = "Given migrated projects, when indexes are inspected, then the unique slug index exists")]
    public async Task CreateUniqueSlugIndexAsync()
    {
        var definitions = await QuerySingleColumnAsync(
            "SELECT indexdef FROM pg_indexes WHERE schemaname = 'public' AND tablename = 'projects'");

        definitions.ShouldContain(static definition => definition.Contains("ix_projects_slug")
            && definition.Contains("UNIQUE")
            && definition.Contains("slug"));
    }

    [Fact(DisplayName = "Given a stored project, when another row with the same slug is saved, then the unique index refuses it")]
    public async Task RefuseDuplicateSlugAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ProjectsDbContext>();
        var first = Project.Create("First", "dup-slug", null, null, null, now);
        _ = db.Projects.Add(first);
        _ = db.ProjectSettings.Add(ProjectSettings.CreateDefaults(first.Id, now));
        _ = await db.SaveChangesAsync(cancellationToken);

        var second = Project.Create("Second", "dup-slug", null, null, null, now);
        _ = db.Projects.Add(second);
        _ = db.ProjectSettings.Add(ProjectSettings.CreateDefaults(second.Id, now));

        _ = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(cancellationToken));
    }

    [Fact(DisplayName = "Given a created project, when settings are read through the handler, then the default row comes back with version 1")]
    public async Task CreateProjectWithDefaultSettingsAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        ProjectId projectId;

        await using (var scope = provider.CreateAsyncScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<CreateProjectHandler>();
            var view = await handler.HandleAsync(
                new CreateProjectCommand("Web Platform", "web-platform", "portal", null, "refs/tags/v1"),
                cancellationToken);

            projectId = view.Id;
            view.Slug.ShouldBe("web-platform");
            view.Archived.ShouldBeFalse();
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var settings = scope.ServiceProvider.GetRequiredService<GetProjectSettingsHandler>();
            var view = await settings.HandleAsync(projectId, cancellationToken);

            view.MaxConcurrent.ShouldBe(ProjectSettings.DefaultMaxConcurrent);
            view.Version.ShouldBe(1);
        }
    }

    [Fact(DisplayName = "Given cached settings, when the handler updates them, then the shared cache and the change token fire without a restart")]
    public async Task LiveReloadSettingsThroughHandlerAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var projectId = await CreateProjectAsync("live-reload", cancellationToken);

        var settingsStore = provider.GetRequiredService<IProjectSettingsStore>();

        // prime the cache and arm the change token
        _ = await settingsStore.FindAsync(projectId, cancellationToken);
        var changeToken = settingsStore.GetChangeToken(projectId);
        changeToken.HasChanged.ShouldBeFalse();
        settingsStore.GetCached(projectId).ShouldNotBeNull();

        await using (var scope = provider.CreateAsyncScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<UpdateSettingsHandler>();
            var current = await settingsStore.FindAsync(projectId, cancellationToken);
            current.ShouldNotBeNull();

            var updated = await handler.HandleAsync(
                new UpdateSettingsCommand(
                    projectId,
                    current.Version,
                    MinIdle: 2,
                    MaxConcurrent: 12,
                    IdleTtlSeconds: 900,
                    ApproveRequired: true,
                    KnowledgeEnabled: true,
                    VerifyEnabled: false,
                    ProxyEnabled: true,
                    SoftBudgetUsdMicros: null,
                    HardBudgetUsdMicros: null),
                cancellationToken);

            updated.Version.ShouldBe(2);
            updated.MaxConcurrent.ShouldBe(12);
        }

        // the snapshot every sync reader sees reflects the write immediately
        changeToken.HasChanged.ShouldBeTrue();
        var cached = settingsStore.GetCached(projectId);
        cached.ShouldNotBeNull();
        cached.MaxConcurrent.ShouldBe(12);
        cached.IdleTtlSeconds.ShouldBe(900);
        cached.Version.ShouldBe(2);
    }

    [Fact(DisplayName = "Given settings at version 2, when a writer presents version 1, then the optimistic check refuses it")]
    public async Task RefuseStaleSettingsWriterAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var projectId = await CreateProjectAsync("stale-writer", cancellationToken);
        var settingsStore = provider.GetRequiredService<IProjectSettingsStore>();

        await using (var scope = provider.CreateAsyncScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<UpdateSettingsHandler>();
            var current = await settingsStore.FindAsync(projectId, cancellationToken);
            current.ShouldNotBeNull();

            _ = await handler.HandleAsync(
                new UpdateSettingsCommand(projectId, current.Version, 1, 6, null, false, false, false, false, null, null),
                cancellationToken);
        }

        await using var staleScope = provider.CreateAsyncScope();
        var staleHandler = staleScope.ServiceProvider.GetRequiredService<UpdateSettingsHandler>();

        var stale = await Should.ThrowAsync<ProjectSettingsConflictException>(
            () => staleHandler.HandleAsync(
                new UpdateSettingsCommand(projectId, Version: 1, MinIdle: 0, MaxConcurrent: 4,
                    IdleTtlSeconds: null, ApproveRequired: false, KnowledgeEnabled: false,
                    VerifyEnabled: false, ProxyEnabled: false, SoftBudgetUsdMicros: null, HardBudgetUsdMicros: null),
                cancellationToken));

        stale.CurrentVersion.ShouldBe(2);
    }

    [Fact(DisplayName = "Given an archived project, when listed without the flag, then it is hidden but still readable by id")]
    public async Task HideArchivedProjectFromDefaultListAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var projectId = await CreateProjectAsync("to-archive", cancellationToken);

        await using (var scope = provider.CreateAsyncScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<ArchiveProjectHandler>();
            var archived = await handler.HandleAsync(new ArchiveProjectCommand(projectId), cancellationToken);
            archived.Archived.ShouldBeTrue();
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var list = scope.ServiceProvider.GetRequiredService<ListProjectsHandler>();
            var visible = await list.HandleAsync(includeArchived: false, cancellationToken);
            visible.ShouldNotContain(static view => view.Slug == "to-archive");

            var all = await list.HandleAsync(includeArchived: true, cancellationToken);
            all.ShouldContain(static view => view.Slug == "to-archive");
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var get = scope.ServiceProvider.GetRequiredService<GetProjectHandler>();
            var view = await get.HandleAsync(projectId, cancellationToken);
            view.Archived.ShouldBeTrue();
        }
    }

    [Fact(DisplayName = "Given a project, when updated through the handler, then the partial patch lands and persists")]
    public async Task ApplyPartialProjectUpdateAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var projectId = await CreateProjectAsync("patched", cancellationToken);

        await using (var scope = provider.CreateAsyncScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<UpdateProjectHandler>();
            var view = await handler.HandleAsync(
                new UpdateProjectCommand(projectId, Name: "Patched Name", Description: null,
                    ProfilesGitUrl: "https://git.example.com/acme/profiles.git", ProfilesGitRef: null),
                cancellationToken);

            view.Name.ShouldBe("Patched Name");
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var get = scope.ServiceProvider.GetRequiredService<GetProjectHandler>();
            var view = await get.HandleAsync(projectId, cancellationToken);

            view.Name.ShouldBe("Patched Name");
            view.Description.ShouldBeNull();
            view.ProfilesGitUrl.ShouldBe("https://git.example.com/acme/profiles.git");
        }
    }

    [Fact(DisplayName = "Given migrated project_settings, when columns are inspected, then ids are uuid, ttl is nullable and flags are booleans")]
    public async Task StoreExpectedColumnTypesAsync()
    {
        var columns = await QueryColumnsAsync(ProjectsTables.ProjectSettings);

        columns["project_id"].ShouldBe(new ColumnSpec("uuid", "NO"));
        columns["min_idle"].ShouldBe(new ColumnSpec("integer", "NO"));
        columns["max_concurrent"].ShouldBe(new ColumnSpec("integer", "NO"));
        columns["idle_ttl_seconds"].ShouldBe(new ColumnSpec("integer", "YES"));
        columns["approve_required"].ShouldBe(new ColumnSpec("boolean", "NO"));
        columns["knowledge_enabled"].ShouldBe(new ColumnSpec("boolean", "NO"));
        columns["verify_enabled"].ShouldBe(new ColumnSpec("boolean", "NO"));
        columns["proxy_enabled"].ShouldBe(new ColumnSpec("boolean", "NO"));
        columns["soft_budget_usd_micros"].ShouldBe(new ColumnSpec("bigint", "YES"));
        columns["hard_budget_usd_micros"].ShouldBe(new ColumnSpec("bigint", "YES"));
        columns["version"].ShouldBe(new ColumnSpec("integer", "NO"));

        var projectColumns = await QueryColumnsAsync(ProjectsTables.Projects);
        projectColumns["id"].ShouldBe(new ColumnSpec("uuid", "NO"));
        projectColumns["slug"].ShouldBe(new ColumnSpec("character varying", "NO"));
        projectColumns["description"].ShouldBe(new ColumnSpec("character varying", "YES"));
        projectColumns["profiles_git_url"].ShouldBe(new ColumnSpec("character varying", "YES"));
    }

    private async Task<ProjectId> CreateProjectAsync(string slug, CancellationToken cancellationToken)
    {
        await using var scope = provider.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<CreateProjectHandler>();
        var view = await handler.HandleAsync(new CreateProjectCommand(slug, slug, null, null, null), cancellationToken);

        return view.Id;
    }

    private async Task<List<string>> QuerySingleColumnAsync(string sql)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ProjectsDbContext>();
        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = db.Database.GetDbConnection();
        var rows = new List<string>();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }

    private async Task<Dictionary<string, ColumnSpec>> QueryColumnsAsync(string tableName)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ProjectsDbContext>();
        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = db.Database.GetDbConnection();
        var columns = new Dictionary<string, ColumnSpec>(StringComparer.Ordinal);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT column_name, data_type, is_nullable FROM information_schema.columns "
            + "WHERE table_schema = 'public' AND table_name = @tableName";
        var tableNameParameter = command.CreateParameter();
        tableNameParameter.ParameterName = "@tableName";
        tableNameParameter.Value = tableName;
        _ = command.Parameters.Add(tableNameParameter);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            columns[reader.GetString(0)] = new ColumnSpec(reader.GetString(1), reader.GetString(2));
        }

        return columns;
    }

    private sealed record ColumnSpec(string DataType, string IsNullable);
}

using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Domain.Journal;
using Comuki.Engine.Orchestration.Domain.Runs;
using Comuki.Engine.Orchestration.Infrastructure;
using Comuki.Engine.Orchestration.Infrastructure.EscalationTimeout;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Modules.Identity.Infrastructure.Persistence;
using Comuki.Modules.Projects.Infrastructure.Persistence;
using Comuki.Shared.Kernel.Ids;
using Comuki.Shared.Kernel.Scoping;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shouldly;
using Testcontainers.PostgreSql;
using Xunit;

namespace Comuki.Host.Integration.Runs;

/// <summary>
/// Boots the host composition on a random loopback port against a
/// migrated Testcontainers Postgres and exercises the passive autonomy
/// ratchet on the <see cref="RunStatus.Escalated"/> state: the
/// <see cref="EscalationTimeoutSweeper"/> should archive stale escalated
/// runs to <see cref="RunStatus.Cancelled"/> and journal one
/// <see cref="RunEventTypes.RunEscalationTimeout"/> event per row.
/// Fresh Escalated runs and non-Escalated runs are left alone.
/// </summary>
public sealed class EscalationTimeoutSweeperShould : IAsyncLifetime
{
    private const string BootstrapEmail = "bootstrap@comuki.test";
    private const string BootstrapPassword = "bootstrap-pass-1";
    private static readonly TimeSpan shortTimeout = TimeSpan.FromMinutes(5);

    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    /// <summary>boundary: initialised in InitializeAsync before any test runs</summary>
    private WebApplication application = null!;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await container.StartAsync(cancellationToken);

        var connectionString = container.GetConnectionString();

        var orchestrationOptions = new DbContextOptionsBuilder<OrchestrationDbContext>();
        OrchestrationDbContext.ApplyOptions(orchestrationOptions, connectionString);
        await using (var orchestrationDb = new OrchestrationDbContext(orchestrationOptions.Options))
        {
            await orchestrationDb.Database.MigrateAsync(cancellationToken);
        }

        var identityOptions = new DbContextOptionsBuilder<IdentityDbContext>();
        IdentityDbContext.ApplyOptions(identityOptions, connectionString);
        await using (var identityDb = new IdentityDbContext(identityOptions.Options))
        {
            await identityDb.Database.MigrateAsync(cancellationToken);
        }

        var projectsOptions = new DbContextOptionsBuilder<ProjectsDbContext>();
        ProjectsDbContext.ApplyOptions(projectsOptions, connectionString);
        await using (var projectsDb = new ProjectsDbContext(projectsOptions.Options))
        {
            await projectsDb.Database.MigrateAsync(cancellationToken);
        }

        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions
            {
                ApplicationName = typeof(HostComposer).Assembly.GetName().Name,
                EnvironmentName = Environments.Development, // test fixture — validator short-circuits on non-Production
            });
        builder.Host.UseDefaultServiceProvider(static options => { options.ValidateOnBuild = false; options.ValidateScopes = false; });
        builder.WebHost.UseUrls($"http://127.0.0.1:{FreeTcpPort()}");
        builder.Logging.ClearProviders();
        builder.Configuration["ControlPlane:Root"] = Path.GetTempPath();
        builder.Configuration["auth:bootstrap:adminEmail"] = BootstrapEmail;
        builder.Configuration["auth:bootstrap:adminPassword"] = BootstrapPassword;
        builder.Configuration["Artifacts:Endpoint"] = "minio:9000";
        builder.Configuration["Artifacts:AccessKey"] = "test-access-key";
        builder.Configuration["Artifacts:SecretKey"] = "test-secret-key-with-enough-entropy";
        builder.Configuration["Artifacts:Bucket"] = "comuki-test-bundles";
        builder.Configuration["Orchestration:EscalationTimeout:EscalationTimeout"] =
            shortTimeout.ToString();
        builder.Configuration["Orchestration:EscalationTimeout:SweepInterval"] = "00:00:30";
        builder.Services
            .AddOrchestrationPersistence(connectionString)
            .AddOrchestrationQueue(builder.Configuration);

        application = HostComposer.Compose(builder, HostDatabase.Explicit(connectionString), validateOnBuild: false);
        await application.StartAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await application.DisposeAsync();
        await container.DisposeAsync();
    }

    [Fact(DisplayName = "Given an escalated run older than the timeout, when the sweeper runs, then it transitions to cancelled and journals one run.escalation_timeout event")]
    public async Task ArchiveStaleEscalatedRunAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var staleId = await SeedStaleEscalatedAsync(cancellationToken);

        var archived = await RunSweepAsync(cancellationToken);
        archived.ShouldBe(1);

        var (status, journal) = await ReadRunStateAsync(staleId, cancellationToken);
        status.ShouldBe(RunStatus.Cancelled);
        journal.Count.ShouldBe(1);
        journal[0].Type.ShouldBe(RunEventTypes.RunEscalationTimeout);
        using var document = JsonDocument.Parse(journal[0].Payload);
        var root = document.RootElement;
        root.GetProperty("from").GetString().ShouldBe(nameof(RunStatus.Escalated));
        root.GetProperty("to").GetString().ShouldBe(nameof(RunStatus.Cancelled));
        root.GetProperty("ageSeconds").GetDouble().ShouldBeGreaterThan(0);
    }

    [Fact(DisplayName = "Given an escalated run younger than the timeout, when the sweeper runs, then it is left in escalation with no journal entry")]
    public async Task LeaveFreshEscalatedRunAloneAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var freshId = await SeedFreshEscalatedAsync(cancellationToken);

        var archived = await RunSweepAsync(cancellationToken);
        archived.ShouldBe(0);

        var (status, journal) = await ReadRunStateAsync(freshId, cancellationToken);
        status.ShouldBe(RunStatus.Escalated);
        journal.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Given a running run, when the sweeper runs, then it is never touched")]
    public async Task IgnoreRunningRunAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var runningId = await SeedRunningRunAsync(cancellationToken);

        var archived = await RunSweepAsync(cancellationToken);
        archived.ShouldBe(0);

        var (status, journal) = await ReadRunStateAsync(runningId, cancellationToken);
        status.ShouldBe(RunStatus.Running);
        journal.ShouldBeEmpty();
    }

    private async Task<RunId> SeedStaleEscalatedAsync(CancellationToken cancellationToken)
    {
        return await SeedEscalatedAsync(age: shortTimeout + TimeSpan.FromHours(2), cancellationToken);
    }

    private async Task<RunId> SeedFreshEscalatedAsync(CancellationToken cancellationToken)
    {
        return await SeedEscalatedAsync(age: TimeSpan.FromMinutes(1), cancellationToken);
    }

    private async Task<RunId> SeedEscalatedAsync(TimeSpan age, CancellationToken cancellationToken)
    {
        var optionsBuilder = new DbContextOptionsBuilder<OrchestrationDbContext>();
        OrchestrationDbContext.ApplyOptions(optionsBuilder, container.GetConnectionString());
        await using var db = new OrchestrationDbContext(optionsBuilder.Options);

        var now = DateTimeOffset.UtcNow;
        var run = Run.Create(ProjectId.New(), now - age);
        run.TransitionTo(RunStatus.Running, now - age + TimeSpan.FromMinutes(1));
        run.TransitionTo(RunStatus.Escalated, now - age + TimeSpan.FromMinutes(5));
        db.Runs.Add(run);

        await db.SaveChangesAsync(cancellationToken);
        return run.Id;
    }

    private async Task<RunId> SeedRunningRunAsync(CancellationToken cancellationToken)
    {
        var optionsBuilder = new DbContextOptionsBuilder<OrchestrationDbContext>();
        OrchestrationDbContext.ApplyOptions(optionsBuilder, container.GetConnectionString());
        await using var db = new OrchestrationDbContext(optionsBuilder.Options);

        var now = DateTimeOffset.UtcNow;
        var age = TimeSpan.FromHours(1);
        var run = Run.Create(ProjectId.New(), now - age);
        run.TransitionTo(RunStatus.Running, now - age + TimeSpan.FromMinutes(1));
        db.Runs.Add(run);

        await db.SaveChangesAsync(cancellationToken);
        return run.Id;
    }

    private async Task<int> RunSweepAsync(CancellationToken cancellationToken)
    {
        await using var scope = application.Services.CreateAsyncScope();
        using var systemScope = application.Services
            .GetRequiredService<ISubjectScopeAccessor>()
            .AsSystem("escalation-timeout-sweeper");
        var sweeper = scope.ServiceProvider.GetRequiredService<EscalationTimeoutSweeper>();
        var swept = await sweeper.SweepAsync(cancellationToken);
        return swept.Archived;
    }

    private async Task<(RunStatus Status, IReadOnlyList<RunEvent> Journal)> ReadRunStateAsync(
        RunId runId,
        CancellationToken cancellationToken)
    {
        var optionsBuilder = new DbContextOptionsBuilder<OrchestrationDbContext>();
        OrchestrationDbContext.ApplyOptions(optionsBuilder, container.GetConnectionString());
        await using var db = new OrchestrationDbContext(optionsBuilder.Options);

        var run = await db.Runs.AsNoTracking().FirstAsync(r => r.Id == runId, cancellationToken);
        var events = await db.RunEvents.AsNoTracking()
            .Where(e => e.RunId == runId)
            .OrderBy(e => e.OccurredAt)
            .ToListAsync(cancellationToken);
        return (run.Status, events);
    }

    [Fact(DisplayName = "Given many stale escalated runs that have been mutated mid-flight, when the sweeper runs, then only rows that are still Escalated at the moment of the UPDATE get archived (guarded WHERE filter)")]
    public async Task SweepIsGuardedByStatusInWhereAsync()
    {
        // Performance audit (2026-09-09) §1.2 fix: the new sweeper is a
        // single `UPDATE orchestration.runs SET status='Cancelled' WHERE
        // status='Escalated' AND updated_at < @cutoff RETURNING ...` —
        // the status guard is baked into the WHERE clause, so a row that
        // a concurrent operator re-queues between the SELECT and the
        // UPDATE (which the old code needed a per-row FirstOrDefaultAsync
        // to detect) is now safe by construction. The behavioural
        // assertion: seed one stale Escalated run, manually re-queue it
        // to Running, run the sweeper, and confirm it was NOT archived.
        var cancellationToken = TestContext.Current.CancellationToken;
        var staleId = await SeedEscalatedAsync(shortTimeout + TimeSpan.FromHours(2), cancellationToken);

        var optionsBuilder = new DbContextOptionsBuilder<OrchestrationDbContext>();
        OrchestrationDbContext.ApplyOptions(optionsBuilder, container.GetConnectionString());
        await using (var flipDb = new OrchestrationDbContext(optionsBuilder.Options))
        {
            var run = await flipDb.Runs.FirstAsync(r => r.Id == staleId, cancellationToken);
            run.TransitionTo(RunStatus.Running, DateTimeOffset.UtcNow);
            await flipDb.SaveChangesAsync(cancellationToken);
        }

        var archived = await RunSweepAsync(cancellationToken);
        archived.ShouldBe(0);

        var (status, journal) = await ReadRunStateAsync(staleId, cancellationToken);
        status.ShouldBe(RunStatus.Running);
        journal.ShouldBeEmpty();
    }

    private static int FreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        return port;
    }
}

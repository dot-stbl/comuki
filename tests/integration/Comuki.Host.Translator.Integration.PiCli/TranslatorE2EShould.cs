using System.Reflection;
using Comuki.Engine.Compute.Security;
using Comuki.Engine.Orchestration.Application;
using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Domain.Runs;
using Comuki.Engine.Orchestration.Domain.WorkItems;
using Comuki.Engine.Orchestration.Infrastructure;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Host.Translator.Api.Registration;
using Comuki.Host.Translator.Execution.Loop;
using Comuki.Host.Translator.Grpc;
using Comuki.Host.Translator.Profiles;
using Comuki.Host.Translator.Runtime;
using Comuki.Host.Workers;
using Comuki.Shared.Contracts.Journal;
using Comuki.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Testcontainers.PostgreSql;
using Xunit;

namespace Comuki.Host.Translator.Integration.PiCli;

/// <summary>
/// The crown test (T3.5): one work item through the whole runtime — the
/// real EF queue on Testcontainers Postgres, the real worker REST + gRPC
/// host in-process, and the real translator loop spawning
/// <c>TestFakePi</c>. Proves: claim → gRPC stream → fake pi streams →
/// journal gets stage events → StageReport lands → item completes → lease
/// released. The failure twin proves a non-zero pi exit fails the item and
/// still releases the lease.
/// </summary>
public sealed class TranslatorE2EShould : IAsyncLifetime
{
    private const string Image = "ghcr.io/comuki/worker:s3";
    private const string ProfilesRef = "refs/heads/main";
    private const string ProfileKey = "implement";

    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:16-alpine").Build();

    private TestWorkerHost host = null!;

    private ServiceProvider translatorProvider = null!;

    private string workerToken = null!;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        await container.StartAsync(TestContext.Current.CancellationToken);
        await MigrateAsync(container.GetConnectionString());

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Orchestration:Lease:LeaseTtl"] = "00:02:00",
                ["Orchestration:Lease:ReapGrace"] = "00:00:30",
                ["Orchestration:Lease:MaxAttempts"] = "2",
                ["Orchestration:Lease:ReapInterval"] = "01:00:00",
            })
            .Build();

        // Migrations MUST land before the host starts: the lease reaper
        // BackgroundService sweeps on boot and kills the host when the
        // tables are missing (BackgroundServiceExceptionBehavior.StopHost).
        host = await TestWorkerHost.StartAsync(services =>
        {
            services.AddSingleton(TimeProvider.System);
            services
                .AddOrchestrationPersistence(container.GetConnectionString())
                .AddOrchestrationQueue(configuration)
                .AddOrchestrationApplication()
                .AddWorkerRuntime(configuration);
        });

        workerToken = host.GetService<WorkerTokenIssuer>().Issue(WorkerId.New());
        translatorProvider = BuildTranslatorProvider(ResolveTestFakePiPath());
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await translatorProvider.DisposeAsync();
        await host.DisposeAsync();
        await container.DisposeAsync();
    }

    [Fact]
    public async Task RunOneWorkItemThroughFakePiEndToEndAsync()
    {
        var (runId, workItemId) = await SeedQueuedItemAsync(/*lang=json,strict*/ """{"goal":"do the thing"}""");

        var loop = translatorProvider.GetRequiredService<TranslatorLoop>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ran = await loop.TryRunOnceAsync(timeout.Token);

        ran.ShouldBeTrue("the seeded item should have been claimed");

        var item = await LoadItemAsync(workItemId);
        item.Status.ShouldBe(WorkItemStatus.Succeeded);
        item.LeasedBy.ShouldBeNull();
        item.LeaseUntil.ShouldBeNull("completion releases the lease");
        item.HeartbeatAt.ShouldBeNull();

        var timeline = await ReadTimelineAsync(runId);
        timeline.ShouldContain(static entry => entry.Type == "worker.reported" && entry.PayloadJson.Contains("\"brief\"", StringComparison.Ordinal), "StageStart is journaled");
        timeline.ShouldContain(static entry => entry.Type == "worker.reported" && entry.PayloadJson.Contains("Implementing the thing", StringComparison.Ordinal), "text activity is journaled");
        timeline.ShouldContain(static entry => entry.Type == "worker.reported" && entry.PayloadJson.Contains("Bash", StringComparison.Ordinal), "tool activity is journaled");
        timeline.ShouldContain(static entry => entry.Type == "worker.reported" && entry.PayloadJson.Contains("(fake pi done)", StringComparison.Ordinal), "StageReport with the authoritative result is journaled");
        timeline.ShouldContain(static entry => entry.Type == "work_item.status_changed" && entry.PayloadJson.Contains("Succeeded", StringComparison.Ordinal), "completion is journaled");
    }

    [Fact]
    public async Task FailTheItemWhenPiExitsNonZeroAsync()
    {
        var (runId, workItemId) = await SeedQueuedItemAsync(/*lang=json,strict*/ """{"goal":"fail fast"}""");
        await using var failingProvider = BuildTranslatorProvider("dotnet");

        var loop = failingProvider.GetRequiredService<TranslatorLoop>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ran = await loop.TryRunOnceAsync(timeout.Token);

        ran.ShouldBeTrue("the seeded item should have been claimed");

        var item = await LoadItemAsync(workItemId);
        item.Status.ShouldBe(WorkItemStatus.Failed);
        item.LeaseUntil.ShouldBeNull("failure releases the lease too");

        var timeline = await ReadTimelineAsync(runId);
        timeline.ShouldContain(static entry => entry.Type == "worker.reported" && entry.PayloadJson.Contains("failed", StringComparison.Ordinal), "the failure StageReport is journaled");
        timeline.ShouldContain(static entry => entry.Type == "work_item.status_changed" && entry.PayloadJson.Contains("Failed", StringComparison.Ordinal), "the failure is journaled");
    }

    private ServiceProvider BuildTranslatorProvider(string piExecutable)
    {
        var options = new TranslatorOptions
        {
            OrchestratorBaseUrl = host.BaseAddress,
            OrchestratorGrpcUrl = host.GrpcAddress,
            WorkerToken = workerToken,
            ProfileKey = ProfileKey,
            ProfilesRef = ProfilesRef,
            WorkerImage = Image,
            PiExecutable = piExecutable,
            WorkingDirectory = Path.Combine(Path.GetTempPath(), "comuki-e2e-" + Guid.NewGuid().ToString("N")),
            HeartbeatInterval = TimeSpan.FromSeconds(5),
            ClaimPollInterval = TimeSpan.FromSeconds(1),
        };
        Directory.CreateDirectory(options.WorkingDirectory);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Options.Create(options));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IPiRunner, PiRunner>();
        services.AddSingleton<IProfilesProvider, ProfilesProvider>();
        services.AddSingleton<HeartbeatMonitor>();
        services.AddSingleton<TranslatorLoop>();
        services.AddOrchestratorApi();
        services.AddWorkerGrpcClient();

        return services.BuildServiceProvider();
    }

    private static async Task MigrateAsync(string connectionString)
    {
        // A standalone provider (not the test host): hosted services never
        // run in a bare BuildServiceProvider, so the reaper cannot race the
        // migrations it depends on.
        var services = new ServiceCollection();
        services.AddOrchestrationPersistence(connectionString);
        await using var provider = services.BuildServiceProvider();
        var db = provider.GetRequiredService<OrchestrationDbContext>();
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
    }

    private async Task<(Guid RunId, Guid WorkItemId)> SeedQueuedItemAsync(string brief)
    {
        // The fixture is a system consumer: it seeds and verifies rows the
        // subject-scope filters would otherwise hide (or reject, with no
        // scope established on the test flow at all).
        using var scope = host.CreateScope();
        using var systemScope = scope.ServiceProvider
            .GetRequiredService<Shared.Kernel.Scoping.ISubjectScopeAccessor>()
            .AsSystem("translator-e2e-fixture");
        var db = scope.ServiceProvider.GetRequiredService<OrchestrationDbContext>();
        var run = Run.Create(ProjectId.New(), DateTimeOffset.UtcNow);
        var item = WorkItem.Create(run.Id, ProfileKey, Image, ProfilesRef, brief, WorkItemStatus.Queued, DateTimeOffset.UtcNow);
        db.Runs.Add(run);
        db.WorkItems.Add(item);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return (run.Id.Value, item.Id);
    }

    private async Task<WorkItem> LoadItemAsync(Guid workItemId)
    {
        using var scope = host.CreateScope();
        using var systemScope = scope.ServiceProvider
            .GetRequiredService<Shared.Kernel.Scoping.ISubjectScopeAccessor>()
            .AsSystem("translator-e2e-fixture");
        var db = scope.ServiceProvider.GetRequiredService<OrchestrationDbContext>();
        return await db.WorkItems.AsNoTracking().SingleAsync(item => item.Id == workItemId, TestContext.Current.CancellationToken);
    }

    private async Task<IReadOnlyList<RunEventEntry>> ReadTimelineAsync(Guid runId)
    {
        using var scope = host.CreateScope();
        using var systemScope = scope.ServiceProvider
            .GetRequiredService<Shared.Kernel.Scoping.ISubjectScopeAccessor>()
            .AsSystem("translator-e2e-fixture");
        var journal = scope.ServiceProvider.GetRequiredService<IRunJournal>();
        return await journal.ReadTimelineAsync(new RunId(runId), page: 1, pageSize: 100, TestContext.Current.CancellationToken);
    }

    private static string ResolveTestFakePiPath()
    {
        var assembly = Assembly.Load("Comuki.TestFakePi");
        var directory = Path.GetDirectoryName(assembly.Location)
            ?? throw new InvalidOperationException("could not resolve TestFakePi directory");
        var executableName = OperatingSystem.IsWindows() ? "Comuki.TestFakePi.exe" : "Comuki.TestFakePi";
        return Path.Combine(directory, executableName);
    }
}

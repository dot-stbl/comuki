using System.Reflection;
using System.Text.Json;
using Comuki.Engine.Compute.Security;
using Comuki.Engine.Orchestration.Application;
using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Domain.Runs;
using Comuki.Engine.Orchestration.Domain.WorkItems;
using Comuki.Engine.Orchestration.Infrastructure;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Host.Testing.Fixtures;
using Comuki.Host.Translator.Api.Registration;
using Comuki.Host.Translator.Execution.Loop;
using Comuki.Host.Translator.Grpc;
using Comuki.Host.Translator.Profiles;
using Comuki.Host.Translator.Runtime;
using Comuki.Host.Workers;
using Comuki.Modules.Proxy.Application;
using Comuki.Shared.Bootstrap;
using Comuki.Shared.Contracts.Journal;
using Comuki.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace Comuki.Host.Translator.Integration.PiCli;

/// <summary>
/// The crown test (T3.5): one work item through the whole runtime — the
/// real EF queue on the collection's shared, migrated Postgres
/// (<see cref="PostgresCollectionFixture"/>, reset to empty before every
/// test), the real worker REST + gRPC host in-process, and the real
/// translator loop spawning <c>TestFakePi</c>. Proves: claim → gRPC stream
/// → fake pi streams → journal gets stage events → StageReport lands →
/// item completes → lease released. The failure twin proves a non-zero pi
/// exit fails the item and still releases the lease.
/// </summary>
/// <param name="postgres">The collection's shared Postgres (<see cref="PiCliIntegrationCollection"/>) — reset to empty for every test, migrated once for the whole run.</param>
[Collection(nameof(PiCliIntegrationCollection))]
public sealed class TranslatorE2EShould(PostgresCollectionFixture postgres) : IAsyncLifetime
{
    private const string Image = "ghcr.io/comuki/worker:s3";
    private const string ProfilesRef = "refs/heads/main";
    private const string ProfileKey = "implement";

    private TestWorkerHost host = null!;

    private ServiceProvider translatorProvider = null!;

    private string workerToken = null!;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        // The orchestration schema is already migrated once by
        // PostgresCollectionFixture (HostDatabaseMigrator.MigrateAllAsync
        // covers it) — this class used to spin its own container and
        // hand-migrate via MigrateAsync below, both now superseded. Reset
        // gives every test the same empty-tables starting point the old
        // per-test container gave it.
        await postgres.ResetDatabaseAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Orchestration:Lease:LeaseTtl"] = "00:02:00",
                ["Orchestration:Lease:ReapGrace"] = "00:00:30",
                ["Orchestration:Lease:MaxAttempts"] = "2",
                ["Orchestration:Lease:ReapInterval"] = "01:00:00",

                // Proxy minting (issue #122): enabled with a dummy worker
                // base URL — no proxy listener exists in this fixture, the
                // tests only assert the claim response and the env stamp.
                ["Proxy:Enabled"] = "true",
                ["Proxy:WorkerBaseUrl"] = "http://127.0.0.1:9/",
                ["Proxy:VirtualKeys:0:Token"] = "vkey_e2e_anthropic",
                ["Proxy:VirtualKeys:0:ProjectId"] = Guid.NewGuid().ToString(),
                ["Proxy:VirtualKeys:0:Provider"] = "anthropic",
                ["Proxy:VirtualKeys:0:BaseUrl"] = "http://127.0.0.1:9/upstream",
                ["Proxy:VirtualKeys:0:ApiKeyEnvRef"] = "FAKE_PI_E2E_UPSTREAM_KEY",
            })
            .Build();
        Environment.SetEnvironmentVariable("FAKE_PI_E2E_UPSTREAM_KEY", "sk-not-called-in-this-fixture");

        // Migrations MUST land before the host starts: the lease reaper
        // sweeps on boot and its first cycle would otherwise fail against
        // missing tables (the registry backs off and retries — the host
        // stays up either way).
        host = await TestWorkerHost.StartAsync(services =>
        {
            services.AddSingleton(TimeProvider.System);
            services
                .AddOrchestrationPersistence(postgres.ConnectionString)
                .AddOrchestrationQueue(configuration)
                .AddOrchestrationApplication()
                .AddWorkerRuntime(configuration);
            // The claim endpoint mints into IVirtualKeyStore — the proxy
            // application services provide the store and ProxyOptions.
            services.AddSingleton<Shared.Kernel.Secrets.ISecretResolver>(
                new Shared.Kernel.Secrets.CompositeSecretResolver(
                    [new Shared.Kernel.Secrets.EnvSecretProvider()]));
            services.AddProxyApplication(configuration);
            // The lease reaper registers as an IComukiWorker — this
            // registry is what runs it in this fixture.
            services.AddComukiWorkers();
        });

        workerToken = host.GetService<WorkerTokenIssuer>().Issue(WorkerId.New());
        translatorProvider = BuildTranslatorProvider(ResolveTestFakePiPath());
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await translatorProvider.DisposeAsync();
        await host.DisposeAsync();
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

    [Fact]
    public async Task MintOnClaimRevokeOnCompleteAndKeepTheJournalCleanAsync()
    {
        var (runId, workItemId) = await SeedQueuedItemAsync(/*lang=json,strict*/ """{"goal":"claim only"}""");
        var store = host.GetService<Modules.Proxy.Application.Ports.IVirtualKeyStore>();

        using var client = new HttpClient { BaseAddress = host.BaseAddress };
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", workerToken);

        // Claim over the real REST surface: the minted key rides on the
        // response exactly once.
        using var claim = await client.PostAsync(
            "/workers/claim",
            new StringContent(
                /*lang=json,strict*/ $$"""{"image":"{{Image}}","profilesRef":"{{ProfilesRef}}","profileKey":"{{ProfileKey}}"}""",
                System.Text.Encoding.UTF8,
                "application/json"),
            TestContext.Current.CancellationToken);
        claim.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);

        using var claimDocument = JsonDocument.Parse(await claim.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var proxyBaseUrl = claimDocument.RootElement.GetProperty("proxyBaseUrl").GetString();
        var virtualKey = claimDocument.RootElement.GetProperty("virtualKey").GetString();
        proxyBaseUrl.ShouldBe("http://127.0.0.1:9", "the configured WorkerBaseUrl, trailing slash trimmed");
        virtualKey.ShouldNotBeNullOrWhiteSpace();

        (await store.FindAsync(virtualKey, TestContext.Current.CancellationToken))
            .ShouldNotBeNull("the minted key resolves beside the config-seeded keys");

        // The journal path that mirrors the claim transition must never
        // carry the raw token.
        var claimedTimeline = await ReadTimelineAsync(runId);
        claimedTimeline.ShouldNotContain(entry => entry.PayloadJson.Contains(virtualKey!, StringComparison.Ordinal), "the raw minted token is never journaled");

        using var complete = await client.PostAsync(
            $"/workers/{workItemId}/complete",
            new StringContent(/*lang=json,strict*/ """{"resultJson":"{\"ok\":true}"}""", System.Text.Encoding.UTF8, "application/json"),
            TestContext.Current.CancellationToken);
        complete.StatusCode.ShouldBe(System.Net.HttpStatusCode.NoContent);

        (await store.FindAsync(virtualKey, TestContext.Current.CancellationToken))
            .ShouldBeNull("complete revokes the minted key immediately");
    }

    [Fact]
    public async Task StampTheMintedVirtualKeyIntoTheFakePiEnvironmentAsync()
    {
        var (_, workItemId) = await SeedQueuedItemAsync(/*lang=json,strict*/ """{"goal":"stamp the env"}""");

        var loop = translatorProvider.GetRequiredService<TranslatorLoop>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ran = await loop.TryRunOnceAsync(timeout.Token);

        ran.ShouldBeTrue("the seeded item should have been claimed");

        var item = await LoadItemAsync(workItemId);
        item.Status.ShouldBe(WorkItemStatus.Succeeded, "the run itself is unaffected by the stamp");

        // TestFakePi dumps the model-gateway env it received into the
        // run's working directory when the token stamp is present.
        var workingDirectory = translatorProvider.GetRequiredService<IOptions<TranslatorOptions>>().Value.WorkingDirectory;
        var dump = await File.ReadAllTextAsync(
            Path.Combine(workingDirectory, "fake-pi-env.json"),
            TestContext.Current.CancellationToken);
        dump.ShouldContain("http://127.0.0.1:9");
        using var dumpDocument = JsonDocument.Parse(dump);
        dumpDocument.RootElement.GetProperty("anthropicAuthToken").GetString().ShouldNotBeNullOrWhiteSpace();

        // The mint died with the completed execution.
        var store = host.GetService<Modules.Proxy.Application.Ports.IVirtualKeyStore>();
        var keys = await store.ListAsync(TestContext.Current.CancellationToken);
        keys.ShouldNotContain(key => key.WorkItemId == workItemId, "the completed execution's mint is revoked");
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

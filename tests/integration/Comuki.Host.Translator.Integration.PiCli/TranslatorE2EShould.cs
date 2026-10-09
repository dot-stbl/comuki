using System.Reflection;
using System.Text.Json;
using Comuki.Engine.Compute.Pool;
using Comuki.Engine.Compute.Ports;
using Comuki.Engine.Compute.Security;
using Comuki.Engine.Orchestration.Application;
using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Domain.Runs;
using Comuki.Engine.Orchestration.Domain.WorkItems;
using Comuki.Engine.Orchestration.Infrastructure;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Host.Testing.Fixtures;
using Comuki.Host.Translator.Api.Registration;
using Comuki.Host.Translator.Execution.Clone;
using Comuki.Host.Translator.Execution.Commands;
using Comuki.Host.Translator.Execution.Loop;
using Comuki.Host.Translator.Execution.Restore;
using Comuki.Host.Translator.Grpc;
using Comuki.Host.Translator.Profiles;
using Comuki.Host.Translator.Runtime;
using Comuki.Host.Workers;
using Comuki.Modules.Projects.Application.Ports;
using Comuki.Modules.Projects.Domain.Projects;
using Comuki.Modules.Projects.Domain.Settings;
using Comuki.Modules.Proxy.Application;
using Comuki.Shared.Bootstrap;
using Comuki.Shared.Contracts.Journal;
using Comuki.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
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
    private const string EnvClass = "net10-sdk-bun";
    private const string ProfileKey = "implement";

    private TestWorkerHost host = null!;

    private ServiceProvider translatorProvider = null!;

    private string workerToken = null!;

    private IConfiguration configuration = null!;

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

        configuration = new ConfigurationBuilder()
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
            var projectStore = NewProjectStoreStub();
            var projectSettingsStore = NewProjectSettingsStoreStub();
            services.AddSingleton(typeof(IProjectStore), projectStore);
            services.AddSingleton(typeof(IProjectSettingsStore), projectSettingsStore);
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
            // WorkerEndpoints.ClaimAsync/HeartbeatAsync/CompleteAsync/
            // FailAsync bind IWorkerPoolState for busy/idle bookkeeping
            // (AddComukiCompute registers the real thing on the full host,
            // backed by a Docker/Kubernetes IComputeProvider). This fixture
            // never exercises the scale supervisor, so a no-op stub is
            // enough to satisfy DI without pulling in a container runtime
            // client.
            services.AddSingleton<IWorkerPoolState, NoopWorkerPoolState>();
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
        // The crown test (T3.5) and the fast-completing-worker regression
        // proof for issue #152. Now that TestWorkerHost.StartAsync mirrors
        // production's Http1AndHttp2-REST + dedicated-Http2-gRPC topology,
        // TestFakePi's single-digit-ms run lands the FULL event set
        // (StageStart, text + tool activity, StageReport) via the worker
        // gRPC bidi stream before REST /complete closes the lease — every
        // worker.reported entry proves the stream actually negotiated.
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
    public async Task LoseStreamedEventsWhenTheGrpcListenerIsSharedWithRestAsync()
    {
        var (runId, workItemId) = await SeedQueuedItemAsync(/*lang=json,strict*/ """{"goal":"characterize the shared-listener bug"}""");

        // Characterizes issue #152's exact root cause (see
        // TestWorkerHost.StartWithSharedListenerAsync's remarks): a single
        // shared Http1AndHttp2 listener never negotiates HTTP/2 for the
        // worker gRPC bidi stream, so no worker.reported entry lands at
        // all — only the REST-driven work_item.status_changed does. This
        // guards against ever silently reintroducing a shared listener.
        await using var sharedListenerHost = await TestWorkerHost.StartWithSharedListenerAsync(services =>
        {
            services.AddSingleton(TimeProvider.System);
            services
                .AddOrchestrationPersistence(postgres.ConnectionString)
                .AddOrchestrationQueue(configuration)
                .AddOrchestrationApplication()
                .AddWorkerRuntime(configuration);
            services.AddSingleton<Shared.Kernel.Secrets.ISecretResolver>(
                new Shared.Kernel.Secrets.CompositeSecretResolver(
                    [new Shared.Kernel.Secrets.EnvSecretProvider()]));
            services.AddProxyApplication(configuration);
            services.AddComukiWorkers();
            services.AddSingleton<IWorkerPoolState, NoopWorkerPoolState>();
        });
        var sharedToken = sharedListenerHost.GetService<WorkerTokenIssuer>().Issue(WorkerId.New());

        await using var provider = BuildTranslatorProvider(
            ResolveTestFakePiPath(), sharedListenerHost.BaseAddress, sharedListenerHost.GrpcAddress, sharedToken);
        var loop = provider.GetRequiredService<TranslatorLoop>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ran = await loop.TryRunOnceAsync(timeout.Token);

        ran.ShouldBeTrue("the seeded item should have been claimed");

        var item = await LoadItemAsync(workItemId);
        item.Status.ShouldBe(WorkItemStatus.Succeeded, "REST /complete still lands over its own connection even though the shared listener drops gRPC");

        var timeline = await ReadTimelineAsync(runId);
        timeline.ShouldContain(
            static entry => entry.Type == "work_item.status_changed" && entry.PayloadJson.Contains("Succeeded", StringComparison.Ordinal));
        timeline.ShouldNotContain(
            static entry => entry.Type == "worker.reported",
            "a shared Http1AndHttp2 listener never negotiates HTTP/2 for the worker gRPC stream — this is issue #152's exact mechanism");
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

    [Fact(DisplayName = "Worker-runtime spec: 'Non-zero pi exit fails the item' — TestFakePi streams a few events then exits with code 3, the loop catches the non-zero exit and fails the item (not success)")]
    public async Task FailTheItemWhenTestFakePiExitsNonZeroMidRunAsync()
    {
        // Set the exit-code env var before the loop runs. PiHarness spawns
        // TestFakePi as a child process; the child inherits this env var,
        // reads it via Comuki.TestFakePi.Program.Main, and exits with the
        // configured code after streaming its event set + agent_end.
        Environment.SetEnvironmentVariable("COMUKI_FAKE_PI_EXIT_CODE", "3");
        try
        {
            var (runId, workItemId) = await SeedQueuedItemAsync(/*lang=json,strict*/ """{"goal":"exit-code-3"}""");

            var loop = translatorProvider.GetRequiredService<TranslatorLoop>();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var ran = await loop.TryRunOnceAsync(timeout.Token);

            ran.ShouldBeTrue("the seeded item should have been claimed");

            var item = await LoadItemAsync(workItemId);
            item.Status.ShouldBe(WorkItemStatus.Failed, "non-zero pi exit must fail the item, not return success");
            item.LeaseUntil.ShouldBeNull("failure releases the lease");

            var timeline = await ReadTimelineAsync(runId);
            // The StageReport must carry the exit code in the ErrorText
            // (worker-runtime spec scenario: "outcome is failed carrying
            // the exit code"), and the failure is journaled.
            timeline.ShouldContain(
                static entry => entry.Type == "worker.reported" && entry.PayloadJson.Contains("failed", StringComparison.Ordinal),
                "the failure StageReport is journaled");
            timeline.ShouldContain(
                static entry => entry.Type == "worker.reported" && entry.PayloadJson.Contains("exit code 3", StringComparison.Ordinal),
                "the StageReport carries the exit code in ErrorText");
            timeline.ShouldContain(
                static entry => entry.Type == "work_item.status_changed" && entry.PayloadJson.Contains("Failed", StringComparison.Ordinal),
                "the failure status change is journaled");
        }
        finally
        {
            Environment.SetEnvironmentVariable("COMUKI_FAKE_PI_EXIT_CODE", null);
        }
    }

    [Fact(DisplayName = "Harden-pi-worker-sandbox 5.2 / spec D7: TranslatorLoop emits a worker.drained event before complete, even when the artifact list is empty")]
    public async Task EmitWorkerDrainedBeforeCompleteAsync()
    {
        var (runId, workItemId) = await SeedQueuedItemAsync(/*lang=json,strict*/ """{"goal":"drain empty"}""");

        var loop = translatorProvider.GetRequiredService<TranslatorLoop>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ran = await loop.TryRunOnceAsync(timeout.Token);

        ran.ShouldBeTrue("the seeded item should have been claimed");

        var item = await LoadItemAsync(workItemId);
        item.Status.ShouldBe(WorkItemStatus.Succeeded);

        var timeline = await ReadTimelineAsync(runId);
        // The drain event must land before the StageReport so the host
        // packager can pre-skip already-bundled prefixes; an empty
        // artifact list is still journaled.
        var drainedIndex = IndexOfEntryType(timeline, "worker.drained");
        var reportIndex = IndexOfEntryType(timeline, "worker.reported");
        drainedIndex.ShouldBeGreaterThanOrEqualTo(0, "the worker.drained event must be journaled");
        reportIndex.ShouldBeGreaterThanOrEqualTo(0, "the worker.reported StageReport must be journaled");
        drainedIndex.ShouldBeLessThan(reportIndex, "the drain event precedes the StageReport (pre-complete flush)");
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
                /*lang=json,strict*/ $$"""{"image":"{{Image}}","profilesRef":"{{ProfilesRef}}","profileKey":"{{ProfileKey}}","envClass":"{{EnvClass}}"}""",
                System.Text.Encoding.UTF8,
                "application/json"),
            TestContext.Current.CancellationToken);
        claim.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);

        using var claimDocument = JsonDocument.Parse(await claim.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var proxyBaseUrl = claimDocument.RootElement.GetProperty("proxyBaseUrl").GetString();
        var virtualKey = claimDocument.RootElement.GetProperty("virtualKey").GetString();
        var generation = claimDocument.RootElement.GetProperty("generation").GetInt32();
        proxyBaseUrl.ShouldBe("http://127.0.0.1:9", "the configured WorkerBaseUrl, trailing slash trimmed");
        virtualKey.ShouldNotBeNullOrWhiteSpace();

        (await store.FindAsync(virtualKey, TestContext.Current.CancellationToken))
            .ShouldNotBeNull("the minted key resolves beside the config-seeded keys");

        // The journal path that mirrors the claim transition must never
        // carry the raw token.
        var claimedTimeline = await ReadTimelineAsync(runId);
        claimedTimeline.ShouldNotContain(entry => entry.PayloadJson.Contains(virtualKey!, StringComparison.Ordinal), "the raw minted token is never journaled");

        // W1 generation fencing: complete echoes the claimed generation —
        // a body without it binds to 0 and the guarded SQL rejects the
        // call as an ownership miss (409 work-item.not-owner).
        using var complete = await client.PostAsync(
            $"/workers/{workItemId}/complete",
            new StringContent(/*lang=json,strict*/ $$"""{"resultJson":"{\"ok\":true}","generation":{{generation}}}""", System.Text.Encoding.UTF8, "application/json"),
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
        return BuildTranslatorProvider(piExecutable, host.BaseAddress, host.GrpcAddress, workerToken);
    }

    private static ServiceProvider BuildTranslatorProvider(string piExecutable, Uri baseAddress, Uri grpcAddress, string token)
    {
        var options = new TranslatorOptions
        {
            OrchestratorBaseUrl = baseAddress,
            OrchestratorGrpcUrl = grpcAddress,
            WorkerToken = token,
            ProfileKey = ProfileKey,
            ProfilesRef = ProfilesRef,
            WorkerImage = Image,
            PiExecutable = piExecutable,
            WorkingDirectory = Path.Combine(Path.GetTempPath(), "comuki-e2e-" + Guid.NewGuid().ToString("N")),
            HeartbeatInterval = TimeSpan.FromSeconds(5),
            ClaimPollInterval = TimeSpan.FromSeconds(1),
            EnvClass = EnvClass,
        };
        Directory.CreateDirectory(options.WorkingDirectory);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Options.Create(options));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IHarnessRuntime, PiHarness>();
        services.AddSingleton<IProfilesProvider, ProfilesProvider>();
        // TranslatorLoop's ctor needs both runners (the claim cycle
        // walks Clone → Restore → harness) and the debug-exec host.
        // The production Program.cs registers all of them; the
        // in-test composition was lagging after the runner-rework
        // and 8.4 — all surface here so the harness-spawn tests
        // resolve the loop.
        services.AddSingleton<IRestoreProcessRunner, RestoreProcessRunner>();
        services.AddSingleton<RestoreRunner>();
        services.AddSingleton<ISourceCloneProcessRunner, SourceCloneProcessRunner>();
        services.AddSingleton<SourceCloneRunner>();
        services.AddSingleton<IDebugExecHost, DebugExecHost>();
        // ClaimSourceGitResolver (harden-pi-worker-sandbox 4.3) walks
        // the project tree to resolve the work item's SourceGitUrl /
        // SourceGitRef / GitCredential. The fixture's seeded items
        // don't carry source-git fields, so stubs are enough — the
        // resolver treats missing data as "anonymous clone", which the
        // production SourceCloneRunner rejects later on the worker
        // side. Stubs keep this fixture independent of the real
        // Projects persistence (which would drag the project schema,
        // the settings cache refresher worker, and AddMemoryCache
        // side-effects in).
        var projectStore = NewProjectStoreStub();
        var projectSettingsStore = NewProjectSettingsStoreStub();
        services.AddSingleton(typeof(IProjectStore), projectStore);
        services.AddSingleton(typeof(IProjectSettingsStore), projectSettingsStore);
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
        var item = WorkItem.Create(run.Id, ProfileKey, Image, EnvClass, ProfilesRef, brief, WorkItemStatus.Queued, DateTimeOffset.UtcNow);
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

    /// <summary>
    /// First index in <paramref name="timeline"/> whose entry's
    /// <c>Type</c> matches <paramref name="type"/>, or -1 when no such
    /// entry exists. The <see cref="IReadOnlyList{T}"/> does not
    /// expose <c>FindIndex</c> directly, so this is the explicit
    /// per-entry loop the drain-then-report ordering assertion needs.
    /// </summary>
    private static int IndexOfEntryType(IReadOnlyList<RunEventEntry> timeline, string type)
    {
        for (var i = 0; i < timeline.Count; i++)
        {
            if (string.Equals(timeline[i].Type, type, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Stands in for the scale supervisor's <see cref="WorkerPoolState"/>:
    /// this fixture asserts the claim/heartbeat/complete/fail REST flow, not
    /// pool bookkeeping, so every call is a no-op rather than wiring a real
    /// <c>IComputeProvider</c> (Docker/Kubernetes) into an E2E test that
    /// never starts or lists containers.
    /// </summary>
    private sealed class NoopWorkerPoolState : IWorkerPoolState
    {
        public IReadOnlyList<PoolWorker> List(ProjectId projectId)
        {
            return [];
        }

        public void MarkBusy(WorkerId workerId)
        {
        }

        public void MarkIdle(WorkerId workerId)
        {
        }

        public void Touch(WorkerId workerId)
        {
        }
    }

    /// <summary>
    /// In-memory <see cref="IProjectStore"/> stub the host's
    /// <c>ClaimSourceGitResolver</c> walks to surface the work item's
    /// source-git metadata. The fixture never sets source-git fields on
    /// its seeded items, but the resolver must still be constructible
    /// when the claim endpoint runs; the stub returns a project with
    /// the canonical <c>net10-sdk-bun</c> env class so the seeded
    /// items remain claimable.
    /// </summary>
    private static IProjectStore NewProjectStoreStub()
    {
        var stub = Substitute.For<IProjectStore>();
        stub.FindByIdAsync(Arg.Any<ProjectId>(), Arg.Any<CancellationToken>())
            .Returns(static callInfo => Project.Create(
                "Test project",
                "test-project",
                null,
                null,
                null,
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                envClass: EnvClass));
        return stub;
    }

    /// <summary>
    /// Companion stub for <see cref="IProjectSettingsStore"/>:
    /// <c>ClaimSourceGitResolver</c> also reads the project's git
    /// credential ref before the claim endpoint can resolve a
    /// work item. The fixture never sets a ref, so the stub
    /// returns <c>null</c> on every query (resolver treats that
    /// as "no credential" — the anonymous clone path the spec
    /// documents for items without source-git metadata).
    /// </summary>
    private static IProjectSettingsStore NewProjectSettingsStoreStub()
    {
        var stub = Substitute.For<IProjectSettingsStore>();
        stub.FindAsync(Arg.Any<ProjectId>(), Arg.Any<CancellationToken>())
            .Returns(static _ => Task.FromResult<ProjectSettings?>(null));
        return stub;
    }
}

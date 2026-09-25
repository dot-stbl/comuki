using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Domain.Journal;
using Comuki.Engine.Orchestration.Domain.Runs;
using Comuki.Engine.Orchestration.Domain.WorkItems;
using Comuki.Engine.Orchestration.Infrastructure;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Host.Testing;
using Comuki.Host.Testing.Fixtures;
using Comuki.Shared.Contracts.Queue;
using Comuki.Shared.Kernel.Ids;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace Comuki.Host.Integration.Runs;

/// <summary>
/// End-to-end coverage for the operator decision endpoints
/// (<c>POST /api/v1/runs/{runId}/approve</c> + <c>/cancel</c>). Boots the
/// real host composition against one shared, migrated Postgres (owned by
/// this collection's <see cref="PostgresCollectionFixture"/>, reset to
/// empty before every test — see <see cref="RunsIntegrationCollection"/>),
/// drives an admin through the wire, and asserts on the orchestration
/// context + journal rows in the same database the request mutated.
/// </summary>
/// <param name="postgres">The collection's shared Postgres — one container for the whole Runs suite, not one per test.</param>
[Collection(nameof(RunsIntegrationCollection))]
public sealed class RunDecisionsEndpointShould(PostgresCollectionFixture postgres) : IAsyncLifetime
{
    private const string BootstrapEmail = "bootstrap@comuki.test";
    private const string BootstrapPassword = "bootstrap-pass-1";

    private WebApplication application = null!;
    private Uri baseAddress = null!;
    private string connectionString = string.Empty;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await postgres.ResetDatabaseAsync();

        connectionString = postgres.ConnectionString;

        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions
            {
                ApplicationName = typeof(HostComposer).Assembly.GetName().Name,
                // Production env on purpose: ValidateScopes off; production-secret
                // validator (issue #10 T11.4) satisfied by non-dev-default secrets.
                EnvironmentName = Environments.Development, // test fixture — validator short-circuits on non-Production
            });
        builder.Host.UseDefaultServiceProvider(static options => { options.ValidateOnBuild = false; options.ValidateScopes = false; });
        var hostPort = FreeTcpPort();
        builder.WebHost.UseUrls($"http://127.0.0.1:{hostPort}");
        builder.Logging.ClearProviders();
        // AuthPublicHostOptions (security audit A03-1/A10-1): ValidateOnStart
        // requires a non-empty auth:publicHost:publicUrl. This fixture
        // predates Comuki.Host.Testing.TestHostBuilder and builds its own
        // WebApplicationBuilder, so it seeds the same key directly — the
        // test loopback address is this host's "public" address here.
        builder.Configuration["auth:publicHost:publicUrl"] = $"http://127.0.0.1:{hostPort}";
        builder.Configuration["ControlPlane:Root"] = Path.GetTempPath();
        builder.Configuration["auth:bootstrap:adminEmail"] = BootstrapEmail;
        builder.Configuration["auth:bootstrap:adminPassword"] = BootstrapPassword;
        builder.Configuration["Host:RateLimit:LoginPermitsPerMinute"] = "10000";
        // Non-dev-default secrets so the production-secret validator
        // (issue #10 T11.4) passes through.
        builder.Configuration["Artifacts:Endpoint"] = "minio:9000";
        builder.Configuration["Artifacts:AccessKey"] = "test-access-key";
        builder.Configuration["Artifacts:SecretKey"] = "test-secret-key-with-enough-entropy";
        builder.Configuration["Artifacts:Bucket"] = "comuki-test-bundles";
        builder.Services
            .AddOrchestrationPersistence(connectionString)
            .AddOrchestrationQueue(builder.Configuration);

        application = await HostComposer.ComposeAsync(builder, HostDatabase.Explicit(connectionString));
        await application.StartAsync(cancellationToken);

        baseAddress = new Uri(
            application.Services
                .GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
                .Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!
                .Addresses.Single());
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await application.DisposeAsync();
    }

    private async Task<HttpClient> CreateAdminClientAsync()
    {
        var client = new HttpClient(new HttpClientHandler { UseCookies = true, CheckCertificateRevocationList = true })
        {
            BaseAddress = baseAddress,
        };

        return await client.LoginAsBootstrapAdminAsync(TestContext.Current.CancellationToken);
    }

    [Fact(DisplayName = "Given a run in Escalated, when POST /approve, then status becomes Running and a run.status_changed event is appended")]
    public async Task ApproveEscalatedRunAsync()
    {
        var runId = await SeedRunInStatusAsync(RunStatus.Escalated);
        using var client = await CreateAdminClientAsync();

        var response = await client.PostAsync(
            $"/api/v1/runs/{runId}/approve",
            content: null,
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await using var verifyDb = NewSystemDbContext();
        var run = verifyDb.Runs.Single(r => r.Id == new RunId(runId));
        run.Status.ShouldBe(RunStatus.Running);

        var entry = verifyDb.RunEvents.Single(e => e.RunId == new RunId(runId));
        entry.Type.ShouldBe(RunEventTypes.RunStatusChanged);
        entry.Payload.ShouldContain("\"from\"");
        entry.Payload.ShouldContain("Escalated");
        entry.Payload.ShouldContain("\"to\"");
        entry.Payload.ShouldContain("Running");
    }

    [Fact(DisplayName = "Given a run in Succeeded, when POST /approve, then 409 with run.terminal_state code")]
    public async Task ApproveTerminalRunReturnsConflictAsync()
    {
        var runId = await SeedRunInStatusAsync(RunStatus.Succeeded);
        using var client = await CreateAdminClientAsync();

        var response = await client.PostAsync(
            $"/api/v1/runs/{runId}/approve",
            content: null,
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldContain("\"code\":\"run.terminal_state\"");
        body.ShouldContain("\"currentStatus\":\"Succeeded\"");
    }

    [Fact(DisplayName = "Given a run in any non-terminal status, when POST /cancel with a reason, then status becomes Cancelled and the reason rides in the journal payload")]
    public async Task CancelInFlightRunWithReasonAsync()
    {
        var runId = await SeedRunInStatusAsync(RunStatus.Running);
        using var client = await CreateAdminClientAsync();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/runs/{runId}/cancel",
            new { reason = "operator closed the run" },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await using var verifyDb = NewSystemDbContext();
        var run = verifyDb.Runs.Single(r => r.Id == new RunId(runId));
        run.Status.ShouldBe(RunStatus.Cancelled);

        var entry = verifyDb.RunEvents.Single(e => e.RunId == new RunId(runId));
        entry.Type.ShouldBe(RunEventTypes.RunStatusChanged);
        entry.Payload.ShouldContain("operator closed the run");
    }

    [Fact(DisplayName = "Given no authentication, when POST /approve, then 401")]
    public async Task RefuseAnonymousApproveAsync()
    {
        using var client = new HttpClient { BaseAddress = baseAddress };

        var response = await client.PostAsync(
            $"/api/v1/runs/{Guid.NewGuid()}/approve",
            content: null,
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact(DisplayName = "Given a Running run with a live claimed WorkItem, when the run is cancelled, then the worker's stale-generation complete is rejected and the run stays Cancelled")]
    public async Task CancelFencesLiveWorkItemAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedRunningRunWithClaimedItemAsync();

        using var client = await CreateAdminClientAsync();
        var cancelResponse = await client.PostAsJsonAsync(
            $"/api/v1/runs/{seed.RunId}/cancel",
            new { reason = "ws5 acceptance" },
            cancellationToken);

        cancelResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var now = DateTimeOffset.UtcNow;
        using var claimScope = application.Services.CreateScope();
        var queue = claimScope.ServiceProvider.GetRequiredService<IWorkItemQueue>();
        var staleComplete = await queue.CompleteAsync(
            seed.WorkItemId,
            seed.WorkerId,
            seed.Generation,
            /*lang=json,strict*/ """{"summary":"stale"}""",
            now,
            cancellationToken);

        staleComplete.ShouldBeFalse();

        await using var verifyDb = NewSystemDbContext();
        var run = verifyDb.Runs.Single(r => r.Id == new RunId(seed.RunId));
        run.Status.ShouldBe(RunStatus.Cancelled);
        run.Generation.ShouldBeGreaterThan(0);

        var item = verifyDb.WorkItems.Single(i => i.Id == seed.WorkItemId);
        item.Status.ShouldBe(WorkItemStatus.Running);
        item.LeasedBy.ShouldNotBeNull();
        item.Generation.ShouldNotBe(seed.Generation);
    }

    [Fact(DisplayName = "Given a Running run with a live claimed WorkItem, when cancel and complete race concurrently, then exactly one resolves and the other is cleanly rejected — no deadlock")]
    public async Task CancelAndCompleteResolveAtomicallyAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedRunningRunWithClaimedItemAsync();

        using var client = await CreateAdminClientAsync();
        using var queueScope = application.Services.CreateScope();
        var queue = queueScope.ServiceProvider.GetRequiredService<IWorkItemQueue>();
        var now = DateTimeOffset.UtcNow;

        // Fire both without awaiting either first — genuine concurrent transactions
        // on separate connections, mirroring RunJournalShould's
        // FinalizeExactlyOnceOnConcurrentCompletionsAsync.
        var cancelTask = client.PostAsJsonAsync(
            $"/api/v1/runs/{seed.RunId}/cancel",
            new { reason = "race" },
            cancellationToken);
        var completeTask = queue.CompleteAsync(
            seed.WorkItemId,
            seed.WorkerId,
            seed.Generation,
            /*lang=json,strict*/ """{"summary":"racing"}""",
            now,
            cancellationToken);

        var cancelResponse = await cancelTask;
        var completeResult = await completeTask;

        // Exactly one resolves. The cancel HTTP always lands (either 204 or 409);
        // the queue Complete is either true or false.
        var cancelWon = cancelResponse.StatusCode == HttpStatusCode.NoContent;
        if (cancelWon)
        {
            completeResult.ShouldBeFalse();

            await using var verifyDb = NewSystemDbContext();
            var finalRun = verifyDb.Runs.Single(r => r.Id == new RunId(seed.RunId));
            finalRun.Status.ShouldBe(RunStatus.Cancelled);

            var item = verifyDb.WorkItems.Single(i => i.Id == seed.WorkItemId);
            item.Status.ShouldBe(WorkItemStatus.Running);
            item.Generation.ShouldNotBe(seed.Generation);
        }
        else
        {
            cancelResponse.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            completeResult.ShouldBeTrue();

            await using var verifyDb = NewSystemDbContext();
            var finalRun = verifyDb.Runs.Single(r => r.Id == new RunId(seed.RunId));
            finalRun.Status.ShouldBe(RunStatus.Succeeded);

            var item = verifyDb.WorkItems.Single(i => i.Id == seed.WorkItemId);
            item.Status.ShouldBe(WorkItemStatus.Succeeded);
        }
    }

    /// <summary>WS5 seeding helper: seeds a Run already in <see cref="RunStatus.Running"/>,
    /// a Queued WorkItem under it, then claims via the in-process
    /// <see cref="IWorkItemQueue"/> so the run is exercised through the real claim
    /// path (the run was set up in memory — it activates a no-op guard when the
    /// claim's RunProgression.ActivateAsync runs). Returns the state the two new
    /// acceptance tests need.</summary>
    private async Task<LiveWorkItemSeed> SeedRunningRunWithClaimedItemAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;

        var run = Run.Create(ProjectId.New(), now);
        run.TransitionTo(RunStatus.Running, now.AddSeconds(1));
        var workItem = WorkItem.Create(
            run.Id,
            WorkItemProfileKey,
            WorkItemImage,
            WorkItemProfilesRef,
            /*lang=json,strict*/ """{"goal":"ws5"}""",
            WorkItemStatus.Queued,
            now);

        await using (var seedContext = NewSystemDbContext())
        {
            seedContext.Runs.Add(run);
            seedContext.WorkItems.Add(workItem);
            await seedContext.SaveChangesAsync(cancellationToken);
        }

        var workerId = WorkerId.New();
        ClaimedWorkItem? claimed;
        using (var claimScope = application.Services.CreateScope())
        {
            var queue = claimScope.ServiceProvider.GetRequiredService<IWorkItemQueue>();
            claimed = await queue.ClaimAsync(
                workerId,
                new WorkItemLabels(WorkItemImage, WorkItemProfilesRef, WorkItemProfileKey),
                now.AddMinutes(2),
                now,
                cancellationToken);
        }

        claimed.ShouldNotBeNull();

        return new LiveWorkItemSeed(run.Id.Value, workItem.Id, workerId, claimed.Generation);
    }

    private const string WorkItemImage = "ghcr.io/comuki/worker:test";
    private const string WorkItemProfilesRef = "main";
    private const string WorkItemProfileKey = "implement";

    /// <summary>Projection returned by <see cref="SeedRunningRunWithClaimedItemAsync"/>
    /// — the run/work-item ids + worker id + claimed generation the two WS5 acceptance
    /// tests then drive against.</summary>
    /// <param name="RunId"></param>
    /// <param name="WorkItemId"></param>
    /// <param name="WorkerId"></param>
    /// <param name="Generation"></param>
    private sealed record LiveWorkItemSeed(Guid RunId, Guid WorkItemId, WorkerId WorkerId, int Generation);

    private OrchestrationDbContext NewSystemDbContext()
    {
        // A standalone DbContext with no ISubjectScopeAccessor sees every
        // row — see OrchestrationDbContext ctor: ScopeUnrestricted defaults
        // to true when accessor is null, bypassing the row-level filters
        // for the verification reads.
        var options = new DbContextOptionsBuilder<OrchestrationDbContext>()
            .UseNpgsql(
                    connectionString,
                    static npgsql => npgsql.MigrationsHistoryTable(
                        "__ef_migrations_history",
                        OrchestrationDatabase.Schema));
        return new OrchestrationDbContext(options.Options);
    }

    private async Task<Guid> SeedRunInStatusAsync(RunStatus target)
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var run = Run.Create(ProjectId.New(), DateTimeOffset.UtcNow);
        var chain = ResolveChain(target);
        var now = DateTimeOffset.UtcNow;
        var step = now.AddSeconds(1);
        foreach (var hop in chain)
        {
            run.TransitionTo(hop, step);
            step = step.AddSeconds(1);
        }

        await using var seedContext = NewSystemDbContext();
        seedContext.Runs.Add(run);
        await seedContext.SaveChangesAsync(cancellationToken);
        return run.Id.Value;
    }

    /// <summary>
    /// Map a target <see cref="RunStatus"/> to the legal sequence of
    /// transitions needed to reach it from <see cref="RunStatus.Queued"/>.
    /// </summary>
    /// <param name="target"></param>
    private static IReadOnlyList<RunStatus> ResolveChain(RunStatus target)
    {
        if (target == RunStatus.Queued)
        {
            return [];
        }

        if (target == RunStatus.Waiting)
        {
            return [RunStatus.Waiting];
        }

        if (target == RunStatus.Running)
        {
            return [RunStatus.Running];
        }

        if (target == RunStatus.Succeeded)
        {
            return [RunStatus.Running, RunStatus.Succeeded];
        }

        if (target == RunStatus.Failed)
        {
            return [RunStatus.Failed];
        }

        if (target == RunStatus.Cancelled)
        {
            return [RunStatus.Cancelled];
        }

        if (target == RunStatus.Escalated)
        {
            return [RunStatus.Running, RunStatus.Escalated];
        }

#pragma warning disable IDE0046
        throw new ArgumentOutOfRangeException(nameof(target), target, null);
#pragma warning restore IDE0046
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

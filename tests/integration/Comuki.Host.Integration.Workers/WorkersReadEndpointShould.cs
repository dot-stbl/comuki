using System.Net;
using System.Net.Http.Json;
using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Domain.Journal;
using Comuki.Engine.Orchestration.Domain.Runs;
using Comuki.Engine.Orchestration.Domain.WorkItems;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Host.Testing;
using Comuki.Host.Testing.Fixtures;
using Comuki.Shared.Kernel.Ids;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace Comuki.Host.Integration.Workers;

/// <summary>
/// Boots the real host composition on a random loopback port against one
/// shared, migrated Postgres (owned by this type's own
/// <see cref="PostgresCollectionFixture"/>, reset to empty before every
/// test) and exercises the derived workers read surface: page envelope
/// with a busy row, per-worker detail, the honest 404 / 501 paths and the
/// permission gate (anonymous 401).
/// </summary>
/// <param name="postgres">The collection's shared Postgres (<see cref="WorkersIntegrationCollection"/>) — reset to empty for every test, migrated once for the whole run.</param>
[Collection(nameof(WorkersIntegrationCollection))]
public sealed class WorkersReadEndpointShould(PostgresCollectionFixture postgres) : IAsyncLifetime
{
    /// <summary>
    /// boundary: initialised in InitializeAsync before any test runs
    /// </summary>
    private WebApplication application = null!;

    private TempControlPlaneRoot controlPlane = null!;

    private Uri baseAddress = null!;

    private static readonly ProjectId project = ProjectId.New();
    private static readonly WorkerId busyWorker = WorkerId.New();
    private static readonly WorkerId idleWorker = WorkerId.New();
    private Guid busyItemId = Guid.Empty;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await postgres.ResetDatabaseAsync();
        var connectionString = postgres.ConnectionString;

        // Relative to the real clock: the derivation runs against the
        // host's TimeProvider at request time, so a fixed past instant
        // would read every lease as stale (offline, not busy).
        var now = DateTimeOffset.UtcNow;

        var orchestrationOptions = new DbContextOptionsBuilder<OrchestrationDbContext>();
        OrchestrationDbContext.ApplyOptions(orchestrationOptions, connectionString);
        await using (var orchestrationDb = new OrchestrationDbContext(orchestrationOptions.Options))
        {
            var run = Run.Create(project, now.AddHours(-1));
            run.TransitionTo(RunStatus.Running, now.AddMinutes(-30));
            orchestrationDb.Runs.Add(run);

            // busy worker: a Running work item leased by busyWorker
            var busyItem = WorkItem.Create(
                run.Id, "implement", "ghcr.io/comuki/worker:test", "main", /*lang=json,strict*/ """{"step":"implement"}""",
                WorkItemStatus.Queued, now.AddMinutes(-20));
            busyItem.AssignLease(busyWorker, now.AddMinutes(2), now.AddMinutes(-1));
            orchestrationDb.WorkItems.Add(busyItem);
            busyItemId = busyItem.Id;

            // journal trace: busyWorker claimed (fresh), idleWorker claimed
            // 10 minutes ago and has nothing live now
            orchestrationDb.RunEvents.Add(RunEvent.Create(
                run.Id,
                RunEventTypes.WorkItemStatusChanged,
                $$"""{"itemId":"{{busyItem.Id}}","from":"Queued","to":"Running","workerId":"{{busyWorker.Value}}","attempt":1}""",
                now.AddMinutes(-1)));
            orchestrationDb.RunEvents.Add(RunEvent.Create(
                run.Id,
                RunEventTypes.WorkItemStatusChanged,
                $$"""{"itemId":"{{Guid.NewGuid()}}","from":"Queued","to":"Running","workerId":"{{idleWorker.Value}}","attempt":1}""",
                now.AddMinutes(-10)));

            await orchestrationDb.SaveChangesAsync(cancellationToken);
        }

        controlPlane = new TempControlPlaneRoot("workers");

        var builder = TestHostBuilder.Create(connectionString);
        builder.Configuration["ControlPlane:Root"] = controlPlane.Root;
        TestBootstrapAdmin.Configure(builder.Configuration);
        TestArtifactsSecrets.ApplyPlaceholder(builder.Configuration);

        application = await HostComposer.ComposeAsync(builder, HostDatabase.Explicit(connectionString));
        baseAddress = await TestHostBuilder.StartAsync(application, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await application.DisposeAsync();
        controlPlane.Dispose();
    }

    private Task<HttpClient> CreateAdminClientAsync()
    {
        var client = new HttpClient(new HttpClientHandler { UseCookies = true, CheckCertificateRevocationList = true })
        {
            BaseAddress = baseAddress,
        };

        return client.LoginAsBootstrapAdminAsync(TestContext.Current.CancellationToken);
    }

    private sealed record WorkersPageView(
        IReadOnlyList<WorkerView> Items,
        int Page,
        int PageSize,
        int Total);

    private sealed record WorkerView(
        Guid WorkerId,
        string State,
        Guid? ProjectId,
        string? ProfileKey,
        string? Image,
        Guid? CurrentWorkItemId,
        Guid? CurrentRunId,
        DateTimeOffset? LeaseUntil,
        DateTimeOffset? HeartbeatAt,
        int Attempt,
        DateTimeOffset LastSeenAt);

    [Fact(DisplayName = "Given a live lease and a journal trace, when workers are listed, then busy and idle rows derive with the page envelope")]
    public async Task ListDerivedWorkersAsync()
    {
        using var client = await CreateAdminClientAsync();

        var page = await client.GetFromJsonAsync<WorkersPageView>("/api/v1/workers?page=1&pageSize=10", TestContext.Current.CancellationToken);

        page.ShouldNotBeNull();
        page.Total.ShouldBe(2);
        var busy = page.Items.Single(static worker => worker.WorkerId == busyWorker.Value);
        busy.State.ShouldBe("busy");
        busy.ProjectId.ShouldBe(project.Value);
        busy.ProfileKey.ShouldBe("implement");
        busy.CurrentWorkItemId.ShouldBe(busyItemId);
        busy.Attempt.ShouldBe(1);
        var idle = page.Items.Single(static worker => worker.WorkerId == idleWorker.Value);
        idle.State.ShouldBe("idle");
        idle.CurrentWorkItemId.ShouldBeNull();
    }

    [Fact(DisplayName = "Given a busy worker, when read by id, then the derived detail returns the live item")]
    public async Task ReadWorkerDetailAsync()
    {
        using var client = await CreateAdminClientAsync();

        var worker = await client.GetFromJsonAsync<WorkerView>($"/api/v1/workers/{busyWorker.Value}", TestContext.Current.CancellationToken);

        worker.ShouldNotBeNull();
        worker.State.ShouldBe("busy");
        worker.CurrentWorkItemId.ShouldBe(busyItemId);
    }

    [Fact(DisplayName = "Given an unknown worker, when read by id, then a 404 problem returns")]
    public async Task Return404ForUnknownWorkerAsync()
    {
        using var client = await CreateAdminClientAsync();

        var response = await client.GetAsync($"/api/v1/workers/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldContain("worker.not_found");
    }

    [Fact(DisplayName = "Given drain and stop have no runtime mapping, when called, then both answer 501 honestly")]
    public async Task Answer501ForUnsupportedActionsAsync()
    {
        using var client = await CreateAdminClientAsync();

        var drain = await client.PostAsync($"/api/v1/workers/{busyWorker.Value}/drain", content: null, TestContext.Current.CancellationToken);
        var stop = await client.PostAsync($"/api/v1/workers/{busyWorker.Value}/stop", content: null, TestContext.Current.CancellationToken);

        drain.StatusCode.ShouldBe(HttpStatusCode.NotImplemented);
        (await drain.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("worker.drain_unsupported");
        stop.StatusCode.ShouldBe(HttpStatusCode.NotImplemented);
        (await stop.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("worker.stop_unsupported");
    }

    [Fact(DisplayName = "Given no authentication, when the endpoint is called, then it is rejected")]
    public async Task RefuseAnonymousAsync()
    {
        using var client = new HttpClient { BaseAddress = baseAddress };

        var response = await client.GetAsync("/api/v1/workers", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}

/// <summary>
/// One shared Postgres for the whole Workers suite (WS2), never in
/// parallel: two Testcontainers hosts on the same Docker daemon starve the
/// bootstrap-admin seed long enough for the faster class to win and the
/// slower boot to get canceled mid-start — the same contract
/// <c>CostsIntegrationCollection</c> documents.
/// </summary>
[CollectionDefinition(nameof(WorkersIntegrationCollection), DisableParallelization = true)]
public sealed class WorkersIntegrationCollection : ICollectionFixture<PostgresCollectionFixture>;

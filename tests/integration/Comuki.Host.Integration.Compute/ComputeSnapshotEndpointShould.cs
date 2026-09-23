using System.Net;
using System.Net.Http.Json;
using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Domain.Runs;
using Comuki.Engine.Orchestration.Domain.WorkItems;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Host.Compute;
using Comuki.Host.Testing;
using Comuki.Host.Testing.Fixtures;
using Comuki.Shared.Kernel.Ids;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace Comuki.Host.Integration.Compute;

/// <summary>
/// Boots the real host composition on a random loopback port against one
/// shared, migrated Postgres (owned by this type's own
/// <see cref="PostgresCollectionFixture"/>, reset to empty before every
/// test) and exercises <c>GET /api/v1/compute</c> — the only coverage
/// this endpoint had before this suite was smoke-level 200 checks
/// (2026-09-23 backend audit): the configured provider, the scale
/// defaults, and per-project × per-profile queued/running counts derived
/// from the work-item queue, plus the permission gate (anonymous 401).
/// </summary>
/// <param name="postgres">The collection's shared Postgres (<see cref="ComputeIntegrationCollection"/>) — reset to empty for every test, migrated once for the whole run.</param>
[Collection(nameof(ComputeIntegrationCollection))]
public sealed class ComputeSnapshotEndpointShould(PostgresCollectionFixture postgres) : IAsyncLifetime
{
    /// <summary>
    /// boundary: initialised in InitializeAsync before any test runs
    /// </summary>
    private WebApplication application = null!;

    /// <summary>boundary: initialised in InitializeAsync before any test runs</summary>
    private TempControlPlaneRoot controlPlane = null!;

    /// <summary>boundary: initialised in InitializeAsync before any test runs</summary>
    private Uri baseAddress = null!;

    private readonly ProjectId projectA = ProjectId.New();
    private readonly ProjectId projectB = ProjectId.New();

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await postgres.ResetDatabaseAsync();
        var connectionString = postgres.ConnectionString;

        var now = DateTimeOffset.UtcNow;

        var orchestrationOptions = new DbContextOptionsBuilder<OrchestrationDbContext>();
        OrchestrationDbContext.ApplyOptions(orchestrationOptions, connectionString);
        await using (var orchestrationDb = new OrchestrationDbContext(orchestrationOptions.Options))
        {
            var runA = Run.Create(projectA, now.AddHours(-1));
            var runB = Run.Create(projectB, now.AddHours(-1));
            orchestrationDb.Runs.AddRange(runA, runB);

            // projectA / implement: two queued, one running.
            var queuedOne = WorkItem.Create(runA.Id, "implement", "ghcr.io/comuki/worker:test", "main", /*lang=json,strict*/ """{"step":"one"}""", WorkItemStatus.Queued, now);
            var queuedTwo = WorkItem.Create(runA.Id, "implement", "ghcr.io/comuki/worker:test", "main", /*lang=json,strict*/ """{"step":"two"}""", WorkItemStatus.Queued, now);
            var running = WorkItem.Create(runA.Id, "implement", "ghcr.io/comuki/worker:test", "main", /*lang=json,strict*/ """{"step":"three"}""", WorkItemStatus.Queued, now);
            running.AssignLease(WorkerId.New(), now.AddMinutes(5), now);

            // A completed item — excluded from every count (neither Queued nor Running).
            var done = WorkItem.Create(runA.Id, "implement", "ghcr.io/comuki/worker:test", "main", /*lang=json,strict*/ """{"step":"four"}""", WorkItemStatus.Queued, now);
            done.AssignLease(WorkerId.New(), now.AddMinutes(5), now);
            done.TransitionTo(WorkItemStatus.Succeeded, now);

            // projectB / review: one queued.
            var reviewQueued = WorkItem.Create(runB.Id, "review", "ghcr.io/comuki/worker:test", "main", /*lang=json,strict*/ """{"step":"one"}""", WorkItemStatus.Queued, now);

            orchestrationDb.WorkItems.AddRange(queuedOne, queuedTwo, running, done, reviewQueued);
            await orchestrationDb.SaveChangesAsync(cancellationToken);
        }

        controlPlane = new TempControlPlaneRoot("compute");

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

    [Fact(DisplayName = "Given seeded queued/running work items across two projects, when the compute snapshot is read, then the provider, scale defaults and per-project × per-profile pool counts return, excluding terminal items")]
    public async Task ReturnComputeSnapshotAsync()
    {
        using var client = await CreateAdminClientAsync();

        var view = await client.GetFromJsonAsync<ComputeSnapshotView>("/api/v1/compute", TestContext.Current.CancellationToken);

        view.ShouldNotBeNull();
        view.Provider.ShouldBe("docker");

        // ScaleSupervisorOptions defaults — no Compute:Scale:* override configured for this suite.
        view.Defaults.WorkerImage.ShouldBe("ghcr.io/dot-stbl/comuki-worker");
        view.Defaults.ProfilesGitRef.ShouldBe("main");
        view.Defaults.MinIdle.ShouldBe(0);
        view.Defaults.MaxConcurrent.ShouldBe(4);
        view.Defaults.IdleTtlSeconds.ShouldBe(600L);
        view.Defaults.PollIntervalSeconds.ShouldBe(15L);

        view.Pools.Count.ShouldBe(2);

        var poolA = view.Pools.Single(pool => pool.ProjectId == projectA.Value);
        poolA.ProfileKey.ShouldBe("implement");
        poolA.Queued.ShouldBe(2);
        poolA.Running.ShouldBe(1);
        poolA.MinIdle.ShouldBe(0);
        poolA.MaxConcurrent.ShouldBe(4);

        var poolB = view.Pools.Single(pool => pool.ProjectId == projectB.Value);
        poolB.ProfileKey.ShouldBe("review");
        poolB.Queued.ShouldBe(1);
        poolB.Running.ShouldBe(0);
    }

    [Fact(DisplayName = "Given no authentication, when the compute snapshot endpoint is called, then it is rejected")]
    public async Task RefuseAnonymousAsync()
    {
        using var client = new HttpClient { BaseAddress = baseAddress };

        var response = await client.GetAsync("/api/v1/compute", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}

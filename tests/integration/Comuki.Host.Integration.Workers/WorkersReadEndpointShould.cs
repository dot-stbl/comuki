using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Domain.Journal;
using Comuki.Engine.Orchestration.Domain.Runs;
using Comuki.Engine.Orchestration.Domain.WorkItems;
using Comuki.Engine.Orchestration.Infrastructure;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Modules.Identity.Infrastructure.Persistence;
using Comuki.Modules.Projects.Infrastructure.Persistence;
using Comuki.Shared.Kernel.Ids;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shouldly;
using Testcontainers.PostgreSql;
using Xunit;

namespace Comuki.Host.Integration.Workers;

/// <summary>
/// Boots the real host composition on a random loopback port against one
/// migrated Testcontainers Postgres (same contract as the runs fixture) and
/// exercises the derived workers read surface: page envelope with a busy
/// row, per-worker detail, the honest 404 / 501 paths and the permission
/// gate (anonymous 401).
/// </summary>
public sealed class WorkersReadEndpointShould : IAsyncLifetime
{
    private const string BootstrapEmail = "bootstrap@comuki.test";
    private const string BootstrapPassword = "bootstrap-pass-1";

    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    /// <summary>
    /// boundary: initialised in InitializeAsync before any test runs
    /// </summary>
    private WebApplication application = null!;

    private Uri baseAddress = null!;

    private static readonly ProjectId project = ProjectId.New();
    private static readonly WorkerId busyWorker = WorkerId.New();
    private static readonly WorkerId idleWorker = WorkerId.New();
    private Guid busyItemId = Guid.Empty;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await container.StartAsync(cancellationToken);

        var connectionString = container.GetConnectionString();

        // Relative to the real clock: the derivation runs against the
        // host's TimeProvider at request time, so a fixed past instant
        // would read every lease as stale (offline, not busy).
        var now = DateTimeOffset.UtcNow;

        var orchestrationOptions = new DbContextOptionsBuilder<OrchestrationDbContext>();
        OrchestrationDbContext.ApplyOptions(orchestrationOptions, connectionString);
        await using (var orchestrationDb = new OrchestrationDbContext(orchestrationOptions.Options))
        {
            await orchestrationDb.Database.MigrateAsync(cancellationToken);

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
                EnvironmentName = Environments.Development,
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
        builder.Services
            .AddOrchestrationPersistence(connectionString)
            .AddOrchestrationQueue(builder.Configuration);

        application = HostComposer.Compose(builder, HostDatabase.Explicit(connectionString));
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
        await container.DisposeAsync();
    }

    private async Task<HttpClient> CreateAdminClientAsync()
    {
        var client = new HttpClient(new HttpClientHandler { UseCookies = true, CheckCertificateRevocationList = true })
        {
            BaseAddress = baseAddress,
        };

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { email = BootstrapEmail, password = BootstrapPassword },
            TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return client;
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

    private static int FreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        return port;
    }
}

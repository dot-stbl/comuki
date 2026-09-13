using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using Comuki.Engine.Orchestration.Infrastructure;
using Comuki.Modules.Costs.Domain.Events;
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

namespace Comuki.Host.Integration.Costs;

/// <summary>
/// Boots the real host composition on a random loopback port against one
/// migrated Testcontainers Postgres and exercises
/// <c>GET /api/v1/costs</c>: the platform-wide rollup over a seeded
/// usage-events table (per-project slices, per-day series, window and
/// all-time totals) and the permission gate (anonymous 401).
/// </summary>
[Collection(nameof(CostsIntegrationCollection))]
public sealed class PlatformCostsEndpointShould : IAsyncLifetime
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

    private readonly ProjectId alphaProject = ProjectId.New();
    private readonly ProjectId betaProject = ProjectId.New();

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await container.StartAsync(cancellationToken);

        var connectionString = container.GetConnectionString();
        var now = DateTimeOffset.UtcNow;

        // Orchestration is migrated even though the rollup never reads it:
        // the host's lease reaper / sweeper BackgroundServices poll these
        // tables from the first second of boot, and a missing table fails
        // the whole host (BackgroundServiceExceptionBehavior=StopHost).
        var orchestrationOptions = new DbContextOptionsBuilder<Engine.Orchestration.Infrastructure.Persistence.OrchestrationDbContext>();
        Engine.Orchestration.Infrastructure.Persistence.OrchestrationDbContext.ApplyOptions(orchestrationOptions, connectionString);
        await using (var orchestrationDb = new Engine.Orchestration.Infrastructure.Persistence.OrchestrationDbContext(orchestrationOptions.Options))
        {
            await orchestrationDb.Database.MigrateAsync(cancellationToken);
        }

        var costsOptions = new DbContextOptionsBuilder<Modules.Costs.Infrastructure.Persistence.CostsDbContext>();
        Modules.Costs.Infrastructure.Persistence.CostsDbContext.ApplyOptions(costsOptions, connectionString);
        await using (var costsDb = new Modules.Costs.Infrastructure.Persistence.CostsDbContext(costsOptions.Options))
        {
            await costsDb.Database.MigrateAsync(cancellationToken);

            var runOfAlpha = RunId.New();
            costsDb.UsageEvents.Add(UsageEvent.Create(
                alphaProject, runOfAlpha, UsageSource.Proxy, "model-a", 10, 20, 1_000, now.AddMinutes(-10)));
            costsDb.UsageEvents.Add(UsageEvent.Create(
                alphaProject, null, UsageSource.Brain, "model-a", 1, 2, 200, now.AddMinutes(-5)));
            costsDb.UsageEvents.Add(UsageEvent.Create(
                betaProject, RunId.New(), UsageSource.Worker, "model-b", 5, 5, 500, now.AddDays(-2)));
            // far outside every window — only the all-time total sees it
            costsDb.UsageEvents.Add(UsageEvent.Create(
                betaProject, null, UsageSource.Proxy, "model-b", 1, 1, 9_000, now.AddDays(-400)));

            await costsDb.SaveChangesAsync(cancellationToken);
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

        // Boot the host detached from the test token: StartAsync runs every
        // hosted service, and a cancel mid-boot surfaces as an opaque
        // OperationCanceledException from whichever service was slower —
        // a real startup failure must surface as itself.
        using var bootCancellationTokenSource = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await application.StartAsync(bootCancellationTokenSource.Token);

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

    private sealed record PlatformCostsView(
        DateTimeOffset Since,
        int WindowDays,
        long WindowUsdMicros,
        long AllTimeUsdMicros,
        IReadOnlyList<ProjectCostSliceView> ByProject,
        IReadOnlyList<DayCostSliceView> ByDay);

    private sealed record ProjectCostSliceView(Guid ProjectId, long CostUsdMicros, int Runs);

    private sealed record DayCostSliceView(DateOnly Date, long CostUsdMicros);

    [Fact(DisplayName = "Given seeded usage events, when the rollup is read with days=7, then window, all-time, per-project and per-day slices return")]
    public async Task RollUpPlatformCostsAsync()
    {
        using var client = await CreateAdminClientAsync();

        var view = await client.GetFromJsonAsync<PlatformCostsView>("/api/v1/costs?days=7", TestContext.Current.CancellationToken);

        view.ShouldNotBeNull();
        view.WindowDays.ShouldBe(7);
        view.WindowUsdMicros.ShouldBe(1_700);
        view.AllTimeUsdMicros.ShouldBe(10_700);

        view.ByProject.Count.ShouldBe(2);
        var alpha = view.ByProject.Single(slice => slice.ProjectId == alphaProject.Value);
        alpha.CostUsdMicros.ShouldBe(1_200);
        alpha.Runs.ShouldBe(1);
        var beta = view.ByProject.Single(slice => slice.ProjectId == betaProject.Value);
        beta.CostUsdMicros.ShouldBe(500);
        beta.Runs.ShouldBe(1);

        // By-day is asserted relative to the window, not to wall-clock
        // dates: the two same-project events sit minutes apart, and
        // whether they share a UTC day depends on when the test runs.
        view.ByDay.Count.ShouldBeGreaterThanOrEqualTo(2);
        view.ByDay.Sum(static slice => slice.CostUsdMicros).ShouldBe(view.WindowUsdMicros);
        view.ByDay.Last().Date.ShouldBe(DateOnly.FromDateTime(DateTime.UtcNow));
        view.ByDay.SequenceEqual([.. view.ByDay.OrderBy(slice => slice.Date)]).ShouldBeTrue();
    }

    [Fact(DisplayName = "Given no authentication, when the endpoint is called, then it is rejected")]
    public async Task RefuseAnonymousAsync()
    {
        using var client = new HttpClient { BaseAddress = baseAddress };

        var response = await client.GetAsync("/api/v1/costs", TestContext.Current.CancellationToken);

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

/// <summary>
/// One container per class, never in parallel: two Testcontainers hosts on
/// the same Docker daemon starve the bootstrap-admin seed long enough for
/// the faster class to win and the slower boot to get canceled mid-start.
/// </summary>
[CollectionDefinition(nameof(CostsIntegrationCollection), DisableParallelization = true)]
public sealed class CostsIntegrationCollection;

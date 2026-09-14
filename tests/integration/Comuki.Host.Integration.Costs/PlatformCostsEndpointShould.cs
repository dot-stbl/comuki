using System.Net;
using System.Net.Http.Json;
using Comuki.Host.Testing;
using Comuki.Modules.Costs.Domain.Events;
using Comuki.Modules.Costs.Infrastructure.Persistence;
using Comuki.Shared.Kernel.Ids;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Testcontainers.PostgreSql;
using Xunit;

namespace Comuki.Host.Integration.Costs;

/// <summary>
/// Boots the real host composition on a random loopback port against one
/// migrated Testcontainers Postgres (every module context, via
/// <see cref="HostDatabaseMigrator"/>) and exercises
/// <c>GET /api/v1/costs</c>: the platform-wide rollup over a seeded
/// usage-events table (per-project slices, per-day series, window and
/// all-time totals) and the permission gate (anonymous 401).
/// </summary>
[Collection(nameof(CostsIntegrationCollection))]
public sealed class PlatformCostsEndpointShould : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    /// <summary>
    /// boundary: initialised in InitializeAsync before any test runs
    /// </summary>
    private WebApplication application = null!;

    private TempControlPlaneRoot controlPlane = null!;

    private Uri baseAddress = null!;

    private readonly ProjectId alphaProject = ProjectId.New();
    private readonly ProjectId betaProject = ProjectId.New();

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await container.StartAsync(cancellationToken);

        var connectionString = container.GetConnectionString();
        await HostDatabaseMigrator.MigrateAllAsync(connectionString, cancellationToken);

        var now = DateTimeOffset.UtcNow;

        var costsOptions = new DbContextOptionsBuilder<CostsDbContext>();
        CostsDbContext.ApplyOptions(costsOptions, connectionString);
        await using (var costsDb = new CostsDbContext(costsOptions.Options))
        {
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

        controlPlane = new TempControlPlaneRoot("costs");

        var builder = TestHostBuilder.Create(connectionString);
        builder.Configuration["ControlPlane:Root"] = controlPlane.Root;
        TestBootstrapAdmin.Configure(builder.Configuration);
        TestArtifactsSecrets.ApplyPlaceholder(builder.Configuration);

        application = await HostComposer.ComposeAsync(builder, HostDatabase.Explicit(connectionString));

        // Boot the host detached from the test token: StartAsync runs every
        // hosted service, and a cancel mid-boot surfaces as an opaque
        // OperationCanceledException from whichever service was slower —
        // a real startup failure must surface as itself.
        using var bootCancellationTokenSource = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        baseAddress = await TestHostBuilder.StartAsync(application, bootCancellationTokenSource.Token);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await application.DisposeAsync();
        controlPlane.Dispose();
        await container.DisposeAsync();
    }

    private Task<HttpClient> CreateAdminClientAsync()
    {
        var client = new HttpClient(new HttpClientHandler { UseCookies = true, CheckCertificateRevocationList = true })
        {
            BaseAddress = baseAddress,
        };

        return client.LoginAsBootstrapAdminAsync(TestContext.Current.CancellationToken);
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
}

/// <summary>
/// One container per class, never in parallel: two Testcontainers hosts on
/// the same Docker daemon starve the bootstrap-admin seed long enough for
/// the faster class to win and the slower boot to get canceled mid-start.
/// </summary>
[CollectionDefinition(nameof(CostsIntegrationCollection), DisableParallelization = true)]
public sealed class CostsIntegrationCollection;

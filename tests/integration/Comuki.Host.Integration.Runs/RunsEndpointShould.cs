using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Domain.Runs;
using Comuki.Engine.Orchestration.Infrastructure;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Host.Testing;
using Comuki.Host.Testing.Fixtures;
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
/// Boots the real host composition on a random loopback port against one
/// shared, migrated Postgres (owned by this collection's
/// <see cref="PostgresCollectionFixture"/>, reset to empty before every
/// test — see <see cref="RunsIntegrationCollection"/>) and exercises
/// <c>GET /api/v1/runs</c>: filter DSL, sort, paging envelope, permission
/// gate (anonymous 401) and the 400 path for an illegal filter.
/// </summary>
/// <param name="postgres">The collection's shared Postgres — one container for the whole Runs suite, not one per test.</param>
[Collection(nameof(RunsIntegrationCollection))]
public sealed class RunsEndpointShould(PostgresCollectionFixture postgres) : IAsyncLifetime
{
    private const string BootstrapEmail = "bootstrap@comuki.test";
    private const string BootstrapPassword = "bootstrap-pass-1";

    /// <summary>
    /// boundary: initialised in InitializeAsync before any test runs
    /// </summary>
    private WebApplication application = null!;

    private Uri baseAddress = null!;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await postgres.ResetDatabaseAsync();

        var connectionString = postgres.ConnectionString;

        var orchestrationOptions = new DbContextOptionsBuilder<OrchestrationDbContext>();
        OrchestrationDbContext.ApplyOptions(orchestrationOptions, connectionString);
        await using (var orchestrationDb = new OrchestrationDbContext(orchestrationOptions.Options))
        {
            // Every module's schema, including this one, is already
            // migrated once by PostgresCollectionFixture — no per-context
            // MigrateAsync needed here anymore.
            var now = DateTimeOffset.UtcNow;

            foreach (var (status, age) in new[]
                     {
                         (RunStatus.Running, TimeSpan.FromHours(-1)),
                         (RunStatus.Succeeded, TimeSpan.FromHours(-5)),
                         (RunStatus.Failed, TimeSpan.FromDays(-2)),
                     })
            {
                var run = Run.Create(ProjectId.New(), now + age);

                if (status == RunStatus.Succeeded)
                {
                    run.TransitionTo(RunStatus.Running, now + age + TimeSpan.FromMinutes(1));
                }

                run.TransitionTo(status, now + age + TimeSpan.FromMinutes(5));
                orchestrationDb.Runs.Add(run);
            }

            await orchestrationDb.SaveChangesAsync(cancellationToken);
        }

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
        // Artifacts module — non-dev-default secrets so the production-secret
        // validator (issue #10 T11.4) passes through. The integration
        // suite does not boot a MinIO Testcontainer; the host still
        // validates the options so we satisfy the contract with non-dev
        // throwaway values.
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

    private sealed record RunsPageView(
        IReadOnlyList<RunView> Items,
        int Page,
        int PageSize,
        int Total);

    private sealed record RunView(
        Guid Id,
        Guid ProjectId,
        string Status,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);

    [Fact(DisplayName = "Given seeded runs, when listed as admin, then the page envelope carries all rows newest-agnostic")]
    public async Task ListRunsAsync()
    {
        using var client = await CreateAdminClientAsync();

        var page = await client.GetFromJsonAsync<RunsPageView>("/api/v1/runs?page=1&pageSize=10", TestContext.Current.CancellationToken);

        page.ShouldNotBeNull();
        page.Total.ShouldBe(3);
        page.Items.Count.ShouldBe(3);
        page.Items.Select(static run => run.Status).ShouldBe(["running", "succeeded", "failed"]);
    }

    [Fact(DisplayName = "Given seeded runs, when filtered by status, then only matching rows return with a matching total")]
    public async Task FilterRunsByStatusAsync()
    {
        using var client = await CreateAdminClientAsync();

        var page = await client.GetFromJsonAsync<RunsPageView>("/api/v1/runs?filter=Status==Failed&pageSize=10", TestContext.Current.CancellationToken);

        page.ShouldNotBeNull();
        page.Total.ShouldBe(1);
        page.Items.Single().Status.ShouldBe("failed");
    }

    [Fact(DisplayName = "Given seeded runs, when sorted by createdAt desc, then the newest run leads the page")]
    public async Task SortRunsByCreatedAtDescAsync()
    {
        using var client = await CreateAdminClientAsync();

        var page = await client.GetFromJsonAsync<RunsPageView>("/api/v1/runs?sort=CreatedAt,desc&pageSize=10", TestContext.Current.CancellationToken);

        page.ShouldNotBeNull();
        page.Items.Select(static run => run.Status).ShouldBe(["running", "succeeded", "failed"]);
    }

    [Fact(DisplayName = "Given paging bounds, when page 2 of size 2 is requested, then the second slice returns")]
    public async Task PageRunsAsync()
    {
        using var client = await CreateAdminClientAsync();

        var page = await client.GetFromJsonAsync<RunsPageView>("/api/v1/runs?sort=CreatedAt,asc&page=2&pageSize=2", TestContext.Current.CancellationToken);

        page.ShouldNotBeNull();
        page.Page.ShouldBe(2);
        page.PageSize.ShouldBe(2);
        page.Total.ShouldBe(3);
        page.Items.Single().Status.ShouldBe("running");
    }

    [Fact(DisplayName = "Given an illegal filter, when the endpoint is called, then a 400 problem returns")]
    public async Task RejectIllegalFilterAsync()
    {
        using var client = await CreateAdminClientAsync();

        var response = await client.GetAsync("/api/v1/runs?filter=Nope==1", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldContain("filter.invalid");
    }

    [Fact(DisplayName = "Given no authentication, when the endpoint is called, then it is rejected")]
    public async Task RefuseAnonymousAsync()
    {
        using var client = new HttpClient { BaseAddress = baseAddress };

        var response = await client.GetAsync("/api/v1/runs", TestContext.Current.CancellationToken);

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
/// One shared Postgres for the whole Runs suite (WS2). Before this
/// collection existed, none of the three Runs test classes carried a
/// <c>[Collection]</c> attribute at all, so xUnit ran them in three
/// separate implicit collections — in parallel, by default, three
/// full-host Testcontainers instances at once. That is a real, observed
/// flake source (verified against master: repeated baseline runs showed a
/// different one of the three classes' tests failing each time — an
/// "ageSeconds" timing assertion in <c>EscalationTimeoutSweeperShould</c>
/// once, a seeded-runs-filtered-by-status 400 in this class another time),
/// not a hypothetical one. <c>DisableParallelization = true</c> here fixes
/// that as a side effect of the shared-fixture conversion, the same
/// contract <c>CostsIntegrationCollection</c> and
/// <c>WorkersIntegrationCollection</c> already document.
/// </summary>
[CollectionDefinition(nameof(RunsIntegrationCollection), DisableParallelization = true)]
public sealed class RunsIntegrationCollection : ICollectionFixture<PostgresCollectionFixture>;

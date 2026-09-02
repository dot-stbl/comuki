using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Domain.Runs;
using Comuki.Engine.Orchestration.Infrastructure;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Modules.Identity.Infrastructure.Persistence;
using Comuki.Modules.Projects.Infrastructure.Persistence;
using Comuki.Shared.Kernel.Ids;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using Testcontainers.PostgreSql;
using Xunit;

namespace Comuki.Host.Integration.Runs;

/// <summary>
/// Boots the real host composition on a random loopback port against one
/// migrated Testcontainers Postgres (same contract as the chat fixture) and
/// exercises <c>GET /api/v1/runs</c>: filter DSL, sort, paging envelope,
/// permission gate (anonymous 401) and the 400 path for an illegal filter.
/// </summary>
public sealed class RunsEndpointShould : IAsyncLifetime
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

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await container.StartAsync(cancellationToken);

        var connectionString = container.GetConnectionString();

        var orchestrationOptions = new DbContextOptionsBuilder<OrchestrationDbContext>();
        OrchestrationDbContext.ApplyOptions(orchestrationOptions, connectionString);
        await using (var orchestrationDb = new OrchestrationDbContext(orchestrationOptions.Options))
        {
            await orchestrationDb.Database.MigrateAsync(cancellationToken);

            var now = DateTimeOffset.UtcNow;

            foreach (var (status, age) in new[]
                     {
                         (RunStatus.Running, TimeSpan.FromHours(-1)),
                         (RunStatus.Succeeded, TimeSpan.FromHours(-5)),
                         (RunStatus.Failed, TimeSpan.FromDays(-2)),
                     })
            {
                var run = Run.Create(ProjectId.New(), now + age);

                if (status is RunStatus.Succeeded)
                {
                    run.TransitionTo(RunStatus.Running, now + age + TimeSpan.FromMinutes(1));
                }

                run.TransitionTo(status, now + age + TimeSpan.FromMinutes(5));
                _ = orchestrationDb.Runs.Add(run);
            }

            _ = await orchestrationDb.SaveChangesAsync(cancellationToken);
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
            new WebApplicationOptions { ApplicationName = typeof(HostComposer).Assembly.GetName().Name });
        builder.WebHost.UseUrls($"http://127.0.0.1:{FreeTcpPort()}");
        builder.Logging.ClearProviders();
        builder.Configuration["ControlPlane:Root"] = Path.GetTempPath();
        builder.Configuration["auth:bootstrap:adminEmail"] = BootstrapEmail;
        builder.Configuration["auth:bootstrap:adminPassword"] = BootstrapPassword;
        builder.Services.AddOrchestrationPersistence(connectionString);

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

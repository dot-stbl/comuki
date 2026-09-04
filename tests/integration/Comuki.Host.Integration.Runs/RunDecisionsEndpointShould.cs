using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Domain.Journal;
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
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shouldly;
using Testcontainers.PostgreSql;
using Xunit;

namespace Comuki.Host.Integration.Runs;

/// <summary>
/// End-to-end coverage for the operator decision endpoints
/// (<c>POST /api/v1/runs/{runId}/approve</c> + <c>/cancel</c>). Boots the
/// real host composition against one Testcontainers Postgres (same fixture
/// the listing test uses), drives an admin through the wire, and
/// asserts on the orchestration context + journal rows in the same
/// database the request mutated.
/// </summary>
public sealed class RunDecisionsEndpointShould : IAsyncLifetime
{
    private const string BootstrapEmail = "bootstrap@comuki.test";
    private const string BootstrapPassword = "bootstrap-pass-1";

    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    private WebApplication application = null!;
    private Uri baseAddress = null!;
    private string connectionString = string.Empty;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await container.StartAsync(cancellationToken);

        connectionString = container.GetConnectionString();

        var orchestrationOptions = new DbContextOptionsBuilder<OrchestrationDbContext>();
        OrchestrationDbContext.ApplyOptions(orchestrationOptions, connectionString);
        await using (var orchestrationDb = new OrchestrationDbContext(orchestrationOptions.Options))
        {
            await orchestrationDb.Database.MigrateAsync(cancellationToken);
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
                // Production env on purpose: ValidateScopes off; production-secret
                // validator (issue #10 T11.4) satisfied by non-dev-default secrets.
                EnvironmentName = Environments.Production,
            });
        builder.WebHost.UseUrls($"http://127.0.0.1:{FreeTcpPort()}");
        builder.Logging.ClearProviders();
        builder.Configuration["ControlPlane:Root"] = Path.GetTempPath();
        builder.Configuration["auth:bootstrap:adminEmail"] = BootstrapEmail;
        builder.Configuration["auth:bootstrap:adminPassword"] = BootstrapPassword;
        // Non-dev-default secrets so the production-secret validator
        // (issue #10 T11.4) passes through.
        builder.Configuration["Artifacts:Endpoint"] = "minio:9000";
        builder.Configuration["Artifacts:AccessKey"] = "test-access-key";
        builder.Configuration["Artifacts:SecretKey"] = "test-secret-key-with-enough-entropy";
        builder.Configuration["Artifacts:Bucket"] = "comuki-test-bundles";
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
        var chain = target switch
        {
            RunStatus.Queued => Array.Empty<RunStatus>(),
            RunStatus.Waiting => [RunStatus.Waiting],
            RunStatus.Running => [RunStatus.Running],
            RunStatus.Succeeded => [RunStatus.Running, RunStatus.Succeeded],
            RunStatus.Failed => [RunStatus.Failed],
            RunStatus.Cancelled => [RunStatus.Cancelled],
            RunStatus.Escalated => [RunStatus.Running, RunStatus.Escalated],
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, null),
        };

        var now = DateTimeOffset.UtcNow;
        var step = now.AddSeconds(1);
        foreach (var hop in chain)
        {
            run.TransitionTo(hop, step);
            step = step.AddSeconds(1);
        }

        await using var seedContext = NewSystemDbContext();
        _ = seedContext.Runs.Add(run);
        _ = await seedContext.SaveChangesAsync(cancellationToken);
        return run.Id.Value;
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

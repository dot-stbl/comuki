using System.Net;
using System.Net.Http.Json;
using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Domain.Runs;
using Comuki.Engine.Orchestration.Domain.WorkItems;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Host.Realtime;
using Comuki.Host.Testing;
using Comuki.Modules.Identity.Application.Users;
using Comuki.Shared.Kernel.Ids;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Testcontainers.PostgreSql;
using Xunit;

namespace Comuki.Host.Integration.Realtime;

/// <summary>
/// Boots the real host composition (<see cref="HostComposer"/>) on a
/// random loopback port against one migrated Testcontainers Postgres —
/// every module context, via <see cref="HostDatabaseMigrator"/> — the
/// HostChatServer pattern, extended with helpers for the realtime suite:
/// seeding a run with one queued work item, creating a permission-less
/// member account, and building cookie-authenticated hub connections.
/// </summary>
public sealed class HostRealtimeServer : IAsyncLifetime
{
    public const string BootstrapEmail = TestBootstrapAdmin.Email;
    public const string BootstrapPassword = TestBootstrapAdmin.Password;

    public const string MemberEmail = "member@comuki.test";
    public const string MemberPassword = "member-pass-1";

    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("pgvector/pgvector:pg16")
        .Build();

    private WebApplication application = null!;
    private TempControlPlaneRoot controlPlane = null!;
    private Uri baseAddress = null!;
    private bool detailedErrorsPreviouslySet;

    /// <summary>Bound on every hub read — SignalR defaults can be slow on cold containers.</summary>
    public static readonly TimeSpan HubTimeout = TimeSpan.FromSeconds(30);

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        // The realtime suite's HubException assertions expect stack frames
        // in the message — the production gate in AddComukiRealtime would
        // turn them off (issue #19). Flip the test-only opt-in here, before
        // HostComposer.Compose builds the SignalR options.
        detailedErrorsPreviouslySet = Environment.GetEnvironmentVariable(RealtimeExtensions.DetailedErrorsEnvVar) is { } already
            && string.Equals(already, "true", StringComparison.Ordinal);
        Environment.SetEnvironmentVariable(RealtimeExtensions.DetailedErrorsEnvVar, "true");

        await container.StartAsync(cancellationToken);

        var connectionString = container.GetConnectionString();
        await HostDatabaseMigrator.MigrateAllAsync(connectionString, cancellationToken);

        controlPlane = new TempControlPlaneRoot("realtime");
        controlPlane.WriteDefaultChatCommand();

        var builder = TestHostBuilder.Create(connectionString);
        builder.Configuration["ControlPlane:Root"] = controlPlane.Root;
        TestBootstrapAdmin.Configure(builder.Configuration);
        TestArtifactsSecrets.ApplyPlaceholder(builder.Configuration);

        application = HostComposer.Compose(builder, HostDatabase.Explicit(connectionString));
        baseAddress = await TestHostBuilder.StartAsync(application, cancellationToken);

        await HostRealtimeBootstrap.CreateMemberAccountAsync(application.Services);
    }

    /// <summary>The hub URL of the booted host.</summary>
    public Uri HubAddress => new(baseAddress, "ws/runs");

    /// <summary>The host's DI root — tests need it for fixtures that want to append journal rows or seed data through the real DbContext.</summary>
    public IServiceProvider Services => application.Services;

    /// <summary>
    /// Seeds one run in <c>Queued</c> with one <c>Queued</c> work item and
    /// returns their ids — the chat-run-starter shape, applied directly
    /// through the engine's domain factories. Each seed uses a unique
    /// profile key so ClaimAsync cannot pick up a leftover Queued item
    /// from a sibling test that never claimed (shared fixture DB).
    /// </summary>
    public async Task<(RunId RunId, Guid WorkItemId, string ProfileKey)> SeedRunAsync(ProjectId projectId)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        var profileKey = "profile-" + Guid.NewGuid().ToString("N");

        await using var scope = application.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OrchestrationDbContext>();
        using var systemScope = scope.ServiceProvider
            .GetRequiredService<Shared.Kernel.Scoping.ISubjectScopeAccessor>()
            .AsSystem("realtime-fixture");

        var run = Run.Create(projectId, now);
        var item = WorkItem.Create(
            run.Id,
            profileKey,
            "image",
            "profiles-ref",
                                 /*lang=json,strict*/
                                 """{"goal":"seed"}""",
            WorkItemStatus.Queued,
            now);
        db.Runs.Add(run);
        db.WorkItems.Add(item);
        await db.SaveChangesAsync(cancellationToken);

        return (run.Id, item.Id, profileKey);
    }

    /// <summary>Claims the seeded item through the real queue — the claim path a worker takes.</summary>
    public async Task ClaimAsync(RunId runId, Guid workItemId, string profileKey)
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = application.Services.CreateAsyncScope();
        var queue = scope.ServiceProvider.GetRequiredService<Shared.Contracts.Queue.IWorkItemQueue>();
        using var systemScope = scope.ServiceProvider
            .GetRequiredService<Shared.Kernel.Scoping.ISubjectScopeAccessor>()
            .AsSystem("realtime-fixture");

        var claimed = await queue.ClaimAsync(
            new WorkerId(Guid.NewGuid()),
            new Shared.Contracts.Queue.WorkItemLabels("image", "profiles-ref", profileKey),
            DateTimeOffset.UtcNow.AddMinutes(2),
            DateTimeOffset.UtcNow,
            cancellationToken);

        if (claimed is not { } item)
        {
            throw new InvalidOperationException("seeded work item was not claimable");
        }

        item.RunId.ShouldBe(runId);
        item.WorkItemId.ShouldBe(workItemId);
    }

    /// <summary>
    /// A logged-in hub connection: logs <paramref name="email"/> in over
    /// REST first, then rides the session cookie on the hub requests. The
    /// cookie rides as an explicit header — HttpConnectionOptions.Cookies
    /// proved unreliable against the default client handler here.
    /// </summary>
    public async Task<HubConnection> ConnectHubAsync(string email, string password)
    {
        var cookieHeader = await LoginCookieHeaderAsync(email, password);

        var connection = new HubConnectionBuilder()
            .WithUrl(
                HubAddress,
                options =>
                {
                    options.Headers["Cookie"] = cookieHeader;
                    options.Transports = HttpTransportType.WebSockets | HttpTransportType.LongPolling;
                })
            .Build();
        await connection.StartAsync(TestContext.Current.CancellationToken);

        return connection;
    }

    /// <summary>The login cookie of one REST login rendered as a Cookie header value.</summary>
    public async Task<string> LoginCookieHeaderAsync(string email, string password)
    {
        var cookieContainer = new CookieContainer();
        using var handler = new HttpClientHandler
        {
            CookieContainer = cookieContainer,
            CheckCertificateRevocationList = true,
        };
        using var client = new HttpClient(handler) { BaseAddress = baseAddress };

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { email, password },
            TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        // sanity: the cookie must authenticate a demanding REST read too
        var me = await client.GetAsync("/api/v1/auth/me", TestContext.Current.CancellationToken);
        me.StatusCode.ShouldBe(HttpStatusCode.OK, cookieContainer.Count > 0
            ? "cookie captured but /auth/me refused it"
            : "no cookie captured by the container");

        return cookieContainer.GetCookieHeader(baseAddress);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (application is not null)
        {
            await application.DisposeAsync();
        }

        controlPlane?.Dispose();
        await container.DisposeAsync();

        if (!detailedErrorsPreviouslySet)
        {
            Environment.SetEnvironmentVariable(RealtimeExtensions.DetailedErrorsEnvVar, null);
        }
    }
}

/// <summary>Bootstrap helpers for the realtime host fixture.</summary>
file static class HostRealtimeBootstrap
{
    /// <summary>Creates the member account with no role assignments — every permission check denies.</summary>
    public static async Task CreateMemberAccountAsync(IServiceProvider services)
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = services.CreateAsyncScope();
        var createUser = scope.ServiceProvider.GetRequiredService<CreateUserHandler>();
        await createUser.HandleAsync(
            new CreateUserCommand(
                HostRealtimeServer.MemberEmail,
                HostRealtimeServer.MemberEmail,
                HostRealtimeServer.MemberPassword),
            cancellationToken);
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Comuki.Engine.Orchestration.Infrastructure;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Modules.Chat.Infrastructure.Persistence;
using Comuki.Modules.Identity.Infrastructure.Persistence;
using Comuki.Modules.Intake.Application.Ports;
using Comuki.Modules.Intake.Domain.Connections;
using Comuki.Modules.Intake.Infrastructure.Persistence;
using Comuki.Modules.Projects.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using Testcontainers.PostgreSql;
using Xunit;

namespace Comuki.Host.Integration.Intake;

/// <summary>
/// Boots the real host composition (<see cref="HostComposer"/>) on a
/// random loopback port against one migrated Testcontainers Postgres —
/// all six module contexts — with a fast bridge interval and a FAKE
/// GitHub sync port pre-registered (the registry resolves first-match,
/// so the fake shadows the real Refit client; no tracker HTTP in tests).
/// One shared instance per test run (collection fixture): every test
/// scopes itself by its own project id / delivery ids.
/// </summary>
public sealed class HostIntakeServer : IAsyncLifetime
{
    public const string BootstrapEmail = "bootstrap@comuki.test";
    public const string BootstrapPassword = "bootstrap-pass-1";
    public const string HookSecretEnv = "COMUKI_TEST_GH_HOOK";
    public const string HookSecret = "test-hook-secret";

    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    private WebApplication application = null!;
    private Uri baseAddress = null!;

    public FakeGithubSyncPort GithubSync { get; } = new();

    /// <summary>The database connection string (direct context access for asserts).</summary>
    public string ConnectionString { get; private set; } = string.Empty;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await container.StartAsync(cancellationToken);

        ConnectionString = container.GetConnectionString();
        await MigrateAsync<OrchestrationDbContext>(OrchestrationDbContext.ApplyOptions, cancellationToken);
        await MigrateAsync<IdentityDbContext>(IdentityDbContext.ApplyOptions, cancellationToken);
        await MigrateAsync<ProjectsDbContext>(ProjectsDbContext.ApplyOptions, cancellationToken);
        await MigrateAsync<ChatDbContext>(ChatDbContext.ApplyOptions, cancellationToken);
        await MigrateAsync<IntakeDbContext>(IntakeDbContext.ApplyOptions, cancellationToken);

        Environment.SetEnvironmentVariable(HookSecretEnv, HookSecret, EnvironmentVariableTarget.Process);

        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { ApplicationName = typeof(HostComposer).Assembly.GetName().Name });
        builder.WebHost.UseUrls($"http://127.0.0.1:{FreeTcpPort()}");
        builder.Logging.ClearProviders();
        builder.Configuration["auth:bootstrap:adminEmail"] = BootstrapEmail;
        builder.Configuration["auth:bootstrap:adminPassword"] = BootstrapPassword;
        builder.Configuration["Intake:BridgeInterval"] = "00:00:01";
        builder.Configuration["Intake:SyncBackoff"] = "00:00:01";

        // Program wires orchestration persistence before Compose (the
        // worker runtime contract) — the intake endpoints resolve scoped
        // orchestration services through the same wiring.
        builder.Services.AddOrchestrationPersistence(ConnectionString);

        // The fake sync port pre-registers BEFORE Compose so the provider
        // registry's first-match resolution shadows the real GitHub client.
        builder.Services.AddSingleton<ITicketSyncPort>(GithubSync);

        application = HostComposer.Compose(builder, HostDatabase.Explicit(ConnectionString));
        await application.StartAsync(cancellationToken);

        baseAddress = new Uri(
            application.Services
                .GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
                .Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!
                .Addresses.Single());
    }

    /// <summary>Cookie-carrying browser client logged in as the bootstrap admin.</summary>
    /// <returns>Logged-in client.</returns>
    public async Task<HttpClient> CreateBrowserClientAsync()
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

    /// <summary>Cookie-less anonymous client.</summary>
    /// <returns>Anonymous client.</returns>
    public HttpClient CreateAnonymousClient()
    {
        return new HttpClient { BaseAddress = baseAddress };
    }

    /// <summary>One fresh orchestration context for direct asserts.</summary>
    /// <returns></returns>
    public OrchestrationDbContext CreateOrchestrationDb()
    {
        var options = new DbContextOptionsBuilder<OrchestrationDbContext>();
        OrchestrationDbContext.ApplyOptions(options, ConnectionString);
        return new OrchestrationDbContext(options.Options);
    }

    /// <summary>One fresh intake context for direct asserts.</summary>
    /// <returns></returns>
    public IntakeDbContext CreateIntakeDb()
    {
        var options = new DbContextOptionsBuilder<IntakeDbContext>();
        IntakeDbContext.ApplyOptions(options, ConnectionString);
        return new IntakeDbContext(options.Options);
    }

    /// <summary>Polls until the condition holds or the timeout expires.</summary>
    /// <param name="condition"></param>
    /// <param name="timeout"></param>
    public static async Task WaitForAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        throw new TimeoutException("condition not met within " + timeout);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await application.DisposeAsync();
        await container.DisposeAsync();
    }

    private async Task MigrateAsync<TContext>(
        Action<DbContextOptionsBuilder, string> applyOptions,
        CancellationToken cancellationToken)
        where TContext : DbContext
    {
        var options = new DbContextOptionsBuilder<TContext>();
        applyOptions(options, ConnectionString);

        // Activator.CreateInstance(Type, args) cannot bind the optional
        // scope-accessor ctor parameter the filtered contexts take; direct
        // construction (no accessor) is system semantics — what a migration
        // pass needs. One reflective cast keeps the generic call sites.
        var constructor = typeof(TContext).GetConstructors().OrderByDescending(static ctor => ctor.GetParameters().Length).First();
        object?[] arguments = constructor.GetParameters().Length switch
        {
            1 => [options.Options],
            2 => [options.Options, null],
            _ => throw new InvalidOperationException("unexpected ctor arity on " + typeof(TContext).Name),
        };
        var context = (TContext)constructor.Invoke(arguments);
        await using (context)
        {
            await context.Database.MigrateAsync(cancellationToken);
        }
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
/// In-memory GitHub sync port — records every transition pushed by the
/// outbox drainer; no HTTP leaves the process.
/// </summary>
public sealed class FakeGithubSyncPort : ITicketSyncPort
{
    private readonly Lock gate = new();

    public List<TicketTransition> Transitions { get; } = [];

    public string SourceKey => "github";

    public Task TransitionAsync(SourceConnection connection, TicketTransition transition, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            Transitions.Add(transition);
        }

        return Task.CompletedTask;
    }
}

/// <summary>Fixture payloads + helpers.</summary>
public static class HostIntakeFiles
{
    public static async Task<byte[]> ReadFixtureAsync(string fileName)
    {
        return await File.ReadAllBytesAsync(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName),
            TestContext.Current.CancellationToken);
    }

    public static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        return JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement;
    }
}

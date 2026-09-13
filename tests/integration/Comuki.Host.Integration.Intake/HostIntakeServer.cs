using System.Text.Json;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Host.Testing;
using Comuki.Modules.Intake.Application.Ports.Sync;
using Comuki.Modules.Intake.Application.Ports.Tickets;
using Comuki.Modules.Intake.Domain.Connections;
using Comuki.Modules.Intake.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace Comuki.Host.Integration.Intake;

/// <summary>
/// Boots the real host composition (<see cref="HostComposer"/>) on a
/// random loopback port against one migrated Testcontainers Postgres —
/// every module context, via <see cref="HostDatabaseMigrator"/> — with a
/// fast bridge interval and a FAKE GitHub sync port pre-registered (the
/// registry resolves first-match, so the fake shadows the real Refit
/// client; no tracker HTTP in tests). One shared instance per test run
/// (collection fixture): every test scopes itself by its own project id /
/// delivery ids.
/// </summary>
public sealed class HostIntakeServer : IAsyncLifetime
{
    public const string BootstrapEmail = TestBootstrapAdmin.Email;
    public const string BootstrapPassword = TestBootstrapAdmin.Password;
    public const string HookSecretEnv = "COMUKI_TEST_GH_HOOK";
    public const string HookSecret = "test-hook-secret";

    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("pgvector/pgvector:pg16")
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
        await HostDatabaseMigrator.MigrateAllAsync(ConnectionString, cancellationToken);

        Environment.SetEnvironmentVariable(HookSecretEnv, HookSecret, EnvironmentVariableTarget.Process);

        var builder = TestHostBuilder.Create(ConnectionString);
        TestBootstrapAdmin.Configure(builder.Configuration);
        builder.Configuration["Intake:BridgeInterval"] = "00:00:01";
        builder.Configuration["Intake:SyncBackoff"] = "00:00:01";
        // Lift rate-limit partitions for the integration run.
        builder.Configuration["Host:RateLimit:LoginPermitsPerMinute"] = "10000";
        builder.Configuration["Host:RateLimit:ApiPermitsPerMinute"] = "100000";
        builder.Configuration["Host:RateLimit:RunDecisionPermitsPerMinute"] = "10000";
        builder.Configuration["Host:RateLimit:OidcStartPermitsPerMinute"] = "10000";
        TestArtifactsSecrets.ApplyPlaceholder(builder.Configuration);

        // The fake sync port pre-registers BEFORE Compose so the provider
        // registry's first-match resolution shadows the real GitHub client.
        builder.Services.AddSingleton<ITicketSyncPort>(GithubSync);

        application = HostComposer.Compose(builder, HostDatabase.Explicit(ConnectionString));
        baseAddress = await TestHostBuilder.StartAsync(application, cancellationToken);
    }

    /// <summary>Cookie-carrying browser client logged in as the bootstrap admin.</summary>
    /// <returns>Logged-in client.</returns>
    public Task<HttpClient> CreateBrowserClientAsync()
    {
        var client = new HttpClient(new HttpClientHandler { UseCookies = true, CheckCertificateRevocationList = true })
        {
            BaseAddress = baseAddress,
        };

        return client.LoginAsBootstrapAdminAsync(TestContext.Current.CancellationToken);
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

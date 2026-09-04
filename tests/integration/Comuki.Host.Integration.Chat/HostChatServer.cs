using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using Comuki.Engine.Orchestration.Infrastructure;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Modules.Chat.Infrastructure.Persistence;
using Comuki.Modules.Identity.Infrastructure.Persistence;
using Comuki.Modules.Projects.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shouldly;
using Testcontainers.PostgreSql;
using Xunit;

namespace Comuki.Host.Integration.Chat;

/// <summary>
/// Boots the real host composition (<see cref="HostComposer"/>) on a
/// random loopback port against one migrated Testcontainers Postgres — all
/// four module contexts (orchestration, identity, projects, chat), a temp
/// control-plane root with one chat command, and the bootstrap admin for
/// cookie login. The brain runs as the in-process stub and the memory
/// digest as the empty fallback — the exact composition production boots
/// until the brain-host and memory-store slices land.
/// </summary>
public sealed class HostChatServer : IAsyncLifetime
{
    public const string BootstrapEmail = "bootstrap@comuki.test";
    public const string BootstrapPassword = "bootstrap-pass-1";

    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    private WebApplication application = null!;
    private TempControlPlaneRoot controlPlane = null!;
    private Uri baseAddress = null!;

    /// <summary>The database connection string (direct context access for asserts).</summary>
    public string ConnectionString { get; private set; } = string.Empty;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await container.StartAsync(cancellationToken);

        ConnectionString = container.GetConnectionString();

        // The migrator's contract: every module context migrates the same
        // database, each with its own migrations history table.
        var orchestrationOptions = new DbContextOptionsBuilder<OrchestrationDbContext>();
        OrchestrationDbContext.ApplyOptions(orchestrationOptions, ConnectionString);
        await using (var orchestrationDb = new OrchestrationDbContext(orchestrationOptions.Options))
        {
            await orchestrationDb.Database.MigrateAsync(cancellationToken);
        }

        var identityOptions = new DbContextOptionsBuilder<IdentityDbContext>();
        IdentityDbContext.ApplyOptions(identityOptions, ConnectionString);
        await using (var identityDb = new IdentityDbContext(identityOptions.Options))
        {
            await identityDb.Database.MigrateAsync(cancellationToken);
        }

        var projectsOptions = new DbContextOptionsBuilder<ProjectsDbContext>();
        ProjectsDbContext.ApplyOptions(projectsOptions, ConnectionString);
        await using (var projectsDb = new ProjectsDbContext(projectsOptions.Options))
        {
            await projectsDb.Database.MigrateAsync(cancellationToken);
        }

        var chatOptions = new DbContextOptionsBuilder<ChatDbContext>();
        ChatDbContext.ApplyOptions(chatOptions, ConnectionString);
        await using (var chatDb = new ChatDbContext(chatOptions.Options))
        {
            await chatDb.Database.MigrateAsync(cancellationToken);
        }

        controlPlane = new TempControlPlaneRoot();
        controlPlane.WriteChatCommand();

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
        builder.Configuration["ControlPlane:Root"] = controlPlane.Root;
        builder.Configuration["auth:bootstrap:adminEmail"] = BootstrapEmail;
        builder.Configuration["auth:bootstrap:adminPassword"] = BootstrapPassword;
        // Artifacts module — non-dev-default secrets so the production-secret
        // validator (issue #10 T11.4) passes through. See HostIntakeServer
        // for the rationale.
        builder.Configuration["Artifacts:Endpoint"] = "minio:9000";
        builder.Configuration["Artifacts:AccessKey"] = "test-access-key";
        builder.Configuration["Artifacts:SecretKey"] = "test-secret-key-with-enough-entropy";
        builder.Configuration["Artifacts:Bucket"] = "comuki-test-bundles";

        // Program wires orchestration persistence before Compose (the worker
        // runtime contract) — the chat endpoints resolve scoped orchestration
        // services, so the fixture mirrors that wiring.
        builder.Services.AddOrchestrationPersistence(ConnectionString);

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

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await application.DisposeAsync();
        controlPlane.Dispose();
        await container.DisposeAsync();
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

/// <summary>Throwaway control-plane root with one chat command the slash catalog merges.</summary>
internal sealed class TempControlPlaneRoot : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "comuki-host-chat-" + Guid.NewGuid().ToString("N"));

    public void WriteChatCommand()
    {
        var directory = Path.Combine(Root, "chat-commands");
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, "restart.md"),
            """
            ---
            name: restart
            description: Restart the current run.
            ---

            Restart the current run now.
            """,
            new UTF8Encoding(false));
    }

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}

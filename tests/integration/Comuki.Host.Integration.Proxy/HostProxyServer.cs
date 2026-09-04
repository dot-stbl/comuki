using System.Net;
using System.Net.Sockets;
using Comuki.Engine.Orchestration.Application;
using Comuki.Engine.Orchestration.Infrastructure;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Modules.Costs.Infrastructure.Persistence;
using Comuki.Modules.Identity.Infrastructure.Persistence;
using Comuki.Modules.Projects.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;
using Xunit;

namespace Comuki.Host.Integration.Proxy;

/// <summary>
/// Boots the full host composition (including the YARP proxy module
/// from issue #8) on a loopback port against a migrated Testcontainers
/// Postgres. The proxy upstreams are pointed at an in-process fake
/// HTTP listener so the suite never reaches the real OpenAI / Anthropic
/// endpoints.
/// </summary>
public sealed class HostProxyServer : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    internal WebApplication Application { get; private set; } = null!;
    internal FakeUpstreamServer FakeUpstream { get; private set; } = null!;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await container.StartAsync(cancellationToken);
        var connectionString = container.GetConnectionString();

        await MigrateAsync(connectionString, cancellationToken);

        FakeUpstream = new FakeUpstreamServer();
        await FakeUpstream.StartAsync();

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(HostComposer).Assembly.GetName().Name,
            EnvironmentName = Environments.Production,
        });
        builder.WebHost.UseUrls($"http://127.0.0.1:{FreeTcpPort()}");
        builder.Logging.ClearProviders();
        _ = builder.Logging.AddSimpleConsole(static options => { options.IncludeScopes = true; });

        // Production-secret gate (issue #10 T11.4) needs non-dev-defaults.
        builder.Configuration["Artifacts:Endpoint"] = "minio:9000";
        builder.Configuration["Artifacts:AccessKey"] = "test-access-key";
        builder.Configuration["Artifacts:SecretKey"] = "test-secret-key-with-enough-entropy";
        builder.Configuration["Artifacts:Bucket"] = "comuki-test-bundles";

        // Enable the proxy with a single virtual key pointing at the
        // in-process fake upstream; the upstream API key is sourced
        // from FAKE_OPENAI_KEY which the fake server trusts.
        builder.Configuration["Proxy:Enabled"] = "true";
        builder.Configuration["Proxy:KnownModels:0"] = "gpt-4o-mini";
        builder.Configuration["Proxy:Pricing:DefaultPricing:InputUsdPerMillion"] = "3";
        builder.Configuration["Proxy:Pricing:DefaultPricing:OutputUsdPerMillion"] = "15";
        Environment.SetEnvironmentVariable("FAKE_OPENAI_KEY", "sk-fake");
        builder.Configuration["Proxy:VirtualKeys:0:Token"] = "vkey_test_alpha";
        builder.Configuration["Proxy:VirtualKeys:0:ProjectId"] = Guid.NewGuid().ToString();
        builder.Configuration["Proxy:VirtualKeys:0:Provider"] = "openai";
        builder.Configuration["Proxy:VirtualKeys:0:BaseUrl"] = FakeUpstream.BaseAddress.ToString();
        builder.Configuration["Proxy:VirtualKeys:0:ApiKeyEnvRef"] = "FAKE_OPENAI_KEY";

        _ = builder.Services.AddOrchestrationPersistence(connectionString);
        _ = builder.Services.AddOrchestrationApplication();

        // The artifact packager BackgroundService polls every 10s on
        // the same Postgres pool the test's HTTP request uses; one cycle
        // racing with the proxy call shows up as a Npgsql "command
        // already in progress" and a 500. Strip it for the proxy suite.
        var packagerDescriptors = builder.Services
            .Where(static descriptor => descriptor.ServiceType == typeof(IHostedService))
            .ToList();
        foreach (var descriptor in packagerDescriptors)
        {
            _ = builder.Services.Remove(descriptor);
        }

        Application = HostComposer.Compose(builder, HostDatabase.Explicit(connectionString));
        await Application.StartAsync(cancellationToken);

        BaseAddress = new Uri(Application.Services
            .GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
            .Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!
            .Addresses.Single());
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Application is not null)
        {
            await Application.DisposeAsync();
        }

        if (FakeUpstream is not null)
        {
            await FakeUpstream.DisposeAsync();
        }

        if (container is not null)
        {
            await container.DisposeAsync();
        }
    }

    /// <summary>Base address the host listens on (where requests go).</summary>
    public Uri BaseAddress { get; private set; } = null!;

    /// <summary>HTTP client pointed at the host; per-test.</summary>
    public HttpClient CreateClient()
    {
        return new HttpClient { BaseAddress = BaseAddress };
    }

    private static async Task MigrateAsync(string connectionString, CancellationToken cancellationToken)
    {
        var orchestrationOptions = new DbContextOptionsBuilder<OrchestrationDbContext>();
        OrchestrationDbContext.ApplyOptions(orchestrationOptions, connectionString);
        await using var orchestrationDb = new OrchestrationDbContext(orchestrationOptions.Options);
        await orchestrationDb.Database.MigrateAsync(cancellationToken);

        var identityOptions = new DbContextOptionsBuilder<IdentityDbContext>();
        IdentityDbContext.ApplyOptions(identityOptions, connectionString);
        await using var identityDb = new IdentityDbContext(identityOptions.Options);
        await identityDb.Database.MigrateAsync(cancellationToken);

        var projectsOptions = new DbContextOptionsBuilder<ProjectsDbContext>();
        ProjectsDbContext.ApplyOptions(projectsOptions, connectionString);
        await using var projectsDb = new ProjectsDbContext(projectsOptions.Options);
        await projectsDb.Database.MigrateAsync(cancellationToken);

        var costsOptions = new DbContextOptionsBuilder<CostsDbContext>();
        CostsDbContext.ApplyOptions(costsOptions, connectionString);
        await using var costsDb = new CostsDbContext(costsOptions.Options);
        await costsDb.Database.MigrateAsync(cancellationToken);
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

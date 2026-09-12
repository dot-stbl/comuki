using Comuki.Engine.Orchestration.Application;
using Comuki.Host.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;
using Xunit;

namespace Comuki.Host.Integration.Proxy;

/// <summary>
/// Boots the full host composition (including the YARP proxy module
/// from issue #8) on a loopback port against one migrated Testcontainers
/// Postgres — every module context, via <see cref="HostDatabaseMigrator"/>.
/// The proxy upstreams are pointed at an in-process fake HTTP listener so
/// the suite never reaches the real OpenAI / Anthropic endpoints.
/// </summary>
public sealed class HostProxyServer : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("pgvector/pgvector:pg16")
        .Build();

    internal WebApplication Application { get; private set; } = null!;
    internal FakeUpstreamServer FakeUpstream { get; private set; } = null!;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await container.StartAsync(cancellationToken);
        var connectionString = container.GetConnectionString();

        await HostDatabaseMigrator.MigrateAllAsync(connectionString, cancellationToken);

        FakeUpstream = new FakeUpstreamServer();
        await FakeUpstream.StartAsync();

        var builder = TestHostBuilder.Create(connectionString);
        builder.Logging.AddSimpleConsole(static options => { options.IncludeScopes = true; });
        TestArtifactsSecrets.ApplyPlaceholder(builder.Configuration);

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

        builder.Services.AddOrchestrationApplication();

        // The artifact packager BackgroundService polls every 10s on
        // the same Postgres pool the test's HTTP request uses; one cycle
        // racing with the proxy call shows up as a Npgsql "command
        // already in progress" and a 500. Strip it for the proxy suite.
        var packagerDescriptors = builder.Services
            .Where(static descriptor => descriptor.ServiceType == typeof(IHostedService))
            .ToList();
        foreach (var descriptor in packagerDescriptors)
        {
            builder.Services.Remove(descriptor);
        }

        Application = HostComposer.Compose(builder, HostDatabase.Explicit(connectionString));
        BaseAddress = await TestHostBuilder.StartAsync(Application, cancellationToken);
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

        await container.DisposeAsync();
    }

    /// <summary>Base address the host listens on (where requests go).</summary>
    public Uri BaseAddress { get; private set; } = null!;

    /// <summary>HTTP client pointed at the host; per-test.</summary>
    public HttpClient CreateClient()
    {
        return new HttpClient { BaseAddress = BaseAddress };
    }
}

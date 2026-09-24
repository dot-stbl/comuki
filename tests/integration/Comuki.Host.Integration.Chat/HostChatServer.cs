using Comuki.Host.Testing;
using Comuki.Host.Testing.Fixtures;
using Microsoft.AspNetCore.Builder;
using Xunit;

namespace Comuki.Host.Integration.Chat;

/// <summary>
/// Boots the real host composition (<see cref="HostComposer"/>) on a
/// random loopback port against one shared, migrated Postgres (owned by
/// this type's own <see cref="PostgresCollectionFixture"/> — one container
/// for the whole test class, not rebuilt per test), a temp control-plane
/// root with one chat command, and the bootstrap admin for cookie login.
/// The brain runs as the in-process stub; the memory digest is the real
/// memory-module adapter over the migrated memory schema — the exact
/// composition production boots.
/// </summary>
public sealed class HostChatServer : IAsyncLifetime
{
    public const string BootstrapEmail = TestBootstrapAdmin.Email;
    public const string BootstrapPassword = TestBootstrapAdmin.Password;

    private readonly PostgresCollectionFixture postgres = new();

    private WebApplication application = null!;
    private TempControlPlaneRoot controlPlane = null!;
    private Uri baseAddress = null!;

    /// <summary>The database connection string (direct context access for asserts).</summary>
    public string ConnectionString { get; private set; } = string.Empty;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await postgres.InitializeAsync();
        ConnectionString = postgres.ConnectionString;

        controlPlane = new TempControlPlaneRoot("chat");
        controlPlane.WriteDefaultChatCommand();

        var builder = TestHostBuilder.Create(ConnectionString);
        builder.Configuration["ControlPlane:Root"] = controlPlane.Root;
        TestBootstrapAdmin.Configure(builder.Configuration);
        TestArtifactsSecrets.ApplyPlaceholder(builder.Configuration);

        application = await HostComposer.ComposeAsync(builder, HostDatabase.Explicit(ConnectionString));
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

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await application.DisposeAsync();
        controlPlane.Dispose();
        await postgres.DisposeAsync();
    }
}

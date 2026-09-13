using Comuki.Host.Testing;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace Comuki.Host.Integration.Oidc;

/// <summary>
/// Boots the real host composition (<see cref="HostComposer"/>) against a
/// migrated Testcontainers Postgres (every module context, via
/// <see cref="HostDatabaseMigrator"/>), with a real Keycloak (realm
/// <c>comuki</c> imported from the same
/// <c>deploy/keycloak/comuki-realm.json</c> the compose profile uses) as
/// the configured OIDC provider.
/// <para>
/// <b>Layers covered</b> (issue #12 tail, pragmatic contract): keycloak
/// discovery document; our <c>AddOpenIdConnect</c> handler constructing
/// the authorize redirect (PKCE S256, correct client and callback) for
/// <c>/auth/oidc/keycloak/start</c>; token issuance via the password
/// grant; the real userinfo claims; and the <c>OidcAccountLinker</c>
/// resolving those claims to a local account row (provision → link).
/// NOT covered: the browser callback exchange (state/nonce cookies,
/// code-for-token on our callback path) — that needs a real browser or
/// heavy session plumbing and stays with the dashboard slice.
/// </para>
/// </summary>
public sealed class HostOidcServer : IAsyncLifetime
{
    public const string ProviderName = "keycloak";
    public const string ClientId = "comuki-dashboard";
    public const string ClientSecretEnv = "COMUKI_TEST_OIDC_CLIENT_SECRET";
    public const string TestUsername = "test-user";
    public const string TestPassword = "test-pass-123";
    public const string TestEmail = "test-user@comuki.test";

    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("pgvector/pgvector:pg16")
        .Build();
    [Obsolete]
    private readonly IContainer keycloak = new ContainerBuilder()
        .WithImage("quay.io/keycloak/keycloak:26.2.5")
        .WithCommand("start-dev", "--import-realm")
        .WithEnvironment("KC_BOOTSTRAP_ADMIN_USERNAME", "admin")
        .WithEnvironment("KC_BOOTSTRAP_ADMIN_PASSWORD", "admin")
        .WithResourceMapping(
            new FileInfo(Path.Combine(AppContext.BaseDirectory, "comuki-realm.json")),
            "/opt/keycloak/data/import")
        .WithPortBinding(8080, assignRandomHostPort: true)
        .WithWaitStrategy(Wait.ForUnixContainer()
            .UntilHttpRequestIsSucceeded(static request => request.ForPort(8080).ForPath("/realms/comuki")))
        .Build();

    private WebApplication application = null!;
    private Uri baseAddress = null!;

    /// <summary>The keycloak realm authority the host is configured with.</summary>
    public string Authority { get; private set; } = string.Empty;

    /// <summary>The composed host's DI container — direct resolution for non-HTTP checks (DB seed, hosted services).</summary>
    public IServiceProvider Services => application.Services;

    /// <summary>Anonymous client that does NOT follow redirects — the start endpoint's 302 is the assertion target.</summary>
    public HttpClient CreateNoRedirectClient()
    {
        return new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, CheckCertificateRevocationList = true })
        {
            BaseAddress = baseAddress,
        };
    }

    /// <summary>Plain client for anonymous calls.</summary>
    public HttpClient CreateClient()
    {
        return new HttpClient(new HttpClientHandler { CheckCertificateRevocationList = true })
        {
            BaseAddress = baseAddress,
        };
    }

    /// <summary>Runs the account linker against the host's own stores.</summary>
    /// <param name="subject"></param>
    /// <param name="email"></param>
    /// <param name="displayName"></param>
    /// <param name="cancellationToken"></param>
    public async Task<Modules.Identity.Application.Oidc.OidcLinkResult> LinkAsync(
        string subject,
        string email,
        string? displayName,
        CancellationToken cancellationToken = default)
    {
        using var scope = application.Services.CreateScope();
        var linker = scope.ServiceProvider.GetRequiredService<Modules.Identity.Application.Oidc.OidcAccountLinker>();

        return await linker.HandleAsync(
            new Modules.Identity.Application.Oidc.OidcLinkRequest(ProviderName, subject, email, displayName, EmailVerified: true),
            cancellationToken);
    }

    /// <inheritdoc />
    [Obsolete]
    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await postgres.StartAsync(cancellationToken);
        await keycloak.StartAsync(cancellationToken);

        Authority = $"http://localhost:{keycloak.GetMappedPublicPort(8080)}/realms/comuki";

        var connectionString = postgres.GetConnectionString();
        await HostDatabaseMigrator.MigrateAllAsync(connectionString, cancellationToken);

        Environment.SetEnvironmentVariable(ClientSecretEnv, "test-client-secret");

        var builder = TestHostBuilder.Create(connectionString);
        TestBootstrapAdmin.Configure(builder.Configuration);
        builder.Configuration["auth:oidc:providers:0:Name"] = ProviderName;
        builder.Configuration["auth:oidc:providers:0:Authority"] = Authority;
        builder.Configuration["auth:oidc:providers:0:ClientId"] = ClientId;
        builder.Configuration["auth:oidc:providers:0:ClientSecretEnv"] = ClientSecretEnv;
        builder.Configuration["auth:oidc:providers:0:RequireHttps"] = "false";
        TestArtifactsSecrets.ApplyPlaceholder(builder.Configuration);

        // The .NET 10 handler opportunistically switches to Pushed
        // Authorization Requests when the discovery document advertises the
        // PAR endpoint (keycloak does) — and PAR validates the callback
        // against the client's registered redirect URIs at push time. This
        // fixture's host sits on a random loopback port that cannot be
        // pre-registered, so the classic front-channel authorize redirect is
        // asserted instead: PAR stays on in real deployments.
        builder.Services.AddSingleton<
            Microsoft.Extensions.Options.IPostConfigureOptions<Microsoft.AspNetCore.Authentication.OpenIdConnect.OpenIdConnectOptions>,
            NoPushedAuthorizationPostConfigure>();

        application = HostComposer.Compose(builder, HostDatabase.Explicit(connectionString));
        baseAddress = await TestHostBuilder.StartAsync(application, cancellationToken);
    }

    /// <inheritdoc />
    [Obsolete]
    public async ValueTask DisposeAsync()
    {
        Environment.SetEnvironmentVariable(ClientSecretEnv, null);
        await application.DisposeAsync();
        await keycloak.DisposeAsync();
        await postgres.DisposeAsync();
    }
}

/// <summary>
/// Skips Pushed Authorization Requests for the test provider only: the
/// fixture's random loopback port cannot be pre-registered in the realm,
/// and PAR rejects unregistered callbacks at push time.
/// </summary>
file sealed class NoPushedAuthorizationPostConfigure
    : Microsoft.Extensions.Options.IPostConfigureOptions<Microsoft.AspNetCore.Authentication.OpenIdConnect.OpenIdConnectOptions>
{
    public void PostConfigure(string? name, Microsoft.AspNetCore.Authentication.OpenIdConnect.OpenIdConnectOptions options)
    {
        if (name == Modules.Identity.Infrastructure.Security.AuthSchemes.Oidc(HostOidcServer.ProviderName))
        {
            // registered before HostComposer's own post-configure, which then
            // chains its ticket-received hook onto this instance
            options.Events = new SkipParEvents();
        }
    }
}

/// <summary>Events subclass that always skips the PAR push.</summary>
file sealed class SkipParEvents : Microsoft.AspNetCore.Authentication.OpenIdConnect.OpenIdConnectEvents
{
    public override Task PushAuthorization(Microsoft.AspNetCore.Authentication.OpenIdConnect.PushedAuthorizationContext context)
    {
        context.SkipPush();

        return Task.CompletedTask;
    }
}

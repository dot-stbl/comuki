using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Comuki.Host.Testing;
using Comuki.Host.Testing.Fixtures;
using Comuki.Shared.Editions.Gating;
using Comuki.Shared.Editions.Installers;
using Comuki.Shared.Editions.Licensing;
using Comuki.Shared.Editions.Licensing.Ed25519;
using Comuki.Shared.Editions.Licensing.Grants;
using Comuki.Shared.Editions.Licensing.Modes;
using Comuki.Shared.Editions.Tiers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace Comuki.Host.Integration.Auth;

/// <summary>
/// End-to-end editions-gate assertion against the booted host composition
/// with a fresh, paid (Team-tier) Ed25519-signed license in play —
/// <c>[EnforceLimit(&quot;projects&quot;)]</c> on <c>POST /api/v1/projects</c>
/// permits more than Community's 1, and the test-only
/// <c>[RequiresFeature(&quot;multi-repo&quot;)]</c> sample endpoint
/// answers 200.
/// <para>
/// The license is signed inline against a freshly generated keypair (the
/// production verifying key's matching private key was deliberately
/// discarded — see <c>ProductionEd25519PublicKey.cs</c>); a TEST-only
/// <see cref="ILicenseProvider"/> registered before
/// <see cref="HostComposer.ComposeAsync"/> wins over the production
/// registration because <see cref="ComukiEditionsInstaller.AddComukiEditions"/>
/// uses <c>TryAddSingleton</c> for that exact reason. The Team-tier
/// grant in <see cref="LicenseMode.ImplicitByRank"/> mode covers every
/// declared <c>Features.*</c> / <c>Limits.*</c> entry whose
/// <c>MinimumRank &lt;= 1</c> (which today is all of them, including
/// <c>Limits.Projects</c>'s Team cap of 10 and the
/// <c>&quot;multi-repo&quot;</c> feature the sample endpoint checks).
/// </para>
/// <para>
/// Each fixture instance mints its own keypair and stores its token in
/// a uniquely-named env var so parallel runs in the same process never
/// see each other's <c>Host:License:Path</c>.
/// </para>
/// </summary>
public sealed class EditionsPaidGatingShould(PaidEditionFixture fixture) : IClassFixture<PaidEditionFixture>
{
    [Fact(DisplayName = "Given a paid (Team) edition, when a second project is created via the real endpoint, then both succeed (201)")]
    public async Task PaidEditionAllowsTwoProjectsAsync()
    {
        using var client = await fixture.CreateAdminClientAsync();

        var first = await client.PostAsJsonAsync(
            "/api/v1/projects",
            new { name = "First", slug = $"first-{Guid.NewGuid():N}" },
            TestContext.Current.CancellationToken);

        first.StatusCode.ShouldBe(HttpStatusCode.Created, await first.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var second = await client.PostAsJsonAsync(
            "/api/v1/projects",
            new { name = "Second", slug = $"second-{Guid.NewGuid():N}" },
            TestContext.Current.CancellationToken);

        second.StatusCode.ShouldBe(HttpStatusCode.Created, await second.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact(DisplayName = "Given a paid (Team) edition, when the test-only sample endpoint is called, then it answers 200")]
    public async Task RequiresFeatureAllowsAnonymousOnPaidAsync()
    {
        using var client = fixture.CreateApiKeyClient();

        var response = await client.GetAsync("/api/v1/_test/editions-sample", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        payload.GetProperty("ok").GetBoolean().ShouldBeTrue();
    }
}

/// <summary>
/// Boots <see cref="HostComposer"/> with a freshly minted Team-tier
/// Ed25519-signed license configured on the builder before composition,
/// plus one test-only <c>[RequiresFeature(&quot;multi-repo&quot;)]</c>
/// endpoint added to the returned <see cref="WebApplication"/> before
/// starting it. Each fixture instance owns one isolated Postgres
/// container, parallel with the Auth suite's shared one and with the
/// Community fixture's own.
/// </summary>
public sealed class PaidEditionFixture : IAsyncLifetime
{
    private const string SampleEndpointRoute = "/api/v1/_test/editions-sample";

    private readonly PostgresCollectionFixture postgres = new();

    private readonly string licenseEnvVar = $"COMUKI_TEST_LICENSE_{Guid.NewGuid():N}";

    private WebApplication application = null!;
    private TempControlPlaneRoot controlPlane = null!;
    private Uri baseAddress = null!;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await postgres.InitializeAsync();
        var connectionString = postgres.ConnectionString;

        // Mint the token BEFORE ComposeAsync so the ILicenseProvider
        // can register against the matching public key — TryAddSingleton
        // keeps the test-registration winning over the production one.
        var (publicKey, privateKeySeed) = Ed25519LicenseSigner.GenerateKeyPair();
        var token = Ed25519LicenseSigner.Sign(
            new LicenseGrant(
                Org: "Integration Test",
                Tier: EditionTiers.Team,
                Expiry: DateTimeOffset.UtcNow.AddYears(1),
                Mode: LicenseMode.ImplicitByRank,
                NotBefore: null,
                Features: null,
                Limits: null),
            privateKeySeed);

        controlPlane = new TempControlPlaneRoot("editions-paid");
        controlPlane.WriteDefaultChatCommand();

        var builder = TestHostBuilder.Create(connectionString);
        builder.Logging.AddSimpleConsole(static options => options.IncludeScopes = true);
        builder.Configuration["ControlPlane:Root"] = controlPlane.Root;
        TestBootstrapAdmin.Configure(builder.Configuration);
        TestArtifactsSecrets.ApplyPlaceholder(builder.Configuration);

        builder.Services.AddSingleton<ILicenseProvider>(new Ed25519LicenseProvider(publicKey));
        builder.Configuration["Host:License:Path"] = $"env:{licenseEnvVar}";
        Environment.SetEnvironmentVariable(licenseEnvVar, token);

        application = await HostComposer.ComposeAsync(builder, HostDatabase.Explicit(connectionString));

        application.MapGet(
            SampleEndpointRoute,
            [RequiresFeature("multi-repo")] static () => Results.Ok(new { ok = true }));

        baseAddress = await TestHostBuilder.StartAsync(application, cancellationToken);
    }

    /// <summary>Cookie-carrying client logged in as the bootstrap admin.</summary>
    public Task<HttpClient> CreateAdminClientAsync()
    {
        var client = new HttpClient(new HttpClientHandler { UseCookies = true, CheckCertificateRevocationList = true })
        {
            BaseAddress = baseAddress,
        };

        return client.LoginAsBootstrapAdminAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Cookie-less client for anonymous calls.</summary>
    public HttpClient CreateApiKeyClient()
    {
        return new HttpClient { BaseAddress = baseAddress };
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (application is not null)
        {
            await application.DisposeAsync();
        }

        Environment.SetEnvironmentVariable(licenseEnvVar, null);
        controlPlane?.Dispose();
        await postgres.DisposeAsync();
    }
}

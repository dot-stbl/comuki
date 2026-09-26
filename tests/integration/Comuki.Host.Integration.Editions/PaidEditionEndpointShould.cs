using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Comuki.Host.Testing;
using Comuki.Host.Testing.Fixtures;
using Comuki.Shared.Editions;
using Comuki.Shared.Editions.Licensing;
using Comuki.Shared.Editions.Licensing.Ed25519;
using Comuki.Shared.Editions.Licensing.Grants;
using Comuki.Shared.Editions.Licensing.Modes;
using Comuki.Shared.Editions.Tiers;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace Comuki.Host.Integration.Editions;

/// <summary>
/// End-to-end shape assertion for <c>GET /api/v1/edition</c> against the
/// booted host composition with a fresh, paid (Team-tier) Ed25519-signed
/// license mounted through <c>Host:License:Path</c> (issue #164, change
/// <c>add-editions-and-licensing</c> task 8.5). The endpoint is anonymous
/// by design — the dashboard's first-paint upsell renders before
/// sign-in — so the assertions run through a cookie-less client.
/// <para>
/// The license is signed inline against a freshly generated keypair and
/// mounted via a uniquely-named env var, mirroring
/// <c>EditionsPaidGatingShould</c>'s fixture: the production verifying
/// key's private half was deliberately discarded, and the editions
/// installer's <c>TryAddSingleton</c> lets this test-only
/// <see cref="ILicenseProvider"/> registration win over the production
/// one when it is added before <c>HostComposer.ComposeAsync</c>.
/// </para>
/// <para>
/// The 403 half of the endpoint contract (gated endpoints answering
/// <c>edition.feature_unavailable</c> / <c>edition.limit_exceeded</c>
/// with <c>application/problem+json</c>) is owned byte-level by the Auth
/// suite's <c>EditionsCommunityGatingShould</c> /
/// <c>EditionsPaidGatingShould</c>; this suite owns the read-side view.
/// </para>
/// </summary>
public sealed class PaidEditionEndpointShould(PaidEditionApiFixture fixture) : IClassFixture<PaidEditionApiFixture>
{
    [Fact(DisplayName = "Given a paid (Team) license mounted via Host:License:Path, when GET /api/v1/edition is called anonymously, then the full response shape is byte-correct")]
    public async Task ReturnPaidEditionSnapshotAsync()
    {
        using var client = fixture.CreateAnonymousClient();

        var response = await client.GetAsync("/api/v1/edition", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        payload.GetProperty("tier").GetString().ShouldBe("team");
        payload.GetProperty("status").GetString().ShouldBe("valid");
        payload.GetProperty("version").GetString().ShouldNotBeNullOrEmpty();

        // expiresAt is always present on the wire (required+nullable contract);
        // the paid mount carries the minted one-year expiry.
        var expiresAt = payload.GetProperty("expiresAt");
        expiresAt.ValueKind.ShouldBe(JsonValueKind.String);
        DateTimeOffset.TryParse(expiresAt.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var expiry).ShouldBeTrue();
        expiry.ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddDays(300));
        expiry.ShouldBeLessThan(DateTimeOffset.UtcNow.AddDays(430));

        // Features: every catalog key exactly once, all covered at Team rank.
        var expectedFeatureKeys = Features.All.Select(static feature => feature.Key.Value).ToArray();
        var features = payload.GetProperty("features");
        features.GetArrayLength().ShouldBe(expectedFeatureKeys.Length);
        var wireFeatureKeys = features.EnumerateArray().Select(static row => row.GetProperty("key").GetString()).ToArray();
        wireFeatureKeys.ToHashSet().SetEquals(expectedFeatureKeys).ShouldBeTrue();
        features.EnumerateArray().ShouldAllBe(static row => row.GetProperty("available").GetBoolean());

        // Limits: the projects quota at its Team cap, usage zero on a fresh database.
        var limits = payload.GetProperty("limits");
        var projects = limits.EnumerateArray().Single(static row => row.GetProperty("key").GetString() == "projects");
        projects.GetProperty("current").GetInt32().ShouldBe(0);
        projects.GetProperty("cap").GetInt32().ShouldBe(10);
    }
}

/// <summary>
/// Boots <see cref="HostComposer"/> with a freshly minted Team-tier
/// Ed25519-signed license configured on the builder before composition.
/// Each fixture instance owns one isolated Postgres container, parallel
/// with every other suite's — mirrors <c>PaidEditionFixture</c>.
/// </summary>
public sealed class PaidEditionApiFixture : IAsyncLifetime
{
    private readonly PostgresCollectionFixture postgres = new();

    private readonly string licenseEnvVar = $"COMUKI_TEST_LICENSE_API_{Guid.NewGuid():N}";

    private WebApplication application = null!;
    private TempControlPlaneRoot controlPlane = null!;
    private Uri baseAddress = null!;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await postgres.InitializeAsync();
        var connectionString = postgres.ConnectionString;

        // Mint the token BEFORE ComposeAsync so the ILicenseProvider can
        // register against the matching public key — TryAddSingleton keeps
        // the test registration winning over the production one.
        var (publicKey, privateKeySeed) = Ed25519LicenseSigner.GenerateKeyPair();
        var token = Ed25519LicenseSigner.Sign(
            new LicenseGrant(
                Org: "Editions API Test",
                Tier: EditionTiers.Team,
                Expiry: DateTimeOffset.UtcNow.AddYears(1),
                Mode: LicenseMode.ImplicitByRank,
                NotBefore: null,
                Features: null,
                Limits: null),
            privateKeySeed);

        controlPlane = new TempControlPlaneRoot("editions-api-paid");
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
        baseAddress = await TestHostBuilder.StartAsync(application, cancellationToken);
    }

    /// <summary>Cookie-less client — the endpoint is anonymous by design.</summary>
    public HttpClient CreateAnonymousClient()
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

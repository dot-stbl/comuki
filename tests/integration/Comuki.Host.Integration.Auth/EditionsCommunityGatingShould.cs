using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Comuki.Host.Testing;
using Comuki.Host.Testing.Fixtures;
using Comuki.Shared.Editions.Edition;
using Comuki.Shared.Editions.Gating;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace Comuki.Host.Integration.Auth;

/// <summary>
/// End-to-end editions-gate assertion against the booted host composition
/// with NO license configured — the absent <c>Host:License:Path</c> means
/// <see cref="LicenseEdition"/> falls back to Community (#164, chunk C),
/// so the real <c>[EnforceLimit(&quot;projects&quot;)]</c> gate on
/// <c>POST /api/v1/projects</c> closes at project #1, and the test-only
/// <c>[RequiresFeature(&quot;multi-repo&quot;)]</c> sample endpoint
/// refuses with <c>edition.feature_unavailable</c>.
/// <para>
/// This fixture boots its OWN isolated Postgres container (separate from
/// <see cref="HostAuthServer"/>'s shared one) — <c>ProjectCountLimitUsageProvider</c>
/// counts every non-archived project platform-wide, and sharing a database
/// with the Auth suite's other test classes would mean the project count
/// starts polluted by their seeded projects, making the
/// "create project #1 succeeds, #2 403s" assertion flaky. One container
/// per fixture is the standard pattern across <c>tests/integration/**</c>.
/// </para>
/// </summary>
public sealed class EditionsCommunityGatingShould(CommunityEditionFixture fixture) : IClassFixture<CommunityEditionFixture>
{
    private static async Task<string> ProblemCodeAsync(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        return payload.GetProperty("code").GetString()!;
    }

    [Fact(DisplayName = "Given a Community edition, when a second project is created via the real endpoint, then it 403s with edition.limit_exceeded")]
    public async Task SecondProjectOnCommunityExceedsLimitAsync()
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

        second.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        second.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        (await ProblemCodeAsync(second)).ShouldBe("edition.limit_exceeded");
    }

    [Fact(DisplayName = "Given a Community edition, when the test-only sample endpoint is called, then it 403s with edition.feature_unavailable")]
    public async Task RequiresFeatureDeniesAnonymousOnCommunityAsync()
    {
        using var client = fixture.CreateApiKeyClient();

        var response = await client.GetAsync("/api/v1/_test/editions-sample", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        (await ProblemCodeAsync(response)).ShouldBe("edition.feature_unavailable");
    }
}

/// <summary>
/// Boots <see cref="HostComposer"/> with no license configured
/// (<c>Host:License:Path</c> left absent by design — that's the
/// Community path) and adds one test-only
/// <c>[RequiresFeature(&quot;multi-repo&quot;)]</c> endpoint to the
/// returned <see cref="WebApplication"/> before starting it. Each
/// fixture instance owns one isolated Postgres container, parallel with
/// the Auth suite's shared one.
/// </summary>
public sealed class CommunityEditionFixture : IAsyncLifetime
{
    private const string SampleEndpointRoute = "/api/v1/_test/editions-sample";

    private readonly PostgresCollectionFixture postgres = new();

    private WebApplication application = null!;
    private TempControlPlaneRoot controlPlane = null!;
    private Uri baseAddress = null!;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await postgres.InitializeAsync();
        var connectionString = postgres.ConnectionString;

        controlPlane = new TempControlPlaneRoot("editions-community");
        controlPlane.WriteDefaultChatCommand();

        var builder = TestHostBuilder.Create(connectionString);
        builder.Logging.AddSimpleConsole(static options => options.IncludeScopes = true);
        builder.Configuration["ControlPlane:Root"] = controlPlane.Root;
        TestBootstrapAdmin.Configure(builder.Configuration);
        TestArtifactsSecrets.ApplyPlaceholder(builder.Configuration);

        application = await HostComposer.ComposeAsync(builder, HostDatabase.Explicit(connectionString));

        // Add the test-only [RequiresFeature("multi-repo")] endpoint
        // before StartAsync so the middleware sees its metadata on
        // every incoming request. Anonymous by design — only the
        // edition gate is exercised here.
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

        controlPlane?.Dispose();
        await postgres.DisposeAsync();
    }
}

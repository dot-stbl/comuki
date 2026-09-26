using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Comuki.Host.Testing;
using Comuki.Host.Testing.Fixtures;
using Comuki.Shared.Editions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace Comuki.Host.Integration.Editions;

/// <summary>
/// End-to-end shape assertion for <c>GET /api/v1/edition</c> against the
/// booted host composition with NO license configured — the Community
/// path every fresh deployment starts on (issue #164, change
/// <c>add-editions-and-licensing</c> task 8.5): tier falls back to
/// <c>community</c>, status to <c>absent</c>, every paid feature is
/// listed honestly with <c>available: false</c> (the dashboard renders
/// its lock affordances from this), the projects limit reports the
/// Community cap of 1, and <c>expiresAt</c> is present on the wire as
/// JSON <c>null</c> (the required+nullable contract the generated zod
/// schema and the FE mapper both rely on).
/// </summary>
public sealed class CommunityEditionEndpointShould(CommunityEditionApiFixture fixture) : IClassFixture<CommunityEditionApiFixture>
{
    [Fact(DisplayName = "Given no license configured, when GET /api/v1/edition is called anonymously, then the Community snapshot shape is byte-correct")]
    public async Task ReturnCommunityEditionSnapshotAsync()
    {
        using var client = fixture.CreateAnonymousClient();

        var response = await client.GetAsync("/api/v1/edition", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        payload.GetProperty("tier").GetString().ShouldBe("community");
        payload.GetProperty("status").GetString().ShouldBe("absent");

        // Required+nullable on the wire: the property is present and null,
        // never omitted — the FE mapper branches on null at the edge.
        payload.GetProperty("expiresAt").ValueKind.ShouldBe(JsonValueKind.Null);

        // Every catalog key exactly once, none covered at Community rank.
        var expectedFeatureKeys = Features.All.Select(static feature => feature.Key.Value).ToArray();
        var features = payload.GetProperty("features");
        features.GetArrayLength().ShouldBe(expectedFeatureKeys.Length);
        var wireFeatureKeys = features.EnumerateArray().Select(static row => row.GetProperty("key").GetString()).ToArray();
        wireFeatureKeys.ToHashSet().SetEquals(expectedFeatureKeys).ShouldBeTrue();
        features.EnumerateArray().ShouldAllBe(static row => !row.GetProperty("available").GetBoolean());

        // The projects quota at its Community cap of 1 on a fresh database.
        var limits = payload.GetProperty("limits");
        var projects = limits.EnumerateArray().Single(static row => row.GetProperty("key").GetString() == "projects");
        projects.GetProperty("current").GetInt32().ShouldBe(0);
        projects.GetProperty("cap").GetInt32().ShouldBe(1);
    }
}

/// <summary>
/// Boots <see cref="HostComposer"/> with no license configured
/// (<c>Host:License:Path</c> left absent by design — that IS the
/// Community path). Each fixture instance owns one isolated Postgres
/// container, parallel with every other suite's — mirrors
/// <c>CommunityEditionFixture</c> in the Auth suite.
/// </summary>
public sealed class CommunityEditionApiFixture : IAsyncLifetime
{
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

        controlPlane = new TempControlPlaneRoot("editions-api-community");
        controlPlane.WriteDefaultChatCommand();

        var builder = TestHostBuilder.Create(connectionString);
        builder.Logging.AddSimpleConsole(static options => options.IncludeScopes = true);
        builder.Configuration["ControlPlane:Root"] = controlPlane.Root;
        TestBootstrapAdmin.Configure(builder.Configuration);
        TestArtifactsSecrets.ApplyPlaceholder(builder.Configuration);

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

        controlPlane?.Dispose();
        await postgres.DisposeAsync();
    }
}

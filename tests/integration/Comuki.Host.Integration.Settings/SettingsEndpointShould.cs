using System.Net;
using System.Net.Http.Json;
using Comuki.Host.Settings;
using Comuki.Host.Testing;
using Comuki.Host.Testing.Fixtures;
using Microsoft.AspNetCore.Builder;
using Shouldly;
using Xunit;

namespace Comuki.Host.Integration.Settings;

/// <summary>
/// Boots the real host composition on a random loopback port against one
/// shared, migrated Postgres (owned by this type's own
/// <see cref="PostgresCollectionFixture"/>, reset to empty before every
/// test) and exercises <c>GET /api/v1/settings</c> — no dedicated
/// integration project existed for this endpoint before this suite
/// (2026-09-23 backend audit). Two config knobs are deliberately
/// overridden away from their defaults (<c>Orchestration:Lease:MaxAttempts</c>,
/// <c>Orchestration:EscalationTimeout:Enabled</c>) so the assertions prove
/// the endpoint reflects the live bound <c>IOptions</c>, not hard-coded
/// values; everything else is asserted at its documented default.
/// </summary>
/// <param name="postgres">The collection's shared Postgres (<see cref="SettingsIntegrationCollection"/>) — reset to empty for every test, migrated once for the whole run.</param>
[Collection(nameof(SettingsIntegrationCollection))]
public sealed class SettingsEndpointShould(PostgresCollectionFixture postgres) : IAsyncLifetime
{
    /// <summary>
    /// boundary: initialised in InitializeAsync before any test runs
    /// </summary>
    private WebApplication application = null!;

    /// <summary>boundary: initialised in InitializeAsync before any test runs</summary>
    private TempControlPlaneRoot controlPlane = null!;

    /// <summary>boundary: initialised in InitializeAsync before any test runs</summary>
    private Uri baseAddress = null!;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await postgres.ResetDatabaseAsync();
        var connectionString = postgres.ConnectionString;

        controlPlane = new TempControlPlaneRoot("settings");

        var builder = TestHostBuilder.Create(connectionString);
        builder.Configuration["ControlPlane:Root"] = controlPlane.Root;
        TestBootstrapAdmin.Configure(builder.Configuration);
        TestArtifactsSecrets.ApplyPlaceholder(builder.Configuration);

        // Overrides away from the documented defaults — proves the
        // endpoint reads the live bound options rather than echoing a
        // hard-coded shape.
        builder.Configuration["Orchestration:Lease:MaxAttempts"] = "7";
        builder.Configuration["Orchestration:EscalationTimeout:Enabled"] = "false";

        application = await HostComposer.ComposeAsync(builder, HostDatabase.Explicit(connectionString));
        baseAddress = await TestHostBuilder.StartAsync(application, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await application.DisposeAsync();
        controlPlane.Dispose();
    }

    private Task<HttpClient> CreateAdminClientAsync()
    {
        var client = new HttpClient(new HttpClientHandler { UseCookies = true, CheckCertificateRevocationList = true })
        {
            BaseAddress = baseAddress,
        };

        return client.LoginAsBootstrapAdminAsync(TestContext.Current.CancellationToken);
    }

    [Fact(DisplayName = "Given overridden lease and escalation config plus untouched compute/proxy defaults, when the settings snapshot is read, then every value reflects the live bound options")]
    public async Task ReturnSettingsSnapshotAsync()
    {
        using var client = await CreateAdminClientAsync();

        var view = await client.GetFromJsonAsync<SettingsView>("/api/v1/settings", TestContext.Current.CancellationToken);

        view.ShouldNotBeNull();

        // Overridden away from the class defaults above — proves live IOptions, not a stub shape.
        view.Orchestration.Lease.MaxAttempts.ShouldBe(7);
        view.Orchestration.EscalationTimeout.Enabled.ShouldBeFalse();

        // Untouched LeaseOptions/EscalationTimeoutOptions defaults.
        view.Orchestration.Lease.LeaseTtlSeconds.ShouldBe(120L);
        view.Orchestration.Lease.ReapIntervalSeconds.ShouldBe(30L);
        view.Orchestration.Lease.ReapGraceSeconds.ShouldBe(30L);
        view.Orchestration.EscalationTimeout.TimeoutSeconds.ShouldBe(3_600L);
        view.Orchestration.EscalationTimeout.SweepIntervalSeconds.ShouldBe(15L);

        // ComputeOptions / ScaleSupervisorOptions defaults — no Compute:* override configured for this suite.
        view.Compute.Provider.ShouldBe("docker");
        view.Compute.Scale.WorkerImage.ShouldBe("ghcr.io/dot-stbl/comuki-worker");
        view.Compute.Scale.ProfilesGitRef.ShouldBe("main");
        view.Compute.Scale.MinIdle.ShouldBe(0);
        view.Compute.Scale.MaxConcurrent.ShouldBe(4);
        view.Compute.Scale.IdleTtlSeconds.ShouldBe(600L);
        view.Compute.Scale.PollIntervalSeconds.ShouldBe(15L);

        // ProxyOptions default — no Proxy:* section configured for this suite.
        view.Proxy.Enabled.ShouldBeFalse();
    }

    [Fact(DisplayName = "Given no authentication, when the settings endpoint is called, then it is rejected")]
    public async Task RefuseAnonymousAsync()
    {
        using var client = new HttpClient { BaseAddress = baseAddress };

        var response = await client.GetAsync("/api/v1/settings", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}

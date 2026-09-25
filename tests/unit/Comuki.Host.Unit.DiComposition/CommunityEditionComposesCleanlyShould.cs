using Comuki.Engine.Orchestration.Application;
using Comuki.Host.Testing;
using Comuki.Host.Workers;
using Comuki.Shared.Editions.Edition;
using Comuki.Shared.Editions.Licensing.Status;
using Comuki.Shared.Editions.Tiers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace Comuki.Host.Unit.DiComposition;

/// <summary>
/// Complement to <see cref="HostServiceProviderShould.ComposeCleanlyAsync"/>:
/// the DI graph this change ships must validate under
/// <c>Host:License:Path</c> being entirely absent — proving Community
/// composes with zero paid services registered, and that no captive
/// dependency or missing registration hangs on a license the operator
/// chose not to install.
/// <para>
/// The test sits in <c>tests/unit/Comuki.Host.Unit.DiComposition</c>
/// rather than inside the architecture-test project because
/// the assertion "resolve <c>IEdition</c> and confirm the Community /
/// Absent identity" is runtime-shape, not a static reflection scan: the
/// value <c>LicenseEdition</c> synthesises at <c>Path = null / empty</c>
/// cannot be observed without resolving the built service provider
/// end-to-end.
/// </para>
/// <para>
/// The Community shape is a property the rest of the codebase depends
/// on — every gateway, every module installer and the dashboard's
/// <c>GET /api/v1/edition</c> reads <see cref="IEdition.Current"/> and
/// <see cref="IEdition.Status"/> before deciding whether to add the paid
/// service. A future change that silently couples a paid registration
/// to <see cref="IEdition"/> will fail here the moment CI tries to
/// build the Community shape.
/// </para>
/// </summary>
public sealed class CommunityEditionComposesCleanlyShould
{
    [Fact(DisplayName = "Given the full host composition with no Host:License:Path configured, when ValidateOnBuild and ValidateScopes are forced on, then it composes and IEdition reports Community / Absent")]
    public async Task CommunityComposesAndResolvesToCommunityAbsentAsync()
    {
        var connectionString = "Host=127.0.0.1;Port=1;Database=di-validation-community;Username=probe;Password=probe";
        var builder = TestHostBuilder.Create(connectionString);

        builder.Services
            .AddOrchestrationApplication()
            .AddWorkerRuntime(builder.Configuration);
        builder.Host.UseDefaultServiceProvider(static options =>
        {
            options.ValidateOnBuild = true;
            options.ValidateScopes = true;
        });

        TestBootstrapAdmin.Configure(builder.Configuration);

        // Arranged absence — the assertion below depends on the configuration
        // genuinely carrying no Host:License:Path section. The license-options
        // validator does NOT fail-loud on a missing Path (that is the
        // documented "absent license behaves as Community" scenario from the
        // spec), so the validator cannot catch a regression here — only this
        // test can.
        builder.Configuration["Host:License:Path"] = null;

        builder.Configuration["Host:Testing:SkipBootMigrations"] = "true";

        var app = await HostComposer.ComposeAsync(builder, HostDatabase.Explicit(connectionString));

        try
        {
            using var scope = app.Services.CreateScope();
            var edition = scope.ServiceProvider.GetRequiredService<IEdition>();

            edition.Current.ShouldBe(EditionTier.Community);
            edition.Status.ShouldBe(LicenseStatus.Absent);
            edition.IsDegraded.ShouldBeFalse();
            edition.ExpiresAt.ShouldBeNull();
        }
        finally
        {
            await app.DisposeAsync();
        }
    }
}

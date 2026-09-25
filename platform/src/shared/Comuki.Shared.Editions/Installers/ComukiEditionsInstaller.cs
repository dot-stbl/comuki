using Comuki.Shared.Editions.Edition;
using Comuki.Shared.Editions.Licensing;
using Comuki.Shared.Editions.Licensing.Ed25519;
using Comuki.Shared.Editions.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Comuki.Shared.Editions.Installers;

/// <summary>
/// DI installer for <see cref="LicenseOptions"/> + the production
/// <see cref="Ed25519LicenseProvider"/> + the hot-reloading
/// <see cref="IEdition"/>. Absent config -&gt; Community (no error);
/// present-but-unresolvable <see cref="LicenseOptions.Path"/> fails
/// boot loudly via <see cref="LicenseOptionsValidator"/>;
/// present-but-cryptographically-invalid license degrades to
/// Community at runtime with a logged warning (see
/// <see cref="LicenseEdition"/>) — neither bad path crashes
/// the host.
/// <para>
/// Deliberately NOT wired into <c>Comuki.Host</c>'s <c>HostComposer</c>
/// in this chunk — host wiring is API/DI-gating-adjacent territory
/// this workstream explicitly excludes. The installer is a fully
/// self-contained, testable extension method a later change calls.
/// </para>
/// </summary>
public static class ComukiEditionsInstaller
{
    /// <summary>
    /// Binds <see cref="LicenseOptions"/> from <c>Host:License</c>,
    /// registers the validator, the production
    /// <see cref="Ed25519LicenseProvider"/>, and <see cref="IEdition"/>.
    /// </summary>
    public static IServiceCollection AddComukiEditions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<LicenseOptions>()
            .Bind(configuration.GetSection(LicenseOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<LicenseOptions>, LicenseOptionsValidator>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<ILicenseProvider>(static _ => new Ed25519LicenseProvider(ProductionEd25519PublicKey.Value));
        services.AddSingleton<IEdition, LicenseEdition>();

        return services;
    }
}

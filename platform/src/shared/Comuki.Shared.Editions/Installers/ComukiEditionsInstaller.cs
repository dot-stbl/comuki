using Comuki.Shared.Editions.Edition;
using Comuki.Shared.Editions.Licensing;
using Comuki.Shared.Editions.Licensing.Ed25519;
using Comuki.Shared.Editions.Licensing.Ed25519.Internal;
using Comuki.Shared.Editions.Options;
using Comuki.Shared.Editions.Registry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Comuki.Shared.Editions.Installers;

/// <summary>
/// DI installer for <see cref="LicenseOptions"/> + the production
/// <see cref="Ed25519LicenseProvider"/> + the hot-reloading
/// <see cref="IEdition"/> + the in-memory <see cref="IEditionCapabilityRegistry"/>
/// every API / DI gate call site reads from. Absent config -&gt; Community
/// (no error); present-but-unresolvable <see cref="LicenseOptions.Path"/>
/// fails boot loudly via <see cref="LicenseOptionsValidator"/>;
/// present-but-cryptographically-invalid license degrades to
/// Community at runtime with a logged warning (see
/// <see cref="LicenseEdition"/>) — neither bad path crashes
/// the host.
/// <para>
/// Wired into <c>Comuki.Host</c>'s <c>HostComposer</c> in chunk C
/// (issue #164 / add-editions-and-licensing, §4 &amp; §5).
/// </para>
/// </summary>
public static class ComukiEditionsInstaller
{
    /// <summary>
    /// Binds <see cref="LicenseOptions"/> from <c>Host:License</c>,
    /// registers the validator, the production
    /// <see cref="Ed25519LicenseProvider"/> (with the optional
    /// dev-overlay key when <see cref="LicenseOptions.DevPublicKey"/>
    /// is configured), <see cref="IEdition"/>, and the in-memory
    /// <see cref="IEditionCapabilityRegistry"/>.
    /// </summary>
    public static IServiceCollection AddComukiEditions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<LicenseOptions>()
            .Bind(configuration.GetSection(LicenseOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<LicenseOptions>, LicenseOptionsValidator>();
        services.TryAddSingleton(TimeProvider.System);

        // TryAdd so an integration test that needs to verify against a
        // NON-production keypair can register its own ILicenseProvider on
        // the builder's IServiceCollection BEFORE calling
        // HostComposer.ComposeAsync, and that earlier registration wins.
        // The production private key was deliberately discarded per
        // ProductionEd25519PublicKey.cs's doc comment — nobody, including
        // tests, can mint a token the production key accepts.
        //
        // Dev key is resolved from IOptions<LicenseOptions> so the
        // boot-time validator has already enforced its shape. An
        // empty dev-key span means "no dev licenses trusted here"
        // (Ed25519LicenseProvider.Verify throws "dev audience not
        // trusted here" for any dev-audience token). Production
        // behaviour is unchanged because nothing else registers
        // ILicenseProvider in production, so TryAdd behaves exactly
        // like Add here.
        services.TryAddSingleton<ILicenseProvider>(static sp =>
        {
            var options = sp.GetRequiredService<IOptions<LicenseOptions>>().Value;
            var devKey = Ed25519PublicKeyParsing.TryDecode(options.DevPublicKey, out var decoded)
                ? decoded ?? throw new InvalidOperationException("DevPublicKey decode returned null despite TryDecode success.")
                : [];
            return new Ed25519LicenseProvider(ProductionEd25519PublicKey.Value, devKey, TimeProvider.System);
        });
        services.AddSingleton<IEdition, LicenseEdition>();
        services.AddSingleton<IEditionCapabilityRegistry, EditionCapabilityRegistry>();

        return services;
    }
}

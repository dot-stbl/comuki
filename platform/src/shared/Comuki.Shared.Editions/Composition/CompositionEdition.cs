using Comuki.Shared.Editions.Edition;
using Comuki.Shared.Editions.Gating;
using Comuki.Shared.Editions.Installers;
using Comuki.Shared.Editions.Licensing;
using Comuki.Shared.Editions.Licensing.Ed25519;
using Comuki.Shared.Editions.Licensing.Status;
using Comuki.Shared.Editions.Options;
using Comuki.Shared.Editions.Tiers;
using Comuki.Shared.Kernel.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Comuki.Shared.Editions.Composition;

/// <summary>
/// Composition-time edition loader — the read-side of
/// <see cref="IEdition"/> resolved exactly once, before
/// <see cref="IServiceCollection"/>
/// has been built. The host calls this once before its
/// <c>AddComukiEditions(...)</c> + module registration chain and passes
/// the returned snapshot to every <see cref="AddComukiModuleExtensions.AddComukiModule{TModule}"/>
/// / <see cref="AddForEditionExtensions.AddForEdition{TService}"/>
/// call. Read snapshots cross-check with
/// <see cref="LicenseEvaluator"/>'s <see cref="LicenseEvaluator.Classify"/>
/// output, so a hot-reload license change at runtime still honours the
/// snapshot's decision at composition time (a fact documented on
/// <see cref="AddComukiModuleExtensions.AddComukiModule{TModule}"/>).
/// </summary>
public static class CompositionEdition
{
    /// <summary>
    /// Builds the composition-time edition snapshot from
    /// <paramref name="configuration"/> — binds
    /// <see cref="LicenseOptions"/> from <see cref="LicenseOptions.SectionName"/>,
    /// resolves the secret via the in-process
    /// <see cref="EnvSecretProvider"/> + <see cref="FileSecretProvider"/>
    /// stack, verifies via <see cref="ProductionEd25519PublicKey"/>,
    /// classifies. Failures degrade to Community + a logged warning —
    /// matching <see cref="LicenseEdition"/>'s runtime failure policy.
    /// </summary>
    /// <param name="configuration">The host's configuration; reads <c>Host:License</c>.</param>
    /// <param name="loggerFactory">Optional sink for resolution warnings; defaults to <see cref="NullLoggerFactory.Instance"/>.</param>
    /// <returns>The snapshot. Pass to <see cref="AddComukiModuleExtensions.AddComukiModule{TModule}"/> / <see cref="AddForEditionExtensions.AddForEdition{TService}"/>.</returns>
    public static IEdition Load(IConfiguration configuration, ILoggerFactory? loggerFactory = null)
    {
        var options = new LicenseOptions();
        configuration.GetSection(LicenseOptions.SectionName).Bind(options);

        if (string.IsNullOrWhiteSpace(options.Path))
        {
            return new CompositionEditionSnapshot(LicenseStatus.Absent, EditionTier.Community, license: null);
        }

        var resolver = new CompositeSecretResolver(
        [
            new EnvSecretProvider(),
            new FileSecretProvider(Microsoft.Extensions.Options.Options.Create(new FileSecretOptions { Enabled = true })),
        ]);

        var logger = (loggerFactory ?? NullLoggerFactory.Instance)
            .CreateLogger("Comuki.Shared.Editions.Composition");

        // Sync-over-async at composition time matches LicenseEdition's
        // ComputeSnapshot shape: the resolver/verifier are async because
        // secret reads and ED25519 verification can be expensive in
        // future slices (Vault / Consul / remote KV); this composition
        // call only runs once during host boot. VSTHRD102 / CA2007 do not
        // apply — see cs/async-and-tasks.md §3.
#pragma warning disable VSTHRD002
#pragma warning disable VSTHRD102
        var resolved = resolver.ResolveAsync(options.Path).GetAwaiter().GetResult();
#pragma warning restore VSTHRD102
#pragma warning restore VSTHRD002

        if (string.IsNullOrWhiteSpace(resolved))
        {
            return new CompositionEditionSnapshot(LicenseStatus.Absent, EditionTier.Community, license: null);
        }

        try
        {
            var licenseProvider = new Ed25519LicenseProvider(ProductionEd25519PublicKey.Value);
            var license = licenseProvider.Verify(resolved);
            var classified = LicenseEvaluator.Classify(license, DateTimeOffset.UtcNow, options.GracePeriod);
            return new CompositionEditionSnapshot(classified.Status, classified.Current, license);
        }
        catch (Exception exception) when (exception is SecretRefUnsetException
                                            or SecretRefFormatException
                                            or LicenseInvalidException
                                            or ArgumentOutOfRangeException)
        {
            logger.LogWarning(
                exception,
                "composition-time license at {Path} could not be verified; falling back to Community",
                options.Path);
            return new CompositionEditionSnapshot(LicenseStatus.Absent, EditionTier.Community, license: null);
        }
    }
}

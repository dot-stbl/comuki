using Comuki.Shared.Editions.Catalog;
using Comuki.Shared.Editions.Edition;
using Comuki.Shared.Editions.Gating;
using Comuki.Shared.Editions.Licensing;
using Comuki.Shared.Editions.Licensing.Ed25519;
using Comuki.Shared.Editions.Licensing.Modes;
using Comuki.Shared.Editions.Licensing.Status;
using Comuki.Shared.Editions.Tiers;
using Comuki.Shared.Kernel.Secrets;

namespace Comuki.Shared.Editions.Composition;

/// <summary>
/// Static, composition-time projection of <see cref="IEdition"/> — the
/// exact same resolution path the runtime <see cref="LicenseEdition"/>
/// uses (<c>LicenseOptions.Path</c> -&gt; <see cref="ISecretResolver"/>
/// chain -&gt; <see cref="Ed25519LicenseProvider"/> -&gt;
/// <see cref="LicenseEvaluator.Classify"/>), but frozen at the moment
/// <c>AddComukiEditions(...)</c> runs. The snapshot is the answer to the
/// <see cref="AddComukiModuleExtensions.AddComukiModule{TModule}"/> /
/// <see cref="AddForEditionExtensions.AddForEdition{TService}"/>
/// composition-time gate calls so neither helper has to build a
/// throwaway <see cref="IServiceProvider"/> to look up
/// <see cref="IEdition"/> mid-registration.
/// <para>
/// Resolution failures (missing file, malformed reference, bad
/// signature, negative grace) degrade to <see cref="LicenseStatus.Absent"/>
/// / <see cref="EditionTier.Community"/> with a logged warning — the
/// same failure policy <see cref="LicenseEdition"/> uses at runtime.
/// The snapshot therefore never disagrees with the runtime
/// hot-reloading source: whatever edition a license produced at
/// registration, that license is the same one the runtime gate will
/// see on every subsequent request.
/// </para>
/// </summary>
internal sealed class CompositionEditionSnapshot : IEdition
{
    private readonly LicenseKey? license;

    /// <summary>Builds a snapshot from the resolved license outcome.</summary>
    /// <param name="status">The license's lifecycle classification.</param>
    /// <param name="tier">The effective edition tier (Community when absent).</param>
    /// <param name="license">The verified license, or null when no license applies.</param>
    internal CompositionEditionSnapshot(LicenseStatus status, EditionTier tier, LicenseKey? license)
    {
        Status = status;
        Current = tier;
        this.license = license;
    }

    /// <inheritdoc />
    public EditionTier Current { get; }

    /// <inheritdoc />
    public LicenseStatus Status { get; }

    /// <inheritdoc />
    public bool IsDegraded => Status == LicenseStatus.Expired;

    /// <inheritdoc />
    public bool Has(Feature feature)
    {
        // Match LicenseEdition.Has exactly — the two implementations must
        // agree byte-for-byte on the same (license, feature) input.
        return license is { } licenseSnapshot && licenseSnapshot.Mode == LicenseMode.ExplicitAllowlist
            ? licenseSnapshot.Features.Contains(feature.Key.Value)
            : Current.Rank >= feature.MinimumRank;
    }

    /// <inheritdoc />
    public int Limit(Limit limit)
    {
        return license is { } licenseSnapshot && licenseSnapshot.Limits.TryGetValue(limit.Key.Value, out var overrideValue)
            ? overrideValue
            : limit.ValueFor(Current);
    }
}

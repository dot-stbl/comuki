using Comuki.Shared.Editions.Catalog;
using Comuki.Shared.Editions.Licensing.Status;
using Comuki.Shared.Editions.Tiers;

namespace Comuki.Shared.Editions.Edition;

/// <summary>
/// The runtime read-side of the current license: what tier is active,
/// whether a specific paid feature/limit is covered, and whether the
/// license has degraded past its grace period. Backed by
/// <see cref="LicenseEdition"/>, which re-verifies the underlying
/// license on a throttled interval
/// (<see cref="Options.LicenseOptions.ReloadDelay"/>) so a replaced
/// license file is picked up without a process restart.
/// <para>
/// Members are pure reads; the port deliberately does not expose the
/// underlying <see cref="Licensing.LicenseKey"/> (a downstream gate
/// uses <see cref="IsDegraded"/> as a signal to refuse writes, but it
/// only needs the boolean — leaking the verified key from every read
/// site would let an unauthenticated caller print the customer's
/// organisation name and feature set).
/// </para>
/// </summary>
public interface IEdition
{
    /// <summary>The effective edition tier right now.</summary>
    public EditionTier Current { get; }

    /// <summary>Where the underlying license sits in its lifecycle.</summary>
    public LicenseStatus Status { get; }

    /// <summary>
    /// True exactly when <see cref="Status"/> is
    /// <see cref="LicenseStatus.Expired"/> — past grace. Downstream
    /// gates (a later, out-of-scope workstream) use this as the signal
    /// to refuse writes while still allowing reads/exports of data the
    /// customer already owns; this port only reports the signal.
    /// </summary>
    public bool IsDegraded { get; }

    /// <summary>Whether <paramref name="feature"/> is covered at the current tier/license.</summary>
    public bool Has(Feature feature);

    /// <summary>The effective numeric cap for <paramref name="limit"/> at the current tier/license.</summary>
    public int Limit(Limit limit);
}

using Comuki.Shared.Editions.Catalog;
using Comuki.Shared.Editions.Edition;
using Comuki.Shared.Editions.Licensing.Status;
using Comuki.Shared.Editions.Tiers;

namespace Comuki.Modules.Projects.Integration.Migrations;

/// <summary>
/// Fixed-cap <see cref="IEdition"/> stub for integration tests —
/// reports Community / Absent for the metadata fields and pins every
/// <see cref="IEdition.Limit"/> call to a single value so a test
/// controls the cap without reaching into the licensing layer
/// (<c>ProductionEd25519PublicKey</c> is a placeholder key a test
/// cannot mint against). The migrations suite registers it with
/// <see cref="int.MaxValue"/> so its creates never hit the quota; the
/// race suite registers it with <c>cap = 1</c>.
/// </summary>
/// <param name="cap">The value every <see cref="IEdition.Limit"/> call returns.</param>
internal sealed class FixedCapEdition(int cap) : IEdition
{
    public EditionTier Current => EditionTier.Community;

    public LicenseStatus Status => LicenseStatus.Absent;

    public bool IsDegraded => false;

    public bool Has(Feature feature)
    {
        return false;
    }

    public int Limit(Limit limit)
    {
        return cap;
    }
}

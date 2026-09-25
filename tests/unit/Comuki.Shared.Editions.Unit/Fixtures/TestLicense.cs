using Comuki.Shared.Editions.Catalog;
using Comuki.Shared.Editions.Licensing;
using Comuki.Shared.Editions.Licensing.Ed25519;
using Comuki.Shared.Editions.Licensing.Grants;
using Comuki.Shared.Editions.Licensing.Modes;
using Comuki.Shared.Editions.Tiers;

namespace Comuki.Shared.Editions.Unit.Fixtures;

/// <summary>
/// Test-only signed license fixtures, all under one fixed test keypair
/// generated once per test process — never the production embedded key
/// (<c>Installers.ProductionEd25519PublicKey</c>), which this file never
/// references. <see cref="Provider"/> is a ready-made
/// <see cref="Ed25519LicenseProvider"/> already pointed at
/// <see cref="PublicKey"/>, so most consumers never touch the keypair
/// directly.
/// </summary>
public static class TestLicense
{
    private static readonly Ed25519KeyPair keyPair = Ed25519LicenseSigner.GenerateKeyPair();

    /// <summary>The fixture keypair's public half — construct your own <see cref="Ed25519LicenseProvider"/> with this when you need a fresh instance (e.g. to pin a specific clock).</summary>
    public static byte[] PublicKey => keyPair.PublicKey;

    /// <summary>A ready-made verifier already pointed at <see cref="PublicKey"/>, using <see cref="TimeProvider.System"/>.</summary>
    public static ILicenseProvider Provider { get; } = new Ed25519LicenseProvider(keyPair.PublicKey);

    /// <summary>A valid, far-future-expiry, Community-tier signed token — distinct from "no license configured at all" (which is the Absent path every other test already covers); this exercises an explicitly-issued rank-0 license.</summary>
    public static string Community { get; } = With(EditionTiers.Community);

    /// <summary>
    /// Signs a token for <paramref name="tier"/> with the fixture keypair.
    /// Every parameter beyond <paramref name="tier"/> has a sensible
    /// default so a caller only names what the test actually cares about.
    /// </summary>
    /// <param name="tier">The tier the token claims.</param>
    /// <param name="org">Defaults to <c>"Test Org"</c>.</param>
    /// <param name="expiry">Defaults to 100 years from construction time — effectively "never expires" for a test.</param>
    /// <param name="notBefore">Defaults to <c>null</c> (valid immediately).</param>
    /// <param name="mode">Defaults to <see cref="LicenseMode.ImplicitByRank"/>.</param>
    /// <param name="features">Defaults to <c>null</c> (absent field).</param>
    /// <param name="limits">Defaults to <c>null</c> (absent field).</param>
    public static string With(
        EditionTier tier,
        string org = "Test Org",
        DateTimeOffset? expiry = null,
        DateTimeOffset? notBefore = null,
        LicenseMode? mode = null,
        IReadOnlyCollection<string>? features = null,
        IReadOnlyDictionary<string, int>? limits = null)
    {
        var grant = new LicenseGrant(
            Org: org,
            Tier: tier,
            Expiry: expiry ?? DateTimeOffset.UtcNow.AddYears(100),
            Mode: mode ?? LicenseMode.ImplicitByRank,
            NotBefore: notBefore,
            Features: features,
            Limits: limits);

        return Ed25519LicenseSigner.Sign(grant, keyPair.PrivateKeySeed);
    }

    /// <summary>
    /// Convenience overload: an <see cref="LicenseMode.ExplicitAllowlist"/>
    /// token granting exactly <paramref name="features"/>, at the highest
    /// tier any of them requires (falls back to <see cref="EditionTiers.Team"/>
    /// when <paramref name="features"/> is empty, since an explicit grant
    /// only makes sense above Community). Matches the
    /// <c>TestLicense.With(Features.X)</c> shape named in the issue.
    /// </summary>
    public static string With(params Feature[] features)
    {
        var tier = features.Length == 0
            ? EditionTiers.Team
            : EditionTiers.All.Where(candidate => candidate.Rank >= features.Max(feature => feature.MinimumRank))
                .OrderBy(candidate => candidate.Rank)
                .First();

        return With(tier, mode: LicenseMode.ExplicitAllowlist, features: [.. features.Select(feature => feature.Key.Value)]);
    }
}

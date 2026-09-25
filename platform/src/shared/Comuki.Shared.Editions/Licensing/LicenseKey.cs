using Comuki.Shared.Editions.Licensing.Modes;
using Comuki.Shared.Editions.Tiers;

namespace Comuki.Shared.Editions.Licensing;

/// <summary>
/// The trusted, verified output of <see cref="ILicenseProvider.Verify"/>.
/// The token bytes have been signature-checked; what remains is a set of
/// strongly-typed fields every consumer (gates, the capability registry,
/// the API response) reads without re-running crypto.
/// <para>
/// <see cref="Features"/> and <see cref="Limits"/> are the raw strings from
/// the wire — this type does not resolve them against the
/// <see cref="Editions.Features"/> / <see cref="Editions.Limits"/> catalogs.
/// An unknown feature/limit key in a license is the consumer's problem,
/// not this type's; upstream of resolution, callers see exactly what the
/// payload contained.
/// </para>
/// </summary>
/// <param name="Tier">The edition tier the license names (resolved from the payload's <c>edition</c> code).</param>
/// <param name="Org">The licensed organisation name from the payload.</param>
/// <param name="NotBefore">Optional start-of-validity instant; <c>null</c> means the license is valid immediately on issuance.</param>
/// <param name="Expiry">The end of the validity window; <see cref="Status.LicenseEvaluator"/> decides grace.</param>
/// <param name="Mode">How downstream consumers match <see cref="Features"/> / <see cref="Limits"/> against the catalogs.</param>
/// <param name="Features">Raw capability keys from the payload; empty when absent.</param>
/// <param name="Limits">Raw quota-key -> cap map from the payload; empty when absent.</param>
/// <param name="VerifiedAt">The <see cref="TimeProvider"/> instant at the moment of successful verification.</param>
/// <param name="VerifiedWith">Lowercase hex of the first 8 bytes of <c>SHA256(publicKeyBytes)</c> — a short fingerprint of the key that accepted the token.</param>
public sealed record LicenseKey(
    EditionTier Tier,
    string Org,
    DateTimeOffset? NotBefore,
    DateTimeOffset Expiry,
    LicenseMode Mode,
    IReadOnlyCollection<string> Features,
    IReadOnlyDictionary<string, int> Limits,
    DateTimeOffset VerifiedAt,
    string VerifiedWith);

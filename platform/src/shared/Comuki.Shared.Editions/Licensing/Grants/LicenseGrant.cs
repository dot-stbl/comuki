using Comuki.Shared.Editions.Licensing.Modes;
using Comuki.Shared.Editions.Tiers;

namespace Comuki.Shared.Editions.Licensing.Grants;

/// <summary>
/// The unverified input to <see cref="Ed25519.Ed25519LicenseSigner.Sign"/>:
/// the shape the mint tool and test fixtures construct and hand to the
/// signer. Distinct from <see cref="LicenseKey"/> by intent — a grant is
/// the sign-side claim (could be forged), a key is the
/// crypto-verified outcome the verifiers emit.
/// <para>
/// <see cref="Mode"/> has no default value: smart-type instances are not
/// compile-time constants, so C# cannot default an optional parameter
/// to one. Callers always pass <see cref="LicenseMode.ImplicitByRank"/> or
/// <see cref="LicenseMode.ExplicitAllowlist"/> explicitly.
/// </para>
/// </summary>
/// <param name="Org">The licensed organisation name; emitted verbatim as the payload's <c>org</c>.</param>
/// <param name="Tier">The edition tier the license names; <see cref="EditionTier.Code"/> is emitted as the payload's <c>edition</c>.</param>
/// <param name="Expiry">The end of the validity window.</param>
/// <param name="Mode">How downstream consumers match <see cref="Features"/> / <see cref="Limits"/> against the catalogs.</param>
/// <param name="NotBefore">Optional start-of-validity instant.</param>
/// <param name="Seats">Optional informational seat count (not enforced in this change).</param>
/// <param name="Features">Optional raw capability keys; <c>null</c> serialises as the JSON <c>null</c>.</param>
/// <param name="Limits">Optional raw quota caps; <c>null</c> serialises as the JSON <c>null</c>.</param>
public sealed record LicenseGrant(
    string Org,
    EditionTier Tier,
    DateTimeOffset Expiry,
    LicenseMode Mode,
    DateTimeOffset? NotBefore = null,
    int? Seats = null,
    IReadOnlyCollection<string>? Features = null,
    IReadOnlyDictionary<string, int>? Limits = null);

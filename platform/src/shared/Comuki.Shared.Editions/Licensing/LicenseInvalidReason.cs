namespace Comuki.Shared.Editions.Licensing;

/// <summary>
/// Stable snake_case machine tokens carried on
/// <see cref="LicenseInvalidException.Reason"/>. Callers branch on these
/// values — never on <see cref="Exception.Message"/> substrings — so the
/// human-readable text can be rephrased without breaking downstream
/// consumers. Each constant mirrors a static factory on
/// <see cref="LicenseInvalidException"/>; the two stay in lockstep.
/// </summary>
public static class LicenseInvalidReason
{
    /// <summary>Wrong number of <c>.</c> separators, or a non-base64url half.</summary>
    public const string MalformedTokenShape = "malformed_token_shape";

    /// <summary>Ed25519 verifier rejected the signature, or the verifying key's length was wrong.</summary>
    public const string BadSignature = "bad_signature";

    /// <summary>JSON parse/shape failure, or an unrecognised <c>mode</c> string.</summary>
    public const string MalformedPayload = "malformed_payload";

    /// <summary>The payload's <c>edition</c> does not resolve via <c>EditionTiers.TryGetByCode</c>.</summary>
    public const string UnknownEditionCode = "unknown_edition_code";

    /// <summary>The payload's <c>audience</c> is not a recognised <c>LicenseAudience</c> value.</summary>
    public const string UnknownAudience = "unknown_audience";

    /// <summary>The token's audience is <c>dev</c> but no dev verifying key is configured for this contour.</summary>
    public const string DevAudienceNotTrusted = "dev_audience_not_trusted";
}

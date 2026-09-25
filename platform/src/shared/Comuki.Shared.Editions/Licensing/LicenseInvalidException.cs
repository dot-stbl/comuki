namespace Comuki.Shared.Editions.Licensing;

/// <summary>
/// Raised by <see cref="ILicenseProvider.Verify"/> when a license token
/// fails any step of the verification pipeline (shape, signature, payload
/// parse, edition resolution).
/// <para>
/// <see cref="Reason"/> is the stable snake_case machine token a caller
/// branches on (see <see cref="LicenseInvalidReason"/>);
/// <see cref="Exception.Message"/> is the human-readable form shown to
/// operators and shipped to logs. Tests assert on
/// <see cref="Reason"/>; logs and humans read
/// <see cref="Exception.Message"/>.
/// </para>
/// </summary>
public sealed class LicenseInvalidException : Exception
{
    /// <summary>Stable snake_case machine token; never null, never localised. See <see cref="LicenseInvalidReason"/>.</summary>
    public string Reason { get; }

    private LicenseInvalidException(string reason, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Reason = reason;
    }

    /// <summary>Wrong number of <c>.</c> separators, or a non-base64url half.</summary>
    public static LicenseInvalidException MalformedTokenShape()
    {
        return new(LicenseInvalidReason.MalformedTokenShape, "license token is invalid: malformed token shape");
    }

    /// <summary>Ed25519 verifier rejected the signature, or the verifying key's length was wrong.</summary>
    public static LicenseInvalidException BadSignature()
    {
        return new(LicenseInvalidReason.BadSignature, "license token is invalid: signature mismatch");
    }

    /// <summary>JSON parse/shape failure, or an unrecognised <c>mode</c> string.</summary>
    /// <param name="innerException">The underlying <c>JsonException</c> (or similar) that surfaced as a <c>malformed_payload</c>.</param>
    public static LicenseInvalidException MalformedPayload(Exception? innerException = null)
    {
        return new(LicenseInvalidReason.MalformedPayload, "license token is invalid: malformed payload", innerException);
    }

    /// <summary>The payload's <c>edition</c> does not resolve via <c>EditionTiers.TryGetByCode</c>.</summary>
    public static LicenseInvalidException UnknownEditionCode()
    {
        return new(LicenseInvalidReason.UnknownEditionCode, "license token is invalid: unknown edition code");
    }
}

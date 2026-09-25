namespace Comuki.Shared.Editions.Licensing;

/// <summary>
/// Raised by <see cref="ILicenseProvider.Verify"/> when a license token
/// fails any step of the verification pipeline (shape, signature, payload
/// parse, edition resolution). <see cref="Exception.Message"/> embeds a
/// stable lowercase <c>reason</c> the verifier is the sole author of —
/// tests assert on substrings, so do not rephrase the head without
/// updating the brief's allowlist:
/// <list type="bullet">
/// <item><c>"malformed token shape"</c> — wrong number of <c>.</c> separators, or a non-base64url half.</item>
/// <item><c>"signature mismatch"</c> — Ed25519 verifier rejected the signature, or the verifying key's length was wrong.</item>
/// <item><c>"malformed payload"</c> — JSON parse/shape failure, or an unrecognised <c>mode</c> string.</item>
/// <item><c>"unknown edition code"</c> — the payload's <c>edition</c> does not resolve via <see cref="Tiers.EditionTiers.TryGetByCode"/>.</item>
/// </list>
/// </summary>
/// <param name="reason">Lowercase, hyphen-free reason embedded in <see cref="Exception.Message"/>.</param>
public sealed class LicenseInvalidException(string reason)
    : Exception($"license token is invalid: {reason}");

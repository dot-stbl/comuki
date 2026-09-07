using System.Security.Cryptography;

namespace Comuki.Modules.Intake.Application.Sources;

/// <summary>
/// Webhook secret generator (issue #46 — rotate-secret endpoint). The
/// rotated value is a 32-byte cryptographic RNG rendered as lowercase hex
/// (64 chars) — 256 bits of entropy, the conventional strength for a
/// HMAC verification secret. The value leaves the host only in the
/// rotation response; nothing else ever persists it.
/// </summary>
public static class WebhookSecretGenerator
{
    /// <summary>Length of the random byte buffer — 256 bits of entropy.</summary>
    public const int ByteLength = 32;

    /// <summary>Generates a fresh webhook verification secret.</summary>
    /// <returns>Lowercase hex, length <c>2 * <see cref="ByteLength"/></c>.</returns>
    public static string Generate()
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(ByteLength)).ToLowerInvariant();
    }
}

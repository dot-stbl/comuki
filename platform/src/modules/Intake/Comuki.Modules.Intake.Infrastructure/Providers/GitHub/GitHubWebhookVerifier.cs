using System.Security.Cryptography;
using System.Text;

namespace Comuki.Modules.Intake.Infrastructure.Providers.GitHub;

/// <summary>
/// GitHub webhook signature verification: the
/// <c>X-Hub-Signature-256</c> header carries
/// <c>sha256=HEX(HMAC-SHA256(secret, body))</c>. The comparison is
/// fixed-time on the decoded digest — a missing secret or header
/// answers false.
/// </summary>
public static class GitHubWebhookVerifier
{
    private const string Prefix = "sha256=";

    /// <summary>Verifies the HMAC signature of a raw webhook body.</summary>
    /// <param name="secret">The webhook secret; null/empty fails closed.</param>
    /// <param name="signatureHeader">The raw header value.</param>
    /// <param name="body">The exact bytes the signature was computed over.</param>
    /// <returns></returns>
    public static bool Verify(string? secret, string? signatureHeader, ReadOnlySpan<byte> body)
    {
        if (string.IsNullOrEmpty(secret)
            || signatureHeader is null
            || !signatureHeader.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
            || signatureHeader.Length != Prefix.Length + 64)
        {
            return false;
        }

        var hex = signatureHeader[Prefix.Length..];
        var expected = new byte[32];
        for (var index = 0; index < expected.Length; index++)
        {
            if (!byte.TryParse(hex.AsSpan(index * 2, 2), System.Globalization.NumberStyles.HexNumber, null, out var digit))
            {
                return false;
            }

            expected[index] = digit;
        }

        var actual = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}

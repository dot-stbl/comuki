using System.Security.Cryptography;
using System.Text;
using Comuki.Modules.Identity.Application.Options;
using Microsoft.Extensions.Options;

namespace Comuki.Modules.Identity.Application.ApiKeys;

/// <summary>
/// HMAC-SHA256 over the API-key plaintext with the server-side pepper.
/// Verification is constant-time (<see cref="CryptographicOperations.FixedTimeEquals"/>)
/// so a timing side channel cannot confirm a guessed prefix or secret.
/// </summary>
/// <param name="options"></param>
public sealed class ApiKeyHasher(IOptions<ApiKeyOptions> options)
{
    private static readonly Encoding utf8 = new UTF8Encoding(false, true);

    private readonly ApiKeyOptions options = options.Value;

    /// <summary>Computes the lowercase-hex digest of a token.</summary>
    /// <param name="plaintext"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentException">The plaintext is empty.</exception>
    /// <exception cref="InvalidOperationException">The pepper is not configured.</exception>
    public string Hash(string plaintext)
    {
        return plaintext.Length > 0
            ? options.Pepper.Length > 0
                ? Convert.ToHexString(HMACSHA256.HashData(utf8.GetBytes(options.Pepper), utf8.GetBytes(plaintext)))
                    .ToLowerInvariant()
                : throw new InvalidOperationException("ApiKeyOptions.Pepper is not configured")
            : throw new ArgumentException("plaintext must not be empty", nameof(plaintext));
    }

    /// <summary>Verifies a presented token against a stored digest; constant-time, false on malformed input.</summary>
    /// <param name="plaintext"></param>
    /// <param name="storedDigest"></param>
    /// <returns></returns>
    public bool Verify(string plaintext, string storedDigest)
    {
        if (plaintext.Length == 0 || storedDigest.Length == 0)
        {
            return false;
        }

        var candidate = Hash(plaintext);

        return candidate.Length == storedDigest.Length
            && CryptographicOperations.FixedTimeEquals(utf8.GetBytes(candidate), utf8.GetBytes(storedDigest));
    }
}

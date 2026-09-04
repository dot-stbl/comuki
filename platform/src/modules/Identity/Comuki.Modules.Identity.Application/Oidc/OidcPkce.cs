using System.Security.Cryptography;
using System.Text;

namespace Comuki.Modules.Identity.Application.Oidc;

/// <summary>
/// PKCE helpers per RFC 7636: a 32-byte random verifier (43-char
/// base64url, no padding), and the SHA-256 challenge derived from it.
/// The challenge is what the browser carries; the verifier stays in
/// the state row until the callback redeems it.
/// </summary>
public static class OidcPkce
{
    /// <summary>Recommended verifier length per RFC 7636 §4.1 — 32 bytes → 43 chars.</summary>
    private const int VerifierByteLength = 32;

    /// <summary>One PKCE verifier + S256 challenge pair.</summary>
    /// <param name="verifier"></param>
    /// <param name="challenge"></param>
    public readonly record struct Pair(string Verifier, string Challenge);

    /// <summary>Generates a fresh <c>S256</c> pair.</summary>
    public static Pair Generate()
    {
        var verifier = GenerateVerifier();
        var challenge = ComputeS256Challenge(verifier);

        return new Pair(verifier, challenge);
    }

    /// <summary>Generates a fresh 32-byte verifier, base64url-encoded without padding.</summary>
    public static string GenerateVerifier()
    {
        var buffer = new byte[VerifierByteLength];
        RandomNumberGenerator.Fill(buffer);

        return ToBase64Url(buffer);
    }

    /// <summary>Returns the S256 challenge of <paramref name="verifier"/>.</summary>
    /// <param name="verifier"></param>
    public static string ComputeS256Challenge(string verifier)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));

        return ToBase64Url(hash);
    }

    private static string ToBase64Url(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}

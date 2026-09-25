using System.Security.Cryptography;

namespace Comuki.Shared.Editions.Licensing.Ed25519.Internal;

/// <summary>
/// Short fingerprint of an Ed25519 public key — the first 8 bytes of
/// <c>SHA256(publicKeyBytes)</c> rendered as lowercase hex. Lives on
/// <see cref="LicenseKey.VerifiedWith"/> so downstream code can show
/// "verified by key ABCDEF12" without exposing the full key.
/// </summary>
internal static class Ed25519PublicKeyFingerprint
{
    /// <summary>Returns the 16-character lowercase hex fingerprint of <paramref name="publicKey"/>.</summary>
    public static string Compute(byte[] publicKey)
    {
        var hash = SHA256.HashData(publicKey);
        return Convert.ToHexStringLower(hash.AsSpan(0, 8));
    }
}

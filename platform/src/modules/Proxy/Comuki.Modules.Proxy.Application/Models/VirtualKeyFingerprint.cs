using System.Buffers.Text;
using System.Security.Cryptography;

namespace Comuki.Modules.Proxy.Application.Models;

/// <summary>
/// Non-reversible dashboard id of a virtual key. Keys have no durable id of
/// their own — the bearer token is the only identity — so the admin surface
/// addresses them by a truncated SHA-256 fingerprint of the token. The
/// fingerprint cannot be reversed into the token, and the raw token never
/// leaves the store.
/// </summary>
public static class VirtualKeyFingerprint
{
    /// <summary>Fingerprint length (Base64Url chars of the leading SHA-256 bytes).</summary>
    public const int Length = 22;

    /// <summary>Derives the stable fingerprint of a key's token.</summary>
    /// <param name="token">Raw bearer token the key is addressed by.</param>
    public static string Of(string token)
    {
        var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));
        return Base64Url.EncodeToString(hash)[..Length];
    }

    /// <summary>Display prefix of a token — the first characters a dashboard may show after the key is stored.</summary>
    /// <param name="token">Raw bearer token.</param>
    public static string PrefixOf(string token)
    {
        return token.Length <= 4 ? token : token[..4];
    }
}

using System.Buffers.Text;
using System.Security.Cryptography;

namespace Comuki.Modules.Identity.Domain.ApiKeys;

/// <summary>
/// An opaque API-key token: <c>ck_</c> + 8-char public prefix + 43-char
/// base64url secret (256 bits). The prefix is the indexed lookup handle;
/// only the HMAC of the whole token is stored. Parsing never throws —
/// a malformed bearer is an authentication failure, not an exception.
/// </summary>
/// <param name="Prefix"></param>
/// <param name="Secret"></param>
public readonly record struct ApiKeyToken(string Prefix, string Secret)
{
    /// <summary>The literal token prefix that identifies a Comuki API key.</summary>
    public const string TokenPrefix = "ck_";

    /// <summary>Length of the public lookup prefix.</summary>
    public const int PrefixLength = 8;

    /// <summary>Length of the base64url secret (32 bytes).</summary>
    public const int SecretLength = 43;

    private const string PrefixAlphabet = "abcdefghijklmnopqrstuvwxyz0123456789";

    /// <summary>Generates a fresh cryptographically random token.</summary>
    public static ApiKeyToken New()
    {
        var prefixChars = new char[PrefixLength];
        foreach (ref var character in prefixChars.AsSpan())
        {
            character = PrefixAlphabet[RandomNumberGenerator.GetInt32(PrefixAlphabet.Length)];
        }

        var secret = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

        return new ApiKeyToken(new string(prefixChars), secret);
    }

    /// <summary>Parses a presented bearer token; null when malformed.</summary>
    /// <param name="token"></param>
    /// <returns></returns>
    public static ApiKeyToken? Parse(string token)
    {
        if (!token.StartsWith(TokenPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var body = token[TokenPrefix.Length..];
        if (body.Length != PrefixLength + SecretLength)
        {
            return null;
        }

        var prefix = body[..PrefixLength];
        var secret = body[PrefixLength..];

        foreach (var character in prefix)
        {
            var isAllowed = char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character);
            if (!isAllowed)
            {
                return null;
            }
        }

        foreach (var character in secret)
        {
            var isAllowed = char.IsAsciiLetterOrDigit(character) || character is '-' or '_';
            if (!isAllowed)
            {
                return null;
            }
        }

        return new ApiKeyToken(prefix, secret);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"{TokenPrefix}{Prefix}{Secret}";
    }
}

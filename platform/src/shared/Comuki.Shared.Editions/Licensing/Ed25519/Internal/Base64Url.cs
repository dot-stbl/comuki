namespace Comuki.Shared.Editions.Licensing.Ed25519.Internal;

/// <summary>
/// Base64url helpers shared by <see cref="Ed25519LicenseSigner"/> and
/// <see cref="Ed25519LicenseProvider"/>. Hand-rolled, not BCL
/// <c>Base64Url</c>: the wire format is two-part
/// <c>base64url(payload).base64url(signature)</c> with the
/// <c>=</c> padding stripped on encode and restored on decode. ~10 lines
/// each direction.
/// </summary>
internal static class Base64Url
{
    /// <summary>Encodes <paramref name="bytes"/> as base64url without padding.</summary>
    public static string Encode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    /// <summary>Decodes a base64url string back to bytes; restores the <c>=</c> padding that the encoder stripped.</summary>
    public static byte[] Decode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        var missing = (4 - padded.Length % 4) % 4;
        return missing is 0
            ? Convert.FromBase64String(padded)
            : Convert.FromBase64String(padded + new string('=', missing));
    }
}

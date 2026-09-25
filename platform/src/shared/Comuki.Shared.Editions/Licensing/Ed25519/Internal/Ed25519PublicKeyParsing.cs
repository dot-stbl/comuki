namespace Comuki.Shared.Editions.Licensing.Ed25519.Internal;

/// <summary>
/// Single source of truth for parsing an operator-supplied Ed25519
/// public key string (base64 of a 32-byte verifying key) — used by
/// <see cref="Options.LicenseOptionsValidator"/> at boot to fail loud
/// on typos and by <see cref="Installers.ComukiEditionsInstaller"/> at
/// DI build time to construct the
/// <see cref="Ed25519LicenseProvider"/> with the dev-overlay key. Lives
/// in the <c>Internal</c> namespace because no external caller needs
/// it; both consumers live in this assembly.
/// </summary>
internal static class Ed25519PublicKeyParsing
{
    /// <summary>Ed25519 verifying key length in bytes; every successful parse yields exactly this size.</summary>
    public const int KeyLength = 32;

    /// <summary>
    /// Attempts to decode <paramref name="encoded"/> as the base64 of a
    /// 32-byte Ed25519 public key. Returns <c>false</c> when the input
    /// is null, whitespace, not valid base64, or decodes to the wrong
    /// length — every shape of operator typo. The caller is the one
    /// that owns the "and what to do about it" message.
    /// </summary>
    /// <param name="encoded">The raw config value (trimmed by the caller if desired — this method trims itself).</param>
    /// <param name="publicKey">The decoded 32-byte key when the method returns <c>true</c>; otherwise <c>null</c>.</param>
    /// <returns><c>true</c> when <paramref name="encoded"/> is a valid base64-of-32-bytes Ed25519 verifying key.</returns>
    public static bool TryDecode(string? encoded, out byte[]? publicKey)
    {
        publicKey = null;

        if (string.IsNullOrWhiteSpace(encoded))
        {
            return false;
        }

        byte[] decoded;
        try
        {
            decoded = Convert.FromBase64String(encoded.Trim());
        }
        catch (FormatException)
        {
            return false;
        }

        if (decoded.Length != KeyLength)
        {
            return false;
        }

        publicKey = decoded;
        return true;
    }
}

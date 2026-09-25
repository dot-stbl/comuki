namespace Comuki.Shared.Editions.Installers;

/// <summary>
/// PLACEHOLDER production verifying key. Generated once for this change
/// with <c>Ed25519LicenseSigner.GenerateKeyPair()</c>; the matching
/// private key was discarded immediately and was never written to disk,
/// a comment, or version control — nobody can mint a license this key
/// accepts. This constant exists only so
/// <see cref="ComukiEditionsInstaller"/> has <em>a</em> real, well-formed
/// Ed25519 public key to compile and test against today. Replace it
/// with the actual business signing key's public half before any
/// customer-facing release — that key is generated and held completely
/// outside this repository.
/// </summary>
internal static class ProductionEd25519PublicKey
{
    /// <summary>32-byte Ed25519 verifying public key, base64-encoded.</summary>
    public static readonly byte[] Value = Convert.FromBase64String("aDdzI6q/Yg7beg3WoLAg40yLQ4tanoFZqB0PPSufSzM=");
}

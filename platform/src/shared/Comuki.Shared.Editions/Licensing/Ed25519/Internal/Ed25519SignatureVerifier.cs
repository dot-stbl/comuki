using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace Comuki.Shared.Editions.Licensing.Ed25519.Internal;

/// <summary>
/// Verifies an Ed25519 signature over a payload using BouncyCastle. Any
/// exception thrown while constructing the key material or running the
/// verifier is translated to <c>false</c> — the caller maps that to
/// <c>"signature mismatch"</c>. The translation is deliberate: a
/// wrong-length key, a malformed signature half, or an internal BCL
/// detail is indistinguishable from a forged signature from the
/// caller's perspective.
/// </summary>
internal static class Ed25519SignatureVerifier
{
    /// <summary>Returns <c>true</c> when <paramref name="signatureBytes"/> is a valid Ed25519 signature over <paramref name="payloadBytes"/> under <paramref name="publicKey"/>.</summary>
    public static bool Verify(byte[] publicKey, byte[] payloadBytes, byte[] signatureBytes)
    {
        try
        {
            var verifier = new Ed25519Signer();
            verifier.Init(forSigning: false, new Ed25519PublicKeyParameters(publicKey, 0));
            verifier.BlockUpdate(payloadBytes, 0, payloadBytes.Length);
            return verifier.VerifySignature(signatureBytes);
        }
        catch
        {
            return false;
        }
    }
}

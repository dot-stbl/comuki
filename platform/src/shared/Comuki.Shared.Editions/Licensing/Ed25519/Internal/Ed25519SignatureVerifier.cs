using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace Comuki.Shared.Editions.Licensing.Ed25519.Internal;

/// <summary>
/// Verifies an Ed25519 signature over a payload using BouncyCastle. A
/// wrong-length public key or signature half is translated to
/// <c>false</c> — the caller maps that to
/// <see cref="LicenseInvalidReason.BadSignature"/>. The translation is
/// deliberate: a wrong-length key, a malformed signature half, or a
/// forged signature is indistinguishable from the caller's perspective,
/// and BouncyCastle surfaces all three as <see cref="ArgumentException"/>
/// from the parameter / signature array length checks.
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
        catch (ArgumentException)
        {
            return false;
        }
    }
}

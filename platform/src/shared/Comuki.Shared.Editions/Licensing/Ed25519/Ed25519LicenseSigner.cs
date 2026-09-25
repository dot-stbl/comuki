using System.Text.Json;
using Comuki.Shared.Editions.Licensing.Audiences;
using Comuki.Shared.Editions.Licensing.Ed25519.Internal;
using Comuki.Shared.Editions.Licensing.Grants;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;

namespace Comuki.Shared.Editions.Licensing.Ed25519;

/// <summary>
/// Sign side of the Ed25519 license format: produces the
/// two-part token string <c>base64url(payload).base64url(signature)</c>
/// from a <see cref="LicenseGrant"/> and a 32-byte private-key seed.
/// No verify logic — the verifier is its counterpart in
/// <see cref="Ed25519LicenseProvider"/>, and the two share the
/// <c>LicensePayload</c> + <c>Base64Url</c> types so the two halves
/// cannot drift.
/// <para>
/// Lives in the shared library (not a mint tool) so the dev mint tool
/// and the test fixtures can both reuse the exact same signer every
/// license is built with — a single source of "how a token is
/// produced" matching the single source of "how a token is verified".
/// Inert without an actual production private key; that key is a later
/// chunk's concern.
/// </para>
/// </summary>
public static class Ed25519LicenseSigner
{
    /// <summary>Generates a fresh Ed25519 keypair via BouncyCastle's secure-random-backed constructor.</summary>
    /// <returns>An <see cref="Ed25519KeyPair"/> holding the 32-byte public key and 32-byte private-key seed.</returns>
    public static Ed25519KeyPair GenerateKeyPair()
    {
        var privateKey = new Ed25519PrivateKeyParameters(new SecureRandom());

        return new Ed25519KeyPair(
            PublicKey: privateKey.GeneratePublicKey().GetEncoded(),
            PrivateKeySeed: privateKey.GetEncoded());
    }

    /// <summary>Signs <paramref name="grant"/> with <paramref name="privateKeySeed"/>, returning the two-part token string.</summary>
    /// <param name="grant">The license to sign.</param>
    /// <param name="privateKeySeed">The 32-byte Ed25519 private-key seed.</param>
    /// <returns>The compact token: <c>base64url(payloadBytes).base64url(signatureBytes)</c>.</returns>
    public static string Sign(LicenseGrant grant, ReadOnlySpan<byte> privateKeySeed)
    {
        // The audience VALUE is written only when the grant names Dev;
        // a production grant serializes `"audience": null` (Web defaults
        // write nulls). That is NOT byte-identical to pre-audience
        // builds, but it is verify-compatible both ways: the verifier
        // maps a missing field and an explicit null to Production, so
        // historical tokens keep verifying and new tokens verify on
        // older builds that ignore the field.
        var payload = new LicensePayload
        {
            Org = grant.Org,
            Edition = grant.Tier.Code,
            Seats = grant.Seats,
            NotBefore = grant.NotBefore,
            Expiry = grant.Expiry,
            Mode = grant.Mode.Value,
            Audience = grant.Audience is { } audience && audience == LicenseAudience.Dev
                ? audience.Value
                : null,
            Features = grant.Features,
            Limits = grant.Limits,
        };

        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonSerializerOptions.Web);

        var signer = new Ed25519Signer();
        signer.Init(forSigning: true, new Ed25519PrivateKeyParameters(privateKeySeed.ToArray(), 0));
        signer.BlockUpdate(payloadBytes, 0, payloadBytes.Length);

        return Base64Url.Encode(payloadBytes) + "." + Base64Url.Encode(signer.GenerateSignature());
    }
}

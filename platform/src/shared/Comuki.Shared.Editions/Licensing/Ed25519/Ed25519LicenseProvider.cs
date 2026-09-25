using System.Text.Json;
using Comuki.Shared.Editions.Licensing.Ed25519.Internal;
using Comuki.Shared.Editions.Licensing.Modes;
using Comuki.Shared.Editions.Tiers;

namespace Comuki.Shared.Editions.Licensing.Ed25519;

/// <summary>
/// Verify side of the Ed25519 license format. Holds the 32-byte
/// verifying public key (never the private one) and an injected
/// <see cref="TimeProvider"/>; the caller is always responsible for
/// which key is in play — there is no embedded production key in this
/// chunk. The DI installer (chunk 2) is what binds the real one.
/// </summary>
public sealed class Ed25519LicenseProvider : ILicenseProvider
{
    private readonly byte[] publicKey;
    private readonly TimeProvider clock;

    /// <summary>Convenience constructor for the production path: builds the provider with the system clock.</summary>
    /// <param name="publicKey">The 32-byte Ed25519 verifying public key.</param>
    public Ed25519LicenseProvider(ReadOnlySpan<byte> publicKey)
        : this(publicKey, TimeProvider.System)
    {
    }

    /// <summary>Full constructor: explicit clock so tests can pin <see cref="LicenseKey.VerifiedAt"/>.</summary>
    /// <param name="publicKey">The 32-byte Ed25519 verifying public key.</param>
    /// <param name="clock">Clock used to stamp <see cref="LicenseKey.VerifiedAt"/>.</param>
    public Ed25519LicenseProvider(ReadOnlySpan<byte> publicKey, TimeProvider clock)
    {
        this.publicKey = publicKey.ToArray();
        this.clock = clock;
    }

    /// <inheritdoc />
    /// <exception cref="LicenseInvalidException"><paramref name="token"/> is shaped wrong, signature-mismatched, malformed, or names an unknown edition.</exception>
    public LicenseKey Verify(string token)
    {
        if (token.Count(static c => c == '.') != 1)
        {
            throw LicenseInvalidException.MalformedTokenShape();
        }

        var separatorIndex = token.IndexOf('.');
        var payloadEncoded = token[..separatorIndex];
        var signatureEncoded = token[(separatorIndex + 1)..];

        byte[] payloadBytes;
        byte[] signatureBytes;
        try
        {
            payloadBytes = Base64Url.Decode(payloadEncoded);
            signatureBytes = Base64Url.Decode(signatureEncoded);
        }
        catch (FormatException)
        {
            throw LicenseInvalidException.MalformedTokenShape();
        }

        if (!Ed25519SignatureVerifier.Verify(publicKey, payloadBytes, signatureBytes))
        {
            throw LicenseInvalidException.BadSignature();
        }

        LicensePayload? payload;
        try
        {
            // boundary: System.Text.Json surfaces a malformed payload as a raw JsonException;
            // translate to LicenseInvalidException so the verifier's documented contract holds.
            payload = JsonSerializer.Deserialize<LicensePayload>(payloadBytes, JsonSerializerOptions.Web);
        }
        catch (JsonException exception)
        {
            throw LicenseInvalidException.MalformedPayload(innerException: exception);
        }

        if (payload is not { } p
            || string.IsNullOrWhiteSpace(p.Org)
            || string.IsNullOrWhiteSpace(p.Edition)
            || p.Expiry is null
            || string.IsNullOrWhiteSpace(p.Mode))
        {
            throw LicenseInvalidException.MalformedPayload();
        }

        var mode = LicenseMode.TryParse(p.Mode, out var parsedMode)
            ? parsedMode
            : throw LicenseInvalidException.MalformedPayload();

        var tier = EditionTiers.TryGetByCode(p.Edition, out var resolvedTier)
            ? resolvedTier
            : throw LicenseInvalidException.UnknownEditionCode();

        return new LicenseKey(
            Tier: tier,
            Org: p.Org,
            NotBefore: p.NotBefore,
            Expiry: p.Expiry.Value,
            Mode: mode,
            Features: p.Features ?? [],
            Limits: p.Limits ?? new Dictionary<string, int>(),
            VerifiedAt: clock.GetUtcNow(),
            VerifiedWith: Ed25519PublicKeyFingerprint.Compute(publicKey));
    }
}

using System.Text.Json;
using Comuki.Shared.Editions.Licensing.Audiences;
using Comuki.Shared.Editions.Licensing.Ed25519.Internal;
using Comuki.Shared.Editions.Licensing.Modes;
using Comuki.Shared.Editions.Tiers;

namespace Comuki.Shared.Editions.Licensing.Ed25519;

/// <summary>
/// Verify side of the Ed25519 license format. Holds the 32-byte
/// production verifying public key (never the private one) and an
/// injected <see cref="TimeProvider"/>; the caller is always
/// responsible for which key is in play — there is no embedded
/// production key in this chunk. The DI installer (chunk 2) is what
/// binds the real one.
/// <para>
/// Optionally holds a second 32-byte dev-overlay verifying key (issue
/// / add-editions-dev-license). When configured, a token whose payload
/// carries <c>audience: "dev"</c> is verified against the dev key; the
/// production key never accepts a dev claim and vice versa. A missing
/// <c>devPublicKey</c> argument (or an empty span) means "no dev
/// licenses trusted here" — the dev path throws
/// <see cref="LicenseInvalidException"/> the same way a wrong-key
/// signature mismatch does.
/// </para>
/// </summary>
public sealed class Ed25519LicenseProvider : ILicenseProvider
{
    /// <summary>Length in bytes of an Ed25519 verifying key; used to detect "dev key not configured" callers.</summary>
    public const int PublicKeyLength = 32;

    private readonly byte[] publicKey;
    private readonly byte[]? devPublicKey;
    private readonly TimeProvider clock;

    /// <summary>Convenience constructor for the production path: builds the provider with the system clock and no dev key.</summary>
    /// <param name="publicKey">The 32-byte Ed25519 verifying public key.</param>
    public Ed25519LicenseProvider(ReadOnlySpan<byte> publicKey)
        : this(publicKey, TimeProvider.System)
    {
    }

    /// <summary>Full constructor (single-key): explicit clock so tests can pin <see cref="LicenseKey.VerifiedAt"/>.</summary>
    /// <param name="publicKey">The 32-byte Ed25519 verifying public key.</param>
    /// <param name="clock">Clock used to stamp <see cref="LicenseKey.VerifiedAt"/>.</param>
    public Ed25519LicenseProvider(ReadOnlySpan<byte> publicKey, TimeProvider clock)
    {
        if (publicKey.Length != PublicKeyLength)
        {
            throw new ArgumentException(
                $"Ed25519 public key must be {PublicKeyLength} bytes, was {publicKey.Length}.",
                nameof(publicKey));
        }

        this.publicKey = publicKey.ToArray();
        this.clock = clock;
    }

    /// <summary>Two-key constructor: production key, optional dev key, and an explicit clock.</summary>
    /// <param name="publicKey">The 32-byte production-audience Ed25519 verifying key.</param>
    /// <param name="devPublicKey">
    /// The 32-byte dev-audience Ed25519 verifying key, or an empty
    /// span when the deployment does not configure a dev license. An
    /// empty span REJECTS every dev-audience token (verified path
    /// raises <see cref="LicenseInvalidException"/>); production tokens
    /// still verify against <paramref name="publicKey"/> regardless.
    /// </param>
    /// <param name="clock">Clock used to stamp <see cref="LicenseKey.VerifiedAt"/>.</param>
    /// <exception cref="ArgumentException">Either key is not exactly <see cref="PublicKeyLength"/> bytes.</exception>
    public Ed25519LicenseProvider(ReadOnlySpan<byte> publicKey, ReadOnlySpan<byte> devPublicKey, TimeProvider clock)
    {
        if (publicKey.Length != PublicKeyLength)
        {
            throw new ArgumentException(
                $"Ed25519 public key must be {PublicKeyLength} bytes, was {publicKey.Length}.",
                nameof(publicKey));
        }

        if (devPublicKey.Length is not 0 and not PublicKeyLength)
        {
            throw new ArgumentException(
                $"Ed25519 dev public key must be {PublicKeyLength} bytes, was {devPublicKey.Length}.",
                nameof(devPublicKey));
        }

        this.publicKey = publicKey.ToArray();
        this.devPublicKey = devPublicKey.Length == 0 ? null : devPublicKey.ToArray();
        this.clock = clock;
    }

    /// <inheritdoc />
    /// <exception cref="LicenseInvalidException"><paramref name="token"/> is shaped wrong, signature-mismatched, malformed, names an unknown edition, or carries an audience claim the configured keys cannot authenticate.</exception>
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

        // Parse the payload FIRST, then select the verifying key from
        // the audience claim, THEN verify the signature with the
        // selected key. This is sound because the Ed25519 signature
        // covers the exact payload bytes — including the audience field
        // when present. A production-key signature authenticates the
        // payload bytes that produced it; flipping the audience field
        // changes the payload bytes, so a production key cannot
        // authenticate a dev claim and vice versa. Selecting the key
        // from the untrusted payload is therefore safe: only a holder
        // of the matching private key can mint a token whose signature
        // passes verification under the corresponding public key.
        var payload = Ed25519LicensePayloadParsing.TryParse(payloadBytes);
        if (payload is not { } p
            || string.IsNullOrWhiteSpace(p.Org)
            || string.IsNullOrWhiteSpace(p.Edition)
            || p.Expiry is null
            || string.IsNullOrWhiteSpace(p.Mode))
        {
            throw LicenseInvalidException.MalformedPayload();
        }

        var audience = p.Audience is null
            ? LicenseAudience.Production
            : LicenseAudience.TryParse(p.Audience, out var parsedAudience)
                ? parsedAudience
                : throw LicenseInvalidException.UnknownAudience();

        byte[] verifyingKey;
        if (audience == LicenseAudience.Dev)
        {
            if (devPublicKey is null)
            {
                throw LicenseInvalidException.DevAudienceNotTrusted();
            }

            verifyingKey = devPublicKey;
        }
        else
        {
            verifyingKey = publicKey;
        }

        if (!Ed25519SignatureVerifier.Verify(verifyingKey, payloadBytes, signatureBytes))
        {
            throw LicenseInvalidException.BadSignature();
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
            Audience: audience,
            Features: p.Features ?? [],
            Limits: p.Limits ?? new Dictionary<string, int>(),
            VerifiedAt: clock.GetUtcNow(),
            VerifiedWith: Ed25519PublicKeyFingerprint.Compute(verifyingKey));
    }
}

/// <summary>
/// Payload parse isolated from <see cref="Ed25519LicenseProvider.Verify"/>
/// so the untrusted-bytes-first flow can translate a <c>JsonException</c>
/// into the same <see cref="LicenseInvalidException"/>("malformed payload")
/// every other payload-shape violation produces.
/// </summary>
file static class Ed25519LicensePayloadParsing
{
    /// <summary>
    /// Deserializes the payload bytes; returns <c>null</c> when the bytes
    /// deserialize to JSON <c>null</c>. A non-JSON payload rethrows as
    /// <see cref="LicenseInvalidException.MalformedPayload"/> with the
    /// underlying <c>JsonException</c> preserved as the inner exception.
    /// </summary>
    /// <param name="payloadBytes">The raw (unverified) payload half of the token.</param>
    public static LicensePayload? TryParse(byte[] payloadBytes)
    {
        try
        {
            return JsonSerializer.Deserialize<LicensePayload>(payloadBytes, JsonSerializerOptions.Web);
        }
        catch (JsonException exception)
        {
            throw LicenseInvalidException.MalformedPayload(innerException: exception);
        }
    }
}

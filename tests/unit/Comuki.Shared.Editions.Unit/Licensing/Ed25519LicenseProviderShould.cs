using Comuki.Shared.Editions.Licensing;
using Comuki.Shared.Editions.Licensing.Audiences;
using Comuki.Shared.Editions.Licensing.Ed25519;
using Comuki.Shared.Editions.Licensing.Ed25519.Internal;
using Comuki.Shared.Editions.Licensing.Grants;
using Comuki.Shared.Editions.Licensing.Modes;
using Comuki.Shared.Editions.Tiers;
using Comuki.Shared.Editions.Unit.Fixtures;
using Shouldly;
using Xunit;

namespace Comuki.Shared.Editions.Unit.Licensing;

/// <summary>
/// Ed25519LicenseProvider tests — the verify side of the wire format.
/// Every test generates a fresh keypair so the suite has no hard-coded
/// crypto material; round-trip + tamper cases share one pair per test
/// method, the wrong-key case generates a second pair inline.
/// </summary>
public sealed class Ed25519LicenseProviderShould
{
    private static readonly DateTimeOffset expiry = new(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static LicenseGrant SampleGrant(EditionTier? tier = null)
    {
        return new(
            Org: "Acme Inc",
            Tier: tier ?? EditionTiers.Team,
            Expiry: expiry,
            Mode: LicenseMode.ImplicitByRank,
            Features: ["enterprise-sso", "multi-repo"],
            Limits: new Dictionary<string, int> { ["projects"] = 10 });
    }

    [Fact(DisplayName = "Given a valid signed grant, when Verify runs, then every LicenseKey field round-trips from the grant")]
    public void ValidTokenRoundTripsFields()
    {
        var (signingPublic, privateSeed) = Ed25519LicenseSigner.GenerateKeyPair();
        var grant = SampleGrant();
        var token = Ed25519LicenseSigner.Sign(grant, privateSeed);

        var provider = new Ed25519LicenseProvider(signingPublic);
        var key = provider.Verify(token);

        key.Tier.Rank.ShouldBe(EditionTiers.Team.Rank);
        key.Tier.Code.ShouldBe("team");
        key.Org.ShouldBe("Acme Inc");
        key.Expiry.ShouldBe(expiry);
        key.Mode.ShouldBe(LicenseMode.ImplicitByRank);
        key.Features.ShouldBe(["enterprise-sso", "multi-repo"]);
        key.Limits.ShouldBe(new Dictionary<string, int> { ["projects"] = 10 });
        key.NotBefore.ShouldBeNull();
        key.VerifiedWith.ShouldNotBeNullOrEmpty();
    }

    [Fact(DisplayName = "Given a valid signed token whose payload bytes are tampered, when Verify runs, then signature mismatch is thrown")]
    public void TamperedPayloadThrowsSignatureMismatch()
    {
        var (signingPublic, privateSeed) = Ed25519LicenseSigner.GenerateKeyPair();
        var grant = SampleGrant();
        var token = Ed25519LicenseSigner.Sign(grant, privateSeed);

        var separatorIndex = token.IndexOf('.');
        var payloadEncoded = token[..separatorIndex];
        var signatureEncoded = token[(separatorIndex + 1)..];
        var payloadBytes = Base64Url.Decode(payloadEncoded);
        // Flip the case of the first letter inside the org value — the
        // payload stays well-formed JSON (a byte flip on a structural
        // char would fail as "malformed payload" before the signature
        // check, which the payload-order tests already cover), so the
        // tamper is caught where this test aims: at the signature.
        var orgIndex = Array.IndexOf(payloadBytes, (byte)'A');
        payloadBytes[orgIndex] ^= 0x20;
        var tamperedToken = Base64Url.Encode(payloadBytes) + "." + signatureEncoded;

        var provider = new Ed25519LicenseProvider(signingPublic);

        var ex = Should.Throw<LicenseInvalidException>(() => provider.Verify(tamperedToken));
        ex.Message.ShouldContain("signature mismatch");
    }

    [Fact(DisplayName = "Given a valid signed token whose signature half is tampered, when Verify runs, then signature mismatch is thrown")]
    public void TamperedSignatureThrowsSignatureMismatch()
    {
        var (signingPublic, privateSeed) = Ed25519LicenseSigner.GenerateKeyPair();
        var grant = SampleGrant();
        var token = Ed25519LicenseSigner.Sign(grant, privateSeed);

        var separatorIndex = token.IndexOf('.');
        var payloadEncoded = token[..separatorIndex];
        var signatureEncoded = token[(separatorIndex + 1)..];
        var signatureBytes = Base64Url.Decode(signatureEncoded);
        signatureBytes[0] ^= 0x01;
        var tamperedToken = payloadEncoded + "." + Base64Url.Encode(signatureBytes);

        var provider = new Ed25519LicenseProvider(signingPublic);

        var ex = Should.Throw<LicenseInvalidException>(() => provider.Verify(tamperedToken));
        ex.Message.ShouldContain("signature mismatch");
    }

    [Fact(DisplayName = "Given a token signed with keypair A and verified with keypair B's public key, when Verify runs, then signature mismatch is thrown")]
    public void WrongVerifyingKeyThrowsSignatureMismatch()
    {
        var (_, privateSeedA) = Ed25519LicenseSigner.GenerateKeyPair();
        var (publicKeyB, _) = Ed25519LicenseSigner.GenerateKeyPair();

        var grant = SampleGrant();
        var token = Ed25519LicenseSigner.Sign(grant, privateSeedA);

        var provider = new Ed25519LicenseProvider(publicKeyB);

        var ex = Should.Throw<LicenseInvalidException>(() => provider.Verify(token));
        ex.Message.ShouldContain("signature mismatch");
    }

    [Fact(DisplayName = "Given a token with zero dots, when Verify runs, then malformed token shape is thrown")]
    public void ZeroDotsThrowsMalformedShape()
    {
        var provider = new Ed25519LicenseProvider(new byte[32]);

        var ex = Should.Throw<LicenseInvalidException>(() => provider.Verify("nodots"));
        ex.Message.ShouldContain("malformed token shape");
    }

    [Fact(DisplayName = "Given a token with two dots, when Verify runs, then malformed token shape is thrown")]
    public void TwoDotsThrowsMalformedShape()
    {
        var provider = new Ed25519LicenseProvider(new byte[32]);

        var ex = Should.Throw<LicenseInvalidException>(() => provider.Verify("a.b.c"));
        ex.Message.ShouldContain("malformed token shape");
    }

    [Fact(DisplayName = "Given a token with non-base64url halves, when Verify runs, then malformed token shape is thrown")]
    public void MalformedBase64ThrowsMalformedShape()
    {
        var provider = new Ed25519LicenseProvider(new byte[32]);

        var ex = Should.Throw<LicenseInvalidException>(
            () => provider.Verify("not-valid-base64!!!.also-not-valid!!!"));
        ex.Message.ShouldContain("malformed token shape");
    }

    [Fact(DisplayName = "Given a grant with an unknown edition code, when Verify runs, then unknown edition code is thrown")]
    public void UnknownEditionCodeThrows()
    {
        var (signingPublic, privateSeed) = Ed25519LicenseSigner.GenerateKeyPair();
        var bogusTier = new EditionTier(99, "bogus-tier");
        var grant = SampleGrant(bogusTier);
        var token = Ed25519LicenseSigner.Sign(grant, privateSeed);

        var provider = new Ed25519LicenseProvider(signingPublic);

        var ex = Should.Throw<LicenseInvalidException>(() => provider.Verify(token));
        ex.Message.ShouldContain("unknown edition code");
    }

    [Fact(DisplayName = "Given an unrecognised mode string, when LicenseMode.TryParse runs, then it returns false and yields the Unspecified sentinel")]
    public void UnrecognisedModeStringFailsTryParse()
    {
        var hit = LicenseMode.TryParse("definitely-not-a-mode", out var mode);

        hit.ShouldBeFalse();
        mode.ShouldBe(LicenseMode.Unspecified);
    }

    [Fact(DisplayName = "Given a non-base64url signature half on a well-shaped payload, when Verify runs, then malformed token shape is thrown")]
    public void MalformedSignatureHalfThrowsMalformedShape()
    {
        var provider = new Ed25519LicenseProvider(new byte[32]);

        var ex = Should.Throw<LicenseInvalidException>(
            () => provider.Verify("aGVsbG8.!!!not-base64!!!"));
        ex.Message.ShouldContain("malformed token shape");
    }

    [Fact(DisplayName = "Given an injected clock, when Verify runs, then LicenseKey.VerifiedAt equals that clock's instant")]
    public void VerifiedAtUsesInjectedClock()
    {
        var (signingPublic, privateSeed) = Ed25519LicenseSigner.GenerateKeyPair();
        var grant = SampleGrant();
        var token = Ed25519LicenseSigner.Sign(grant, privateSeed);

        var fixedNow = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(fixedNow);
        var provider = new Ed25519LicenseProvider(signingPublic, clock);

        var key = provider.Verify(token);

        key.VerifiedAt.ShouldBe(fixedNow);
    }

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return now;
        }
    }

    [Fact(DisplayName = "Given a valid dev-audience token signed by the dev key and a provider with both keys, when Verify runs, then Tier is Team, Audience is Dev, and VerifiedWith equals the dev key fingerprint")]
    public void DevAudienceTokenVerifiesAgainstDevKey()
    {
        var (prodPublic, _) = Ed25519LicenseSigner.GenerateKeyPair();
        var devPublic = TestLicense.DevPublicKey;
        var token = TestLicense.WithDev(EditionTiers.Team);

        var provider = new Ed25519LicenseProvider(prodPublic, devPublic, TimeProvider.System);
        var key = provider.Verify(token);

        key.Tier.Rank.ShouldBe(EditionTiers.Team.Rank);
        key.Audience.ShouldBe(LicenseAudience.Dev);
        key.VerifiedWith.ShouldBe(Ed25519PublicKeyFingerprint.Compute(devPublic));
    }

    [Fact(DisplayName = "Given a valid dev-audience token and a provider constructed WITHOUT a dev key, when Verify runs, then LicenseInvalidException is thrown")]
    public void DevAudienceTokenRejectedWhenNoDevKeyConfigured()
    {
        var (prodPublic, _) = Ed25519LicenseSigner.GenerateKeyPair();
        var token = TestLicense.WithDev(EditionTiers.Team);

        var provider = new Ed25519LicenseProvider(prodPublic, TimeProvider.System);

        var ex = Should.Throw<LicenseInvalidException>(() => provider.Verify(token));
        ex.Message.ShouldContain("dev audience not trusted here");
    }

    [Fact(DisplayName = "Given a dev-audience token signed by the MAIN (production-position) keypair, when a provider with both keys verifies it, then LicenseInvalidException is thrown")]
    public void DevAudienceTokenSignedByProdKeyIsRejected()
    {
        var (prodPublic, _) = Ed25519LicenseSigner.GenerateKeyPair();
        var devPublic = TestLicense.DevPublicKey;

        var bogusDevToken = TestLicense.With(
            EditionTiers.Team,
            audience: LicenseAudience.Dev);

        var provider = new Ed25519LicenseProvider(prodPublic, devPublic, TimeProvider.System);

        var ex = Should.Throw<LicenseInvalidException>(() => provider.Verify(bogusDevToken));
        ex.Message.ShouldContain("signature mismatch");
    }

    [Fact(DisplayName = "Given a production (null-audience) token signed by the DEV keypair, when a provider with both keys verifies it, then LicenseInvalidException is thrown")]
    public void ProductionAudienceTokenSignedByDevKeyIsRejected()
    {
        var (prodPublic, _) = Ed25519LicenseSigner.GenerateKeyPair();
        var devPublic = TestLicense.DevPublicKey;

        var grant = new LicenseGrant(
            Org: "Rogue",
            Tier: EditionTiers.Team,
            Expiry: expiry,
            Mode: LicenseMode.ImplicitByRank);
        var token = Ed25519LicenseSigner.Sign(grant, TestLicense.DevPrivateKeySeed);

        var provider = new Ed25519LicenseProvider(prodPublic, devPublic, TimeProvider.System);

        var ex = Should.Throw<LicenseInvalidException>(() => provider.Verify(token));
        ex.Message.ShouldContain("signature mismatch");
    }

    [Fact(DisplayName = "Given a token whose payload names an unknown audience string, when Verify runs, then LicenseInvalidException is thrown")]
    public void UnknownAudienceStringIsRejected()
    {
        var (signingPublic, privateSeed) = Ed25519LicenseSigner.GenerateKeyPair();
        var grant = SampleGrant();
        var token = Ed25519LicenseSigner.Sign(grant, privateSeed);

        // Tamper: rewrite the payload's audience field to an unknown
        // string, then re-sign so the verifier accepts the bytes. We
        // rebuild the payload directly to inject the field.
        var payload = new LicensePayload
        {
            Org = grant.Org,
            Edition = grant.Tier.Code,
            Expiry = grant.Expiry,
            Mode = grant.Mode.Value,
            Audience = "qa",
            Features = grant.Features,
            Limits = grant.Limits,
        };
        var payloadBytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(payload, System.Text.Json.JsonSerializerOptions.Web);
        var payloadEncoded = Base64Url.Encode(payloadBytes);
        var signing = new Org.BouncyCastle.Crypto.Signers.Ed25519Signer();
        signing.Init(forSigning: true, new Org.BouncyCastle.Crypto.Parameters.Ed25519PrivateKeyParameters(privateSeed, 0));
        signing.BlockUpdate(payloadBytes, 0, payloadBytes.Length);
        var tokenWithUnknownAudience = payloadEncoded + "." + Base64Url.Encode(signing.GenerateSignature());

        var provider = new Ed25519LicenseProvider(signingPublic, signingPublic, TimeProvider.System);

        var ex = Should.Throw<LicenseInvalidException>(() => provider.Verify(tokenWithUnknownAudience));
        ex.Message.ShouldContain("unknown audience");
    }
}

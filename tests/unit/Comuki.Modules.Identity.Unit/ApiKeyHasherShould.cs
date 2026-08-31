using Comuki.Modules.Identity.Application.ApiKeys;
using Comuki.Modules.Identity.Application.Options;
using Comuki.Modules.Identity.Domain.ApiKeys;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Identity.Unit;

/// <summary>
/// Hashing round-trip (T4.3): deterministic digest, constant-time verify
/// of the exact token, refusal of anything else, and token parsing that
/// rejects every malformed shape.
/// </summary>
public sealed class ApiKeyHasherShould
{
    private readonly ApiKeyHasher hasher = new(Options.Create(new ApiKeyOptions
    {
        Pepper = "unit-test-pepper-0123456789abcdef",
    }));

    [Fact(DisplayName = "Given a token, when hashed twice, then the digest is deterministic lowercase hex")]
    public void HashDeterministically()
    {
        var token = ApiKeyToken.New().ToString();

        var first = hasher.Hash(token);
        var second = hasher.Hash(token);

        first.ShouldBe(second);
        first.Length.ShouldBe(64);
        first.ShouldMatch("^[0-9a-f]{64}$");
    }

    [Fact(DisplayName = "Given a stored digest, when the same token is verified, then it matches")]
    public void VerifyRoundTrip()
    {
        var token = ApiKeyToken.New().ToString();

        hasher.Verify(token, hasher.Hash(token)).ShouldBeTrue();
    }

    [Fact(DisplayName = "Given a stored digest, when a different token is verified, then it does not match")]
    public void RefuseDifferentToken()
    {
        var token = ApiKeyToken.New();
        var tampered = token with { Secret = token.Secret[..^1] + (token.Secret[^1] == 'A' ? 'B' : 'A') };

        hasher.Verify(token.ToString(), hasher.Hash(tampered.ToString())).ShouldBeFalse();
    }

    [Theory(DisplayName = "Given malformed input, when verified, then it refuses without throwing")]
    [InlineData("")]
    [InlineData("ck_")]
    [InlineData("sk_short")]
    [InlineData("ck_abcdefghijklmnop")]
    [InlineData("ck_abcdefghijklmnopqrstuvwxABCDEFGHIJKLMNOPQ!")]
    public void RefuseMalformedTokens(string candidate)
    {
        ApiKeyToken.Parse(candidate).ShouldBeNull();
        hasher.Verify(candidate, hasher.Hash("ck_00000000aaaa")).ShouldBeFalse();
        hasher.Verify("ck_00000000aaaa", string.Empty).ShouldBeFalse();
    }

    [Fact(DisplayName = "Given pepper separation, when the same token is hashed with another pepper, then the digests differ")]
    public void BindDigestToPepper()
    {
        var otherHasher = new ApiKeyHasher(Options.Create(new ApiKeyOptions { Pepper = "another-pepper-0123456789abcdef" }));
        var token = ApiKeyToken.New().ToString();

        otherHasher.Hash(token).ShouldNotBe(hasher.Hash(token));
    }

    [Fact(DisplayName = "Given a fresh token, when parsed back, then prefix and secret round-trip")]
    public void RoundTripTokenShape()
    {
        var token = ApiKeyToken.New();

        var parsed = ApiKeyToken.Parse(token.ToString()).ShouldNotBeNull();

        parsed.ShouldBe(token);
        parsed.Prefix.Length.ShouldBe(ApiKeyToken.PrefixLength);
        parsed.Secret.Length.ShouldBe(ApiKeyToken.SecretLength);
    }
}

using Comuki.Modules.Proxy.Application.Models;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Proxy.Unit;

/// <summary>Fingerprint derivation of <see cref="VirtualKeyFingerprint"/>.</summary>
public sealed class VirtualKeyFingerprintShould
{
    [Fact(DisplayName = "Given a token, when fingerprinted, then the id is stable, url-safe and truncated")]
    public void DeriveStableFingerprint()
    {
        var fingerprint = VirtualKeyFingerprint.Of("vkey_test-token-material-1");

        fingerprint.Length.ShouldBe(VirtualKeyFingerprint.Length);
        fingerprint.ShouldNotBeNullOrEmpty();
        VirtualKeyFingerprint.Of("vkey_test-token-material-1").ShouldBe(fingerprint);
    }

    [Fact(DisplayName = "Given different tokens, when fingerprinted, then the ids differ")]
    public void DifferAcrossTokens()
    {
        VirtualKeyFingerprint.Of("vkey_a").ShouldNotBe(VirtualKeyFingerprint.Of("vkey_b"));
    }

    [Theory(DisplayName = "Given a token, when prefixed, then only the first four characters surface")]
    [InlineData("vkey_abcd1234567890", "vkey")]
    [InlineData("abc", "abc")]
    public void PrefixOnlyLeadingCharacters(string token, string expectedPrefix)
    {
        VirtualKeyFingerprint.PrefixOf(token).ShouldBe(expectedPrefix);
    }
}

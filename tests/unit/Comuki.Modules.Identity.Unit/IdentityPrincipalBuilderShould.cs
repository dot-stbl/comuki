using System.Security.Claims;
using Comuki.Modules.Identity.Domain.Users;
using Comuki.Modules.Identity.Infrastructure.Security;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Identity.Unit;

/// <summary>
/// <see cref="IdentityPrincipalBuilder"/> turns a <see cref="User"/> into the
/// cookie-scheme principal the auth handler signs. The builder is a pure
/// mapper: it does not gate on <c>Disabled</c> (the auth handler does that on
/// re-validation) and does not embed roles (the cookie scheme is user-only —
/// role escalation lives on the API-key scheme).
/// </summary>
public sealed class IdentityPrincipalBuilderShould
{
    private static readonly DateTimeOffset anchorTime = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Given an active user, when BuildForCookie runs, then the principal carries NameIdentifier, Email, Name and TokensVersion on the cookie scheme")]
    public void BuildForCookieReturnsPrincipalWithCoreClaims()
    {
        var user = User.Create("ada@example.com", "Ada Lovelace", "hash", anchorTime);

        var principal = IdentityPrincipalBuilder.BuildForCookie(user);
        var identity = principal.Identity.ShouldNotBeNull();

        identity.AuthenticationType.ShouldBe(AuthSchemes.Cookie);
        identity.IsAuthenticated.ShouldBeTrue();
        principal.FindFirstValue(ClaimTypes.NameIdentifier).ShouldBe(user.Id.Value.ToString());
        principal.FindFirstValue(ClaimTypes.Email).ShouldBe("ada@example.com");
        principal.FindFirstValue(ClaimTypes.Name).ShouldBe("Ada Lovelace");
        principal.FindFirstValue(IdentityClaimNames.TokensVersion).ShouldBe("1");
    }

    [Fact(DisplayName = "Given a disabled user, when BuildForCookie runs, then the principal is still constructed — Disabled is enforced at re-validation, not at issue time")]
    public void BuildForCookieDoesNotGateOnDisabledFlag()
    {
        var user = User.Create("ada@example.com", "Ada", "hash", anchorTime);
        user.Disable(anchorTime);

        var principal = IdentityPrincipalBuilder.BuildForCookie(user);
        var identity = principal.Identity.ShouldNotBeNull();

        identity.IsAuthenticated.ShouldBeTrue();
        principal.FindFirstValue(ClaimTypes.NameIdentifier).ShouldBe(user.Id.Value.ToString());
    }

    [Fact(DisplayName = "Given a user whose tokens version was bumped, when BuildForCookie runs, then the principal reflects the new version")]
    public void BuildForCookieReflectsBumpedTokensVersion()
    {
        var user = User.Create("ada@example.com", "Ada", "hash", anchorTime);
        user.BumpTokensVersion(anchorTime);
        user.BumpTokensVersion(anchorTime);

        var principal = IdentityPrincipalBuilder.BuildForCookie(user);

        principal.FindFirstValue(IdentityClaimNames.TokensVersion).ShouldBe("3");
    }

    [Fact(DisplayName = "Given a user whose email was upper-cased on input, when BuildForCookie runs, then the email claim is the lower-cased form (Create normalises)")]
    public void BuildForCookieUsesNormalizedEmailFromUser()
    {
        var user = User.Create("ADA@Example.COM", "Ada", "hash", anchorTime);

        var principal = IdentityPrincipalBuilder.BuildForCookie(user);

        principal.FindFirstValue(ClaimTypes.Email).ShouldBe("ada@example.com");
    }

    [Fact(DisplayName = "Given two principals built for the same user, when compared, then the NameIdentifier and TokensVersion claims round-trip identically")]
    public void BuildForCookieIsDeterministicForTheSameUser()
    {
        var user = User.Create("ada@example.com", "Ada", "hash", anchorTime);

        var first = IdentityPrincipalBuilder.BuildForCookie(user);
        var second = IdentityPrincipalBuilder.BuildForCookie(user);

        first.FindFirstValue(ClaimTypes.NameIdentifier).ShouldBe(second.FindFirstValue(ClaimTypes.NameIdentifier));
        first.FindFirstValue(IdentityClaimNames.TokensVersion).ShouldBe(second.FindFirstValue(IdentityClaimNames.TokensVersion));
        first.FindFirstValue(ClaimTypes.Email).ShouldBe(second.FindFirstValue(ClaimTypes.Email));
    }
}

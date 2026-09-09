using Comuki.Modules.Identity.Application.Oidc;
using Comuki.Modules.Identity.Application.Ports;
using Comuki.Modules.Identity.Domain.Users;
using Comuki.Shared.Kernel.Exceptions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Identity.Unit;

/// <summary>
/// Linker semantics (T4.6): an existing link wins, a matching email links
/// the known account, anything else provisions a password-less account.
/// Issue Q36 / v1.1: a disabled user cannot authenticate via OIDC even
/// when a configured IdP signs a valid subject for them.
/// </summary>
public sealed class OidcAccountLinkerShould
{
    private readonly IUserAccountStore userStore = Substitute.For<IUserAccountStore>();
    private readonly IOidcLinkStore linkStore = Substitute.For<IOidcLinkStore>();
    private readonly OidcAccountLinker linker;

    public OidcAccountLinkerShould()
    {
        linker = new OidcAccountLinker(userStore, linkStore, new FrozenTime());
    }

    [Fact(DisplayName = "Given an existing link, when the identity arrives again, then the linked account is returned without creating anything")]
    public async Task ReturnLinkedAccountAsync()
    {
        var user = User.Create("known@example.com", "Known", null, DateTimeOffset.UtcNow);
        var link = OidcLink.Create(user.Id, "keycloak", "sub-123", DateTimeOffset.UtcNow);
        _ = linkStore.FindAsync("keycloak", "sub-123", TestContext.Current.CancellationToken).Returns(link);
        _ = userStore.FindByIdAsync(user.Id, TestContext.Current.CancellationToken).Returns(user);

        var result = await linker.HandleAsync(new OidcLinkRequest("keycloak", "sub-123", "known@example.com", null, EmailVerified: true), TestContext.Current.CancellationToken);

        result.Created.ShouldBeFalse();
        result.User.Id.ShouldBe(user.Id);
        await userStore.DidNotReceiveWithAnyArgs().SaveAsync(default!, TestContext.Current.CancellationToken);
        await linkStore.DidNotReceiveWithAnyArgs().SaveAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact(DisplayName = "Given no link but a matching email, when the identity arrives, then the known account is linked")]
    public async Task LinkKnownAccountByEmailAsync()
    {
        var user = User.Create("known@example.com", "Known", null, DateTimeOffset.UtcNow);
        _ = linkStore.FindAsync("keycloak", "sub-777", TestContext.Current.CancellationToken).Returns((OidcLink?)null);
        _ = userStore.FindByEmailAsync("known@example.com", TestContext.Current.CancellationToken).Returns(user);

        var result = await linker.HandleAsync(new OidcLinkRequest("keycloak", "sub-777", "known@example.com", null, EmailVerified: true), TestContext.Current.CancellationToken);

        result.Created.ShouldBeFalse();
        result.User.Id.ShouldBe(user.Id);
        await linkStore.Received(1).SaveAsync(
            Arg.Is<OidcLink>(link => link.UserId == user.Id && link.Subject == "sub-777"),
            TestContext.Current.CancellationToken);
    }

    [Fact(DisplayName = "Given no link and no matching email, when the identity arrives, then a password-less account is provisioned and linked")]
    public async Task ProvisionAccountWhenUnknownAsync()
    {
        _ = linkStore.FindAsync("authentik", "sub-999", TestContext.Current.CancellationToken).Returns((OidcLink?)null);
        _ = userStore.FindByEmailAsync("fresh@example.com", TestContext.Current.CancellationToken).Returns((User?)null);

        var result = await linker.HandleAsync(new OidcLinkRequest("authentik", "sub-999", "fresh@example.com", "Fresh Face", EmailVerified: true), TestContext.Current.CancellationToken);

        result.Created.ShouldBeTrue();
        result.User.Email.ShouldBe("fresh@example.com");
        await userStore.Received(1).SaveAsync(
            Arg.Is<User>(static user => user.PasswordHash == null && user.DisplayName == "Fresh Face"),
            TestContext.Current.CancellationToken);
        await linkStore.Received(1).SaveAsync(
            Arg.Is<OidcLink>(static link => link.Provider == "authentik" && link.Subject == "sub-999"),
            TestContext.Current.CancellationToken);
    }

    [Fact(DisplayName = "Given a stored link to a disabled user, when OIDC login arrives, then the linker throws ProviderForbiddenException")]
    public async Task RefuseStoredLinkForDisabledUserAsync()
    {
        // Issue Q36 / v1.1: a disabled user must not authenticate via
        // OIDC. The attacker controls a configured IdP — the local
        // account's Disabled flag is the only thing that protects this
        // flow once the subject line up.
        var now = DateTimeOffset.UtcNow;
        var user = User.Create("disabled@example.com", "Disabled", null, now);
        user.Disable(now);
        var link = OidcLink.Create(user.Id, "keycloak", "sub-999", now);
        _ = linkStore.FindAsync("keycloak", "sub-999", TestContext.Current.CancellationToken).Returns(link);
        _ = userStore.FindByIdAsync(user.Id, TestContext.Current.CancellationToken).Returns(user);

        var exception = await Should.ThrowAsync<ProviderForbiddenException>(
            async () => await linker.HandleAsync(
                new OidcLinkRequest("keycloak", "sub-999", "disabled@example.com", null, EmailVerified: true),
                TestContext.Current.CancellationToken));

        exception.Code.ShouldBe("user.disabled");
        exception.Message.ShouldContain("disabled@example.com");

        // The link is not refreshed — the failure short-circuits before
        // the linker would write back.
        await linkStore.DidNotReceiveWithAnyArgs().SaveAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact(DisplayName = "Given a matching email on a disabled user, when OIDC login arrives, then the linker throws ProviderForbiddenException")]
    public async Task RefuseEmailMatchForDisabledUserAsync()
    {
        // Issue Q36 / v1.1: the email-match path (no stored link yet)
        // is the wider window — a first-time login from a configured
        // IdP must not be enough to bypass a disabled local account.
        var now = DateTimeOffset.UtcNow;
        var user = User.Create("disabled@example.com", "Disabled", null, now);
        user.Disable(now);
        _ = linkStore.FindAsync("authentik", "sub-new", TestContext.Current.CancellationToken).Returns((OidcLink?)null);
        _ = userStore.FindByEmailAsync("disabled@example.com", TestContext.Current.CancellationToken).Returns(user);

        var exception = await Should.ThrowAsync<ProviderForbiddenException>(
            async () => await linker.HandleAsync(
                new OidcLinkRequest("authentik", "sub-new", "disabled@example.com", null, EmailVerified: true),
                TestContext.Current.CancellationToken));

        exception.Code.ShouldBe("user.disabled");

        // No link written — the disabled check fires before the linker
        // would mint the link.
        await linkStore.DidNotReceiveWithAnyArgs().SaveAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact(DisplayName = "Given email_verified=false, when the linker runs, then the linker throws ProviderException with the email-unverified code")]
    public async Task RefuseUnverifiedEmailAsync()
    {
        // Security audit A01-2: the linker enforces email_verified on
        // every link / provision decision (defense in depth — the
        // validator gates upstream). An unverified email must not bind
        // a Comuki account under any path: stored link, email match,
        // or fresh provision.
        _ = linkStore.FindAsync(Arg.Any<string>(), Arg.Any<string>(), TestContext.Current.CancellationToken)
            .Returns((OidcLink?)null);
        _ = userStore.FindByEmailAsync(Arg.Any<string>(), TestContext.Current.CancellationToken)
            .Returns((User?)null);

        var exception = await Should.ThrowAsync<ProviderException>(
            async () => await linker.HandleAsync(
                new OidcLinkRequest("keycloak", "sub-123", "unverified@example.com", null, EmailVerified: false),
                TestContext.Current.CancellationToken));

        exception.Code.ShouldBe(OidcIdTokenValidator.EmailUnverifiedCode);

        // No link written, no user stored — the unverified-email gate
        // fires before any side effect.
        await linkStore.DidNotReceiveWithAnyArgs().SaveAsync(default!, TestContext.Current.CancellationToken);
        await userStore.DidNotReceiveWithAnyArgs().SaveAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact(DisplayName = "Given email_verified=true on a fresh identity, when the linker runs, then the linker provisions the password-less account")]
    public async Task ProvisionWhenEmailVerifiedTrueAsync()
    {
        // Same setup as the disabled-user refutation tests, but the
        // email is verified — provision proceeds normally. This is the
        // happy-path assertion for the email_verified gate.
        _ = linkStore.FindAsync("authentik", "sub-999", TestContext.Current.CancellationToken).Returns((OidcLink?)null);
        _ = userStore.FindByEmailAsync("verified@example.com", TestContext.Current.CancellationToken).Returns((User?)null);

        var result = await linker.HandleAsync(
            new OidcLinkRequest("authentik", "sub-999", "verified@example.com", "Verified Face", EmailVerified: true),
            TestContext.Current.CancellationToken);

        result.Created.ShouldBeTrue();
        result.User.Email.ShouldBe("verified@example.com");
        await userStore.Received(1).SaveAsync(
            Arg.Is<User>(static user => user.PasswordHash == null && user.DisplayName == "Verified Face"),
            TestContext.Current.CancellationToken);
        await linkStore.Received(1).SaveAsync(
            Arg.Is<OidcLink>(static link => link.Provider == "authentik" && link.Subject == "sub-999"),
            TestContext.Current.CancellationToken);
    }

    private sealed class FrozenTime : TimeProvider;
}

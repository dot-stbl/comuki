using Comuki.Modules.Identity.Application.Oidc;
using Comuki.Modules.Identity.Application.Ports;
using Comuki.Modules.Identity.Domain.Users;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Identity.Unit;

/// <summary>
/// Linker semantics (T4.6): an existing link wins, a matching email links
/// the known account, anything else provisions a password-less account.
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

        var result = await linker.HandleAsync(new OidcLinkRequest("keycloak", "sub-123", "known@example.com", null), TestContext.Current.CancellationToken);

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

        var result = await linker.HandleAsync(new OidcLinkRequest("keycloak", "sub-777", "known@example.com", null), TestContext.Current.CancellationToken);

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

        var result = await linker.HandleAsync(new OidcLinkRequest("authentik", "sub-999", "fresh@example.com", "Fresh Face"), TestContext.Current.CancellationToken);

        result.Created.ShouldBeTrue();
        result.User.Email.ShouldBe("fresh@example.com");
        await userStore.Received(1).SaveAsync(
            Arg.Is<User>(static user => user.PasswordHash == null && user.DisplayName == "Fresh Face"),
            TestContext.Current.CancellationToken);
        await linkStore.Received(1).SaveAsync(
            Arg.Is<OidcLink>(static link => link.Provider == "authentik" && link.Subject == "sub-999"),
            TestContext.Current.CancellationToken);
    }

    private sealed class FrozenTime : TimeProvider;
}

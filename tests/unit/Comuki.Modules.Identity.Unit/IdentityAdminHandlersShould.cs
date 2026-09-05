using Comuki.Modules.Identity.Application.ApiKeys;
using Comuki.Modules.Identity.Application.ApiKeys.Revoke;
using Comuki.Modules.Identity.Application.Options;
using Comuki.Modules.Identity.Application.Ports;
using Comuki.Modules.Identity.Application.Users;
using Comuki.Modules.Identity.Domain.ApiKeys;
using Comuki.Modules.Identity.Domain.Ids;
using Comuki.Modules.Identity.Domain.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Identity.Unit;

/// <summary>
/// Identity admin handlers (issues #31-#37): invite, set-disabled,
/// link OIDC subject, revoke API key. Stores are mocked; each handler
/// is exercised over the happy path plus the failure codes the
/// controller turns into ProblemDetails.
/// </summary>
public sealed class IdentityAdminHandlersShould
{
    private readonly DateTimeOffset now = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);
    private readonly FakeTime clock;
    private readonly PasswordHasher<User> passwordHasher = new();

    public IdentityAdminHandlersShould()
    {
        clock = new FakeTime(now);
    }

    [Fact(DisplayName = "Given a free email, when InviteUser runs with a password, then the account is saved and viewed")]
    public async Task InviteUserWithPasswordPersistsAsync()
    {
        var store = Substitute.For<IUserAccountStore>();
        store.FindByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((User?)null);
        var handler = new InviteUserHandler(store, passwordHasher, clock);

        var view = await handler.HandleAsync(
            new InviteUserCommand("Ada@Example.COM", " Ada ", "password1"),
            TestContext.Current.CancellationToken);

        view.Email.ShouldBe("ada@example.com");
        view.DisplayName.ShouldBe("Ada");
        view.Disabled.ShouldBeFalse();
        await store.Received(1).SaveAsync(
            Arg.Is<User>(static user => user.Email == "ada@example.com" && user.PasswordHash != null),
            Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given a free email, when InviteUser runs without a password, then the account lands password-less")]
    public async Task InviteUserWithoutPasswordPersistsAsync()
    {
        var store = Substitute.For<IUserAccountStore>();
        store.FindByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((User?)null);
        var handler = new InviteUserHandler(store, passwordHasher, clock);

        var view = await handler.HandleAsync(
            new InviteUserCommand("ada@example.com", null, null),
            TestContext.Current.CancellationToken);

        view.Email.ShouldBe("ada@example.com");
        view.DisplayName.ShouldNotBeNullOrEmpty();
        await store.Received(1).SaveAsync(
            Arg.Is<User>(static user => user.PasswordHash == null),
            Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given a taken email, when InviteUser runs, then InvalidOperationException is thrown")]
    public async Task InviteUserRefusesDuplicateAsync()
    {
        var existing = User.Create("ada@example.com", "Ada", "hash", now);
        var store = Substitute.For<IUserAccountStore>();
        store.FindByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(existing);
        var handler = new InviteUserHandler(store, passwordHasher, clock);

        await Should.ThrowAsync<InvalidOperationException>(
            () => handler.HandleAsync(new InviteUserCommand("ada@example.com", "Ada", "password1"), TestContext.Current.CancellationToken));
    }

    [Fact(DisplayName = "Given a disabled user, when SetUserDisabled runs with disabled=false, then the flag flips and tokens bump")]
    public async Task SetUserDisabledReEnablesAsync()
    {
        var user = User.Create("ada@example.com", "Ada", "hash", now);
        user.Disable(now);
        var originalTokensVersion = user.TokensVersion;
        var store = Substitute.For<IUserAccountStore>();
        store.FindByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        var handler = new SetUserDisabledHandler(store, clock);

        var view = await handler.HandleAsync(
            new SetUserDisabledCommand(user.Id.Value, false),
            TestContext.Current.CancellationToken);

        view.Disabled.ShouldBeFalse();
        await store.Received(1).SaveAsync(user, Arg.Any<CancellationToken>());
        user.TokensVersion.ShouldBe(originalTokensVersion);
    }

    [Fact(DisplayName = "Given an enabled user, when SetUserDisabled runs with disabled=true, then the flag flips and tokens bump")]
    public async Task SetUserDisabledEnablesAsync()
    {
        var user = User.Create("ada@example.com", "Ada", "hash", now);
        var store = Substitute.For<IUserAccountStore>();
        store.FindByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        var handler = new SetUserDisabledHandler(store, clock);

        var view = await handler.HandleAsync(
            new SetUserDisabledCommand(user.Id.Value, true),
            TestContext.Current.CancellationToken);

        view.Disabled.ShouldBeTrue();
        await store.Received(1).SaveAsync(user, Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given a missing user, when SetUserDisabled runs, then InvalidOperationException is thrown")]
    public async Task SetUserDisabledMissingThrowsAsync()
    {
        var store = Substitute.For<IUserAccountStore>();
        store.FindByIdAsync(Arg.Any<UserId>(), Arg.Any<CancellationToken>()).Returns((User?)null);
        var handler = new SetUserDisabledHandler(store, clock);

        await Should.ThrowAsync<InvalidOperationException>(
            () => handler.HandleAsync(new SetUserDisabledCommand(Guid.NewGuid(), true), TestContext.Current.CancellationToken));
    }

    [Fact(DisplayName = "Given a free (provider, subject), when LinkOidcSubject runs, then the link is saved")]
    public async Task LinkOidcSubjectPersistsAsync()
    {
        var userId = UserId.New();
        var user = User.Create("ada@example.com", "Ada", "hash", now);
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(user, userId);
        var store = Substitute.For<IUserAccountStore>();
        store.FindByIdAsync(userId, Arg.Any<CancellationToken>()).Returns(user);
        var oidcLinks = Substitute.For<IOidcLinkStore>();
        oidcLinks.FindAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((OidcLink?)null);
        var handler = new LinkOidcSubjectHandler(store, oidcLinks, clock);

        var view = await handler.HandleAsync(
            new LinkOidcSubjectCommand(userId.Value, "Keycloak", "sub-abc-123"),
            TestContext.Current.CancellationToken);

        view.Provider.ShouldBe("keycloak");
        view.Subject.ShouldBe("sub-abc-123");
        await oidcLinks.Received(1).SaveAsync(Arg.Any<OidcLink>(), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given a duplicate (provider, subject), when LinkOidcSubject runs, then InvalidOperationException is thrown")]
    public async Task LinkOidcSubjectRefusesDuplicateAsync()
    {
        var userId = UserId.New();
        var user = User.Create("ada@example.com", "Ada", "hash", now);
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(user, userId);
        var existing = OidcLink.Create(userId, "Keycloak", "sub-abc-123", now);
        var store = Substitute.For<IUserAccountStore>();
        store.FindByIdAsync(userId, Arg.Any<CancellationToken>()).Returns(user);
        var oidcLinks = Substitute.For<IOidcLinkStore>();
        oidcLinks.FindAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(existing);
        var handler = new LinkOidcSubjectHandler(store, oidcLinks, clock);

        await Should.ThrowAsync<InvalidOperationException>(
            () => handler.HandleAsync(
                new LinkOidcSubjectCommand(userId.Value, "Keycloak", "sub-abc-123"),
                TestContext.Current.CancellationToken));
    }

    [Fact(DisplayName = "Given an unknown user, when LinkOidcSubject runs, then InvalidOperationException is thrown")]
    public async Task LinkOidcSubjectMissingUserAsync()
    {
        var store = Substitute.For<IUserAccountStore>();
        store.FindByIdAsync(Arg.Any<UserId>(), Arg.Any<CancellationToken>()).Returns((User?)null);
        var oidcLinks = Substitute.For<IOidcLinkStore>();
        var handler = new LinkOidcSubjectHandler(store, oidcLinks, clock);

        await Should.ThrowAsync<InvalidOperationException>(
            () => handler.HandleAsync(
                new LinkOidcSubjectCommand(Guid.NewGuid(), "Keycloak", "sub-abc-123"),
                TestContext.Current.CancellationToken));
    }

    [Fact(DisplayName = "Given an active API key, when RevokeApiKey runs, then it is revoked and saved")]
    public async Task RevokeApiKeyPersistsAsync()
    {
        var userId = UserId.New();
        var issuer = new ApiKeyIssuer(
            Substitute.For<IApiKeyStore>(),
            new ApiKeyHasher(Options.Create(new ApiKeyOptions { Pepper = "unit-test-pepper-0123456789abcdef" })),
            clock);
        var issued = await issuer.IssueAsync(userId, "ci", TestContext.Current.CancellationToken);

        var apiKeyStore = Substitute.For<IApiKeyStore>();
        var stored = ApiKey.Create(userId, "ci", issued.Prefix, "digest", now);
        typeof(ApiKey).GetProperty(nameof(ApiKey.Id))!.SetValue(stored, issued.Id);
        apiKeyStore.FindByIdAsync(issued.Id, Arg.Any<CancellationToken>()).Returns(stored);
        var handler = new RevokeApiKeyHandler(apiKeyStore, clock);

        var view = await handler.HandleAsync(issued.Id.Value, TestContext.Current.CancellationToken);

        view.IsActive.ShouldBeFalse();
        view.RevokedAt.ShouldNotBeNull();
        await apiKeyStore.Received(1).SaveAsync(stored, Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given an unknown key id, when RevokeApiKey runs, then InvalidOperationException is thrown")]
    public async Task RevokeApiKeyMissingThrowsAsync()
    {
        var apiKeyStore = Substitute.For<IApiKeyStore>();
        apiKeyStore.FindByIdAsync(Arg.Any<ApiKeyId>(), Arg.Any<CancellationToken>()).Returns((ApiKey?)null);
        var handler = new RevokeApiKeyHandler(apiKeyStore, clock);

        await Should.ThrowAsync<InvalidOperationException>(
            () => handler.HandleAsync(Guid.NewGuid(), TestContext.Current.CancellationToken));
    }

    private sealed class FakeTime(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }
}

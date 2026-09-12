using System.Security.Claims;
using Comuki.Modules.Identity.Application.Ports;
using Comuki.Modules.Identity.Application.Sessions;
using Comuki.Modules.Identity.Domain.Users;
using Comuki.Modules.Identity.Infrastructure.Security;
using Comuki.Modules.Identity.Infrastructure.Security.Cookies;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Identity.Unit;

/// <summary>
/// Logout server-side invalidation suite: LogoutAsync must bump the
/// account's tokens_version so a previously-valid cookie ticket fails
/// ValidateCookieAsync (the security-stamp recheck) on its next request.
/// </summary>
public sealed class UserAuthenticationServiceShould
{
    private readonly DateTimeOffset now = new(2026, 9, 12, 12, 0, 0, TimeSpan.Zero);
    private readonly IAuthenticationService authenticationService = Substitute.For<IAuthenticationService>();
    private readonly IUserAccountStore userStore = Substitute.For<IUserAccountStore>();

    [Fact(DisplayName = "Given a valid cookie ticket, when LogoutAsync runs, then the same ticket fails the next ValidateCookieAsync")]
    public async Task LogoutRejectsPreviouslyValidTicketAsync()
    {
        var user = User.Create("ada@example.com", "Ada", "hash", now);
        userStore.FindByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        var (service, httpContext) = NewService(user);
        var ticket = IdentityPrincipalBuilder.BuildForCookie(user);
        httpContext.User = ticket;

        var beforeLogout = await service.ValidateCookieAsync(ticket, TestContext.Current.CancellationToken);
        beforeLogout.ShouldBeTrue();

        await service.LogoutAsync(TestContext.Current.CancellationToken);

        var afterLogout = await service.ValidateCookieAsync(ticket, TestContext.Current.CancellationToken);
        afterLogout.ShouldBeFalse();
    }

    [Fact(DisplayName = "Given a cookie principal, when LogoutAsync runs, then the saved account has a bumped tokens_version")]
    public async Task LogoutBumpsTokensVersionAndSavesAsync()
    {
        var user = User.Create("ada@example.com", "Ada", "hash", now);
        userStore.FindByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        var (service, httpContext) = NewService(user);
        httpContext.User = IdentityPrincipalBuilder.BuildForCookie(user);

        await service.LogoutAsync(TestContext.Current.CancellationToken);

        user.TokensVersion.ShouldBe(2);
        await userStore.Received(1).SaveAsync(
            Arg.Is<User>(static saved => saved.TokensVersion == 2),
            Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given a cookie principal, when LogoutAsync runs, then the cookie scheme is signed out")]
    public async Task LogoutSignsOutCookieSchemeAsync()
    {
        var user = User.Create("ada@example.com", "Ada", "hash", now);
        userStore.FindByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        var (service, httpContext) = NewService(user);
        httpContext.User = IdentityPrincipalBuilder.BuildForCookie(user);

        await service.LogoutAsync(TestContext.Current.CancellationToken);

        await authenticationService.Received(1).SignOutAsync(
            httpContext,
            AuthSchemes.Cookie,
            Arg.Any<AuthenticationProperties?>());
    }

    [Fact(DisplayName = "Given no cookie principal, when LogoutAsync runs, then the store is untouched but the cookie scheme is signed out")]
    public async Task LogoutWithoutCookiePrincipalSkipsBumpAsync()
    {
        var (service, httpContext) = NewService(null);
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity());

        await service.LogoutAsync(TestContext.Current.CancellationToken);

        await userStore.DidNotReceiveWithAnyArgs().FindByIdAsync(default!, TestContext.Current.CancellationToken);
        await userStore.DidNotReceiveWithAnyArgs().SaveAsync(default!, TestContext.Current.CancellationToken);
        await authenticationService.Received(1).SignOutAsync(
            httpContext,
            AuthSchemes.Cookie,
            Arg.Any<AuthenticationProperties?>());
    }

    [Fact(DisplayName = "Given an api-key principal, when LogoutAsync runs, then the owner's tokens_version is not bumped")]
    public async Task LogoutWithApiKeyPrincipalSkipsBumpAsync()
    {
        var (service, httpContext) = NewService(null);
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())],
            AuthSchemes.ApiKey));

        await service.LogoutAsync(TestContext.Current.CancellationToken);

        await userStore.DidNotReceiveWithAnyArgs().FindByIdAsync(default!, TestContext.Current.CancellationToken);
        await userStore.DidNotReceiveWithAnyArgs().SaveAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact(DisplayName = "Given a principal whose account is gone, when LogoutAsync runs, then logout still completes without throwing")]
    public async Task LogoutWithMissingAccountStillSignsOutAsync()
    {
        var user = User.Create("ada@example.com", "Ada", "hash", now);
        userStore.FindByIdAsync(Arg.Any<Domain.Ids.UserId>(), Arg.Any<CancellationToken>()).Returns((User?)null);
        var (service, httpContext) = NewService(null);
        httpContext.User = IdentityPrincipalBuilder.BuildForCookie(user);

        await service.LogoutAsync(TestContext.Current.CancellationToken);

        await userStore.DidNotReceiveWithAnyArgs().SaveAsync(default!, TestContext.Current.CancellationToken);
        await authenticationService.Received(1).SignOutAsync(
            httpContext,
            AuthSchemes.Cookie,
            Arg.Any<AuthenticationProperties?>());
    }

    private (UserAuthenticationService Service, HttpContext HttpContext) NewService(User? configuredUser)
    {
        if (configuredUser is not null)
        {
            userStore.FindByIdAsync(configuredUser.Id, Arg.Any<CancellationToken>()).Returns(configuredUser);
        }

        var services = new ServiceCollection();
        services.AddSingleton(authenticationService);
        var httpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpContext);

        var service = new UserAuthenticationService(
            new LoginHandler(userStore, new PasswordHasher<User>()),
            userStore,
            new FrozenTime(now),
            accessor);
        return (service, httpContext);
    }

    private sealed class FrozenTime(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }
}

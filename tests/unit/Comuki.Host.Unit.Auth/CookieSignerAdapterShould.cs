using System.Security.Claims;
using Comuki.Host.Auth.Security;
using Comuki.Modules.Identity.Application.Ports;
using Comuki.Modules.Identity.Application.Views;
using Comuki.Modules.Identity.Domain.Ids;
using Comuki.Modules.Identity.Domain.Users;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Host.Unit.Auth;

/// <summary>
/// Host-side <see cref="CookieSignerAdapter"/>: thin wrapper that bridges
/// the OIDC callback handler (which carries the <see cref="UserAccountView"/>)
/// to ASP.NET Core's cookie auth scheme. Three things matter: the user is
/// resolved through <see cref="IUserAccountStore"/>, the principal is built
/// with the production <c>IdentityPrincipalBuilder</c> claims, and a
/// missing user / missing <see cref="HttpContext"/> short-circuits before
/// the auth pipeline runs. Note: this class exposes <c>SignIn</c> only —
/// there is no separate Verify step. The four contract checks below cover
/// the realistic behaviour of the adapter.
/// </summary>
public sealed class CookieSignerAdapterShould
{
    private static readonly DateTimeOffset anchorTime = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Given a known user and an HttpContext, when SignInAsync is called, then the cookie scheme is invoked with the principal carrying the user claims")]
    public async Task SignInAsyncCallsSchemeWithPrincipalAsync()
    {
        var user = User.Create("linked@example.com", "Linked Display", passwordHash: null, now: anchorTime);
        var userStore = Substitute.For<IUserAccountStore>();
        userStore.FindByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        var authService = Substitute.For<IAuthenticationService>();
        var httpContext = NewHttpContext(authService);
        var accessor = NewAccessor(httpContext);
        var adapter = new CookieSignerAdapter(userStore, accessor);

        await adapter.SignInAsync(NewView(user.Id, "linked@example.com", "Linked Display"), TestContext.Current.CancellationToken);

        await authService.Received(1).SignInAsync(
            Arg.Is<HttpContext>(context => context == httpContext),
            Modules.Identity.Infrastructure.Security.AuthSchemes.Cookie,
            Arg.Is<ClaimsPrincipal>(principal => MatchesLinkedUser(principal, user.Id, "linked@example.com", "Linked Display")),
            Arg.Any<AuthenticationProperties>());
    }

    [Fact(DisplayName = "Given a user id that the store can no longer resolve, when SignInAsync is called, then the call fails before the auth scheme runs")]
    public async Task SignInAsyncThrowsWhenUserMissingAsync()
    {
        var userId = UserId.New();
        var userStore = Substitute.For<IUserAccountStore>();
        userStore.FindByIdAsync(userId, Arg.Any<CancellationToken>()).Returns((User?)null);
        var authService = Substitute.For<IAuthenticationService>();
        var httpContext = NewHttpContext(authService);
        var accessor = NewAccessor(httpContext);
        var adapter = new CookieSignerAdapter(userStore, accessor);

        await Should.ThrowAsync<InvalidOperationException>(
            async () => await adapter.SignInAsync(NewView(userId, "x@example.com", "X"), TestContext.Current.CancellationToken));

        await authService.DidNotReceiveWithAnyArgs().SignInAsync(
            default!, default!, default!, default!);
    }

    [Fact(DisplayName = "Given a null HttpContext, when SignInAsync is called, then the call fails before the auth scheme runs")]
    public async Task SignInAsyncThrowsWhenHttpContextMissingAsync()
    {
        var userId = UserId.New();
        var userStore = Substitute.For<IUserAccountStore>();
        var accessor = NewAccessor(httpContext: null);
        var adapter = new CookieSignerAdapter(userStore, accessor);

        await Should.ThrowAsync<InvalidOperationException>(
            async () => await adapter.SignInAsync(NewView(userId, "a@example.com", "A"), TestContext.Current.CancellationToken));
    }

    [Fact(DisplayName = "Given a known user and an HttpContext, when SignInAsync is called, then the principal's name identifier matches the user id")]
    public async Task SignInAsyncPrincipalCarriesUserIdClaimAsync()
    {
        var user = User.Create("name-id@example.com", "Name Id", passwordHash: null, now: anchorTime);
        var userStore = Substitute.For<IUserAccountStore>();
        userStore.FindByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        var authService = Substitute.For<IAuthenticationService>();
        var httpContext = NewHttpContext(authService);
        var accessor = NewAccessor(httpContext);
        var adapter = new CookieSignerAdapter(userStore, accessor);

        await adapter.SignInAsync(NewView(user.Id, "name-id@example.com", "Name Id"), TestContext.Current.CancellationToken);

        var captured = authService.ReceivedCalls()
            .Select(static call => call.GetArguments()[2])
            .OfType<ClaimsPrincipal>()
            .Single();
        captured.FindFirstValue(ClaimTypes.NameIdentifier).ShouldBe(user.Id.Value.ToString());
        captured.FindFirstValue(ClaimTypes.Email).ShouldBe("name-id@example.com");
        captured.Identity!.AuthenticationType.ShouldBe(Modules.Identity.Infrastructure.Security.AuthSchemes.Cookie);
    }

    private static UserAccountView NewView(UserId userId, string email, string displayName)
    {
        return new UserAccountView(userId, email, displayName, Disabled: false, TokensVersion: 1, CreatedAt: anchorTime);
    }

    private static HttpContext NewHttpContext(IAuthenticationService authService)
    {
        var services = new ServiceCollection();
        services.AddSingleton(authService);
        return new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
    }

    private static IHttpContextAccessor NewAccessor(HttpContext? httpContext)
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpContext);
        return accessor;
    }

    private static bool MatchesLinkedUser(ClaimsPrincipal principal, UserId userId, string email, string displayName)
    {
        return principal.FindFirstValue(ClaimTypes.NameIdentifier) == userId.Value.ToString()
            && principal.FindFirstValue(ClaimTypes.Email) == email
            && principal.FindFirstValue(ClaimTypes.Name) == displayName
            && principal.Identity?.AuthenticationType == Modules.Identity.Infrastructure.Security.AuthSchemes.Cookie;
    }
}



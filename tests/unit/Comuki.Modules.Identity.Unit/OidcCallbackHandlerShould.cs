using Comuki.Modules.Identity.Application.Oidc;
using Comuki.Modules.Identity.Application.Ports;
using Comuki.Modules.Identity.Application.Views;
using Comuki.Modules.Identity.Domain.Oidc;
using Comuki.Modules.Identity.Domain.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Identity.Unit;

/// <summary>
/// Callback handler unit suite: state validation, token-exchange errors,
/// id_token failures and successful sign-in paths all land in the right
/// <see cref="OidcCallbackResult"/> shape.
/// </summary>
public sealed class OidcCallbackHandlerShould
{
    private readonly IOidcStateStore stateStore = Substitute.For<IOidcStateStore>();
    private readonly IOidcDiscovery discovery = Substitute.For<IOidcDiscovery>();
    private readonly IOidcClientSecrets clientSecrets = Substitute.For<IOidcClientSecrets>();
    private readonly IOidcTokenExchange tokenExchange = Substitute.For<IOidcTokenExchange>();
    private readonly IOidcIdTokenValidator idTokenValidator = Substitute.For<IOidcIdTokenValidator>();
    private readonly IUserAccountStore userStore = Substitute.For<IUserAccountStore>();
    private readonly IOidcLinkStore linkStore = Substitute.For<IOidcLinkStore>();
    private readonly ICookieSigner signer = Substitute.For<ICookieSigner>();
    private readonly OidcAccountLinker linker = null!;
    private readonly OidcOptions options = new() { Providers = [TestProvider] };
    private readonly OpenIdConnectConfiguration discoveryDoc = new() { TokenEndpoint = "https://kc.example.com/realms/comuki/protocol/openid-connect/token" };
    private readonly OidcCallbackHandler handler;

    public OidcCallbackHandlerShould()
    {
        linker = new OidcAccountLinker(userStore, linkStore, new FrozenTime());

        handler = new OidcCallbackHandler(
            stateStore,
            discovery,
            Options.Create(options),
            clientSecrets,
            tokenExchange,
            idTokenValidator,
            linker,
            signer,
            NullLogger<OidcCallbackHandler>.Instance);
    }

    [Fact(DisplayName = "Given a provider-side error, when HandleAsync runs, then the failure code names the provider's error")]
    public async Task ProviderErrorSurfacesAsFailureCodeAsync()
    {
        var result = await handler.HandleAsync(
            new OidcCallbackRequest(null, null, "access_denied", "user clicked cancel"),
            TestContext.Current.CancellationToken);

        result.Success.ShouldBeFalse();
        result.FailureCode.ShouldBe("oidc.provider_access_denied");
        result.RedirectTarget.ShouldStartWith("/login?reason=oidc-failed");
    }

    [Fact(DisplayName = "Given a missing code or state, when HandleAsync runs, then oidc.callback_incomplete is returned without touching the store")]
    public async Task MissingCodeOrStateShortCircuitsAsync()
    {
        var result = await handler.HandleAsync(
            new OidcCallbackRequest(null, "11111111-2222-3333-4444-555555555555", null, null),
            TestContext.Current.CancellationToken);

        result.Success.ShouldBeFalse();
        result.FailureCode.ShouldBe("oidc.callback_incomplete");
        await stateStore.DidNotReceiveWithAnyArgs().ConsumeAsync(default, TestContext.Current.CancellationToken);
    }

    [Fact(DisplayName = "Given a malformed state, when HandleAsync runs, then oidc.state_malformed is returned")]
    public async Task MalformedStateIsRejectedAsync()
    {
        var result = await handler.HandleAsync(
            new OidcCallbackRequest("auth-code", "not-a-guid", null, null),
            TestContext.Current.CancellationToken);

        result.Success.ShouldBeFalse();
        result.FailureCode.ShouldBe("oidc.state_malformed");
        await stateStore.DidNotReceiveWithAnyArgs().ConsumeAsync(default, TestContext.Current.CancellationToken);
    }

    [Fact(DisplayName = "Given a state the store can't find, when HandleAsync runs, then oidc.state_mismatch is returned")]
    public async Task MissingStateIsMismatchAsync()
    {
        var stateGuid = Guid.Parse("11111111-2222-3333-4444-555555555555");
        _ = stateStore.ConsumeAsync(Arg.Is<OidcStateId>(id => id.Value == stateGuid), TestContext.Current.CancellationToken)
            .Returns((OidcState?)null);

        var result = await handler.HandleAsync(
            new OidcCallbackRequest("auth-code", stateGuid.ToString("D"), null, null),
            TestContext.Current.CancellationToken);

        result.Success.ShouldBeFalse();
        result.FailureCode.ShouldBe("oidc.state_mismatch");
    }

    [Fact(DisplayName = "Given a valid state and a successful exchange + verification, when HandleAsync runs, then the user is signed in and returned to the in-app path")]
    public async Task HappyPathSignsInAsync()
    {
        var stateGuid = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var state = OidcState.Create(
            "keycloak",
            "verifier-abc",
            "S256",
            "https://app.example.com/api/v1/auth/oidc/callback",
            "/runs",
            DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(5));
        var user = User.Create("linked@example.com", "Linked", null, DateTimeOffset.UtcNow);
        var userView = new UserAccountView(user.Id, user.Email, user.DisplayName, false, user.TokensVersion, user.CreatedAt);

        _ = stateStore.ConsumeAsync(Arg.Any<OidcStateId>(), TestContext.Current.CancellationToken).Returns(state);
        _ = discovery.GetAsync(Arg.Any<OidcProviderOptions>(), TestContext.Current.CancellationToken).Returns(discoveryDoc);
        _ = clientSecrets.GetAsync("keycloak", TestContext.Current.CancellationToken).Returns("secret-value");
        _ = tokenExchange.ExchangeAsync(
                Arg.Any<Uri>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                TestContext.Current.CancellationToken)
            .Returns(new OidcTokenResponse("fake-id-token", "fake-access", "Bearer", 60));
        _ = idTokenValidator.Validate("fake-id-token", discoveryDoc, "comuki-dashboard", TestContext.Current.CancellationToken)
            .Returns(new OidcVerifiedClaims("sub-123", "linked@example.com", "Linked"));
        _ = userStore.FindByEmailAsync("linked@example.com", TestContext.Current.CancellationToken).Returns(user);

        var result = await handler.HandleAsync(
            new OidcCallbackRequest("auth-code", stateGuid.ToString("D"), null, null),
            TestContext.Current.CancellationToken);

        result.Success.ShouldBeTrue();
        result.RedirectTarget.ShouldBe("/runs");
        result.FailureCode.ShouldBeNull();
        await signer.Received(1).SignInAsync(
            Arg.Is<UserAccountView>(v => v.Id == user.Id),
            TestContext.Current.CancellationToken);
    }

    [Fact(DisplayName = "Given a state row with an unsafe returnTo, when HandleAsync runs, then it falls back to / and ignores the unsafe path")]
    public async Task UnsafeReturnToFallsBackToRootAsync()
    {
        var stateGuid = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var state = OidcState.Create(
            "keycloak",
            "verifier-abc",
            "S256",
            "https://app.example.com/api/v1/auth/oidc/callback",
            "//evil.test/abc",
            DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(5));
        var user = User.Create("linked@example.com", "Linked", null, DateTimeOffset.UtcNow);

        _ = stateStore.ConsumeAsync(Arg.Any<OidcStateId>(), TestContext.Current.CancellationToken).Returns(state);
        _ = discovery.GetAsync(Arg.Any<OidcProviderOptions>(), TestContext.Current.CancellationToken).Returns(discoveryDoc);
        _ = clientSecrets.GetAsync("keycloak", TestContext.Current.CancellationToken).Returns("secret-value");
        _ = tokenExchange.ExchangeAsync(
                Arg.Any<Uri>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                TestContext.Current.CancellationToken)
            .Returns(new OidcTokenResponse("fake-id-token", "fake-access", "Bearer", 60));
        _ = idTokenValidator.Validate("fake-id-token", discoveryDoc, "comuki-dashboard", TestContext.Current.CancellationToken)
            .Returns(new OidcVerifiedClaims("sub-123", "linked@example.com", null));
        _ = userStore.FindByEmailAsync("linked@example.com", TestContext.Current.CancellationToken).Returns(user);

        var result = await handler.HandleAsync(
            new OidcCallbackRequest("auth-code", stateGuid.ToString("D"), null, null),
            TestContext.Current.CancellationToken);

        result.Success.ShouldBeTrue();
        result.RedirectTarget.ShouldBe("/");
    }

    [Fact(DisplayName = "Given a state that resolves but the token exchange throws, when HandleAsync runs, then oidc.token_exchange_failed is returned and the cookie is never signed")]
    public async Task TokenExchangeFailureDoesNotSignInAsync()
    {
        var stateGuid = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var state = OidcState.Create(
            "keycloak",
            "verifier-abc",
            "S256",
            "https://app.example.com/api/v1/auth/oidc/callback",
            null,
            DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(5));

        _ = stateStore.ConsumeAsync(Arg.Any<OidcStateId>(), TestContext.Current.CancellationToken).Returns(state);
        _ = discovery.GetAsync(Arg.Any<OidcProviderOptions>(), TestContext.Current.CancellationToken).Returns(discoveryDoc);
        _ = clientSecrets.GetAsync("keycloak", TestContext.Current.CancellationToken).Returns("secret-value");
        _ = tokenExchange.ExchangeAsync(
                Arg.Any<Uri>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                TestContext.Current.CancellationToken)
            .Returns(Task.FromException<OidcTokenResponse>(new InvalidOperationException("boom")));

        var result = await handler.HandleAsync(
            new OidcCallbackRequest("auth-code", stateGuid.ToString("D"), null, null),
            TestContext.Current.CancellationToken);

        result.Success.ShouldBeFalse();
        result.FailureCode.ShouldBe("oidc.token_exchange_failed");
        await signer.DidNotReceiveWithAnyArgs().SignInAsync(default!, TestContext.Current.CancellationToken);
    }

    private static OidcProviderOptions TestProvider => new()
    {
        Name = "keycloak",
        Authority = "https://kc.example.com/realms/comuki",
        ClientId = "comuki-dashboard",
        ClientSecretEnv = "COMUKI_OIDC_CLIENT_SECRET",
    };

    private sealed class FrozenTime : TimeProvider;
}

using Comuki.Host.Auth.Security;
using Comuki.Modules.Identity.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Shouldly;
using Xunit;

namespace Comuki.Host.Unit.Auth;

/// <summary>
/// Unit tests for the host-side OIDC scheme finishing: only the
/// module's OIDC schemes get the versioned callback path and the
/// account-linking ticket event; everything else is untouched.
/// </summary>
public sealed class OidcLoginPostConfigureShould
{
    [Fact(DisplayName = "Given an OIDC scheme name, when post-configured, then the callback moves under api/v1 and the ticket event is wired")]
    public void MoveOidcCallbackAndWireTicketEvent()
    {
        var configure = new OidcLoginPostConfigure();
        var options = new OpenIdConnectOptions();

        configure.PostConfigure(AuthSchemes.Oidc("keycloak"), options);

        options.CallbackPath.Value.ShouldBe("/api/v1/auth/oidc/keycloak/callback");
        options.Events.ShouldNotBeNull();
        options.Events.OnTicketReceived.ShouldNotBeNull();
    }

    [Fact(DisplayName = "Given a non-OIDC scheme name, when post-configured, then nothing changes")]
    public void LeaveNonOidcSchemesUntouched()
    {
        var configure = new OidcLoginPostConfigure();
        var options = new OpenIdConnectOptions
        {
            CallbackPath = "/original",
        };
        var defaultTicketReceived = options.Events.OnTicketReceived;

        configure.PostConfigure(AuthSchemes.Cookie, options);

        options.CallbackPath.Value.ShouldBe("/original");
        options.Events.OnTicketReceived.ShouldBe(defaultTicketReceived);
    }

    [Fact(DisplayName = "Given a null scheme name, when post-configured, then nothing changes")]
    public void LeaveNullSchemeUntouched()
    {
        var configure = new OidcLoginPostConfigure();
        var options = new OpenIdConnectOptions();
        var defaultTicketReceived = options.Events.OnTicketReceived;

        configure.PostConfigure(null, options);

        options.Events.OnTicketReceived.ShouldBe(defaultTicketReceived);
    }
}

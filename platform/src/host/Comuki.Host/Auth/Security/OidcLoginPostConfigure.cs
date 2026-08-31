using System.Security.Claims;
using Comuki.Modules.Identity.Application.Oidc;
using Comuki.Modules.Identity.Application.Ports;
using Comuki.Modules.Identity.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;

namespace Comuki.Host.Auth.Security;

/// <summary>
/// Host-side finishing of the module's per-provider OIDC schemes:
/// moves the protocol callback onto the versioned API surface
/// (<c>/api/v1/auth/oidc/{provider}/callback</c>) and turns the
/// external ticket into a local cookie — the <see cref="OidcAccountLinker"/>
/// resolves or provisions the local account, and the ticket principal is
/// replaced with the module's cookie grammar so the security-stamp
/// validation accepts the session.
/// </summary>
internal sealed class OidcLoginPostConfigure : IPostConfigureOptions<OpenIdConnectOptions>
{
    /// <inheritdoc />
    public void PostConfigure(string? name, OpenIdConnectOptions options)
    {
        var schemePrefix = AuthSchemes.Oidc(string.Empty);

        if (name is null || !name.StartsWith(schemePrefix, StringComparison.Ordinal))
        {
            return;
        }

        var provider = name[schemePrefix.Length..];

        options.CallbackPath = $"/{ApiRoutes.AuthOidcRoot}/{provider}/callback";
        options.Events ??= new OpenIdConnectEvents();

        var previousTicketReceived = options.Events.OnTicketReceived;

        options.Events.OnTicketReceived = async context =>
        {
            if (context.Principal is not { } external)
            {
                throw new InvalidOperationException($"oidc provider '{provider}' returned a ticket without a principal");
            }

            var subject = external.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? external.FindFirst("sub")?.Value;
            var email = external.FindFirst(ClaimTypes.Email)?.Value
                ?? external.FindFirst("email")?.Value;

            if (subject is null || email is null)
            {
                throw new InvalidOperationException(
                    $"oidc provider '{provider}' returned no sub/email claim - account linking is impossible");
            }

            ClaimsPrincipal localPrincipal;
            using (var scope = context.HttpContext.RequestServices.CreateScope())
            {
                var linker = scope.ServiceProvider.GetRequiredService<OidcAccountLinker>();
                var linked = await linker.HandleAsync(
                    new OidcLinkRequest(
                        provider,
                        subject,
                        email,
                        external.FindFirst(ClaimTypes.Name)?.Value ?? external.FindFirst("name")?.Value),
                    context.HttpContext.RequestAborted);

                var users = scope.ServiceProvider.GetRequiredService<IUserAccountStore>();
                if (await users.FindByIdAsync(linked.User.Id, context.HttpContext.RequestAborted) is not { } account)
                {
                    throw new InvalidOperationException($"oidc link resolved user {linked.User.Id} that is now missing");
                }

                localPrincipal = IdentityPrincipalBuilder.BuildForCookie(account);

                if (linked.Created)
                {
                    scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
                        .CreateLogger("Comuki.Host.Auth.Oidc")
                        .LogInformation(
                            "Oidc provider {Provider} provisioned local account {Email}",
                            provider,
                            account.Email);
                }
            }

            // The cookie grammar replaces the external identity before the
            // default ticket handling signs the session in.
            context.Principal = localPrincipal;

            if (previousTicketReceived is not null)
            {
                await previousTicketReceived(context);
            }
        };
    }
}

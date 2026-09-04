using System.Security.Claims;
using System.Text.Encodings.Web;
using Comuki.Modules.Proxy.Application.Models;
using Comuki.Modules.Proxy.Application.Resolving;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Comuki.Modules.Proxy.Infrastructure.Auth;

/// <summary>Stable claim names emitted by <see cref="VirtualKeyAuthenticationHandler"/>.</summary>
public static class ProxyClaimNames
{
    /// <summary>Raw virtual-key token; the transformer reads it to swap in the upstream key.</summary>
    public const string VirtualKey = "comuki.proxy.vkey";

    /// <summary>Project the spend is attributed to.</summary>
    public const string ProjectId = "comuki.proxy.project_id";

    /// <summary>Configured upstream provider id (<c>openai</c> / <c>anthropic</c>).</summary>
    public const string Provider = "comuki.proxy.provider";
}

/// <summary>
/// Authentication scheme that turns <c>Authorization: Bearer vkey_xxx</c>
/// into a <see cref="ClaimsPrincipal"/> the YARP transformer reads. The
/// scheme is registered through
/// <see cref="ProxyAuthenticationExtensions.AddVirtualKeyAuth"/>; it has
/// no options (no configuration knobs beyond presence/absence).
/// </summary>
public sealed class VirtualKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    VirtualKeyResolver resolver) : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    /// <summary>Default scheme name registered in the host composition.</summary>
    public const string SchemeName = "VirtualKey";

    /// <inheritdoc />
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authorization = Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(authorization))
        {
            return AuthenticateResult.NoResult();
        }

        const string bearer = "Bearer ";
        if (!authorization.StartsWith(bearer, StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var token = authorization[bearer.Length..].Trim();
        if (string.IsNullOrEmpty(token))
        {
            return AuthenticateResult.NoResult();
        }

        var requestedModel = ExtractRequestedModel();
        var resolution = await resolver.ResolveAsync(token, requestedModel, Context.RequestAborted);
        return resolution.Outcome switch
        {
            VirtualKeyResolver.ResolveOutcome.Missing => AuthenticateResult.Fail("invalid virtual key"),
            VirtualKeyResolver.ResolveOutcome.Expired => AuthenticateResult.Fail("virtual key expired"),
            VirtualKeyResolver.ResolveOutcome.ModelNotAllowed => AuthenticateResult.Fail("model not allowed for this virtual key"),
            VirtualKeyResolver.ResolveOutcome.Resolved => AuthenticateResult.Success(BuildTicket(token, resolution.Key!)),
            _ => AuthenticateResult.Fail("unknown virtual key resolution outcome"),
        };
    }

    /// <inheritdoc />
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = "Bearer realm=\"comuki-proxy\"";
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }

    private static string? ExtractRequestedModel()
    {
        return null;
    }

    private static AuthenticationTicket BuildTicket(string token, VirtualKey key)
    {
        var claims = new List<Claim>
        {
            new(ProxyClaimNames.VirtualKey, token),
            new(ProxyClaimNames.ProjectId, key.ProjectId.Value.ToString()),
            new(ProxyClaimNames.Provider, key.Upstream.Provider),
            new(ClaimTypes.NameIdentifier, key.ProjectId.Value.ToString()),
        };
        var identity = new ClaimsIdentity(claims, SchemeName);
        return new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
    }
}

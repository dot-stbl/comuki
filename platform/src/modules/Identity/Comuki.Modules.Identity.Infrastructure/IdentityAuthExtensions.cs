using System.Reflection;
using Comuki.Modules.Identity.Application.Permissions;
using Comuki.Modules.Identity.Infrastructure.Security;
using Comuki.Modules.Identity.Infrastructure.Security.ApiKeys;
using Comuki.Modules.Identity.Infrastructure.Security.Authorization;
using Comuki.Modules.Identity.Infrastructure.Security.Cookies;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Comuki.Modules.Identity.Infrastructure;

/// <summary>
/// Host-side wiring of the Identity security plumbing: the cookie and
/// API-key schemes, per-provider OIDC, the enforcement filter, the
/// security-stamp cookie validation, and the startup check that every
/// demanded permission key is declared. No controllers live here — the
/// host composes this and its own endpoints.
/// </summary>
public static class IdentityAuthExtensions
{
    /// <summary>
    /// Wires authentication, authorization, the permission filter and the
    /// startup validator. <paramref name="scanAssemblies"/> lists the
    /// assemblies whose <see cref="RequiresPermissionAttribute"/> demands
    /// the startup check covers (typically the host's controller assembly).
    /// </summary>
    /// <param name="services"></param>
    /// <param name="configuration"></param>
    /// <param name="scanAssemblies"></param>
    /// <returns></returns>
    public static IServiceCollection AddIdentityAuth(
        this IServiceCollection services,
        IConfiguration configuration,
        params Assembly[] scanAssemblies)
    {
        services.AddOptions<CookieAuthOptions>()
            .Bind(configuration.GetSection(CookieAuthOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpContextAccessor();
        services.TryAddScoped<IUserAuthenticationService, UserAuthenticationService>();

        var authentication = services.AddAuthentication(options =>
        {
            // Cookie is the default: SignIn/SignOut/challenge map to the
            // browser session; bearer requests forward to the API-key scheme.
            options.DefaultScheme = AuthSchemes.Cookie;
            options.DefaultSignInScheme = AuthSchemes.Cookie;
            options.DefaultSignOutScheme = AuthSchemes.Cookie;
            options.DefaultAuthenticateScheme = AuthSchemes.Cookie;
            options.DefaultChallengeScheme = AuthSchemes.Cookie;
            options.DefaultForbidScheme = AuthSchemes.Cookie;
        });

        services.AddOptions<CookieAuthenticationOptions>(AuthSchemes.Cookie)
            .Configure<IConfiguration>(ConfigureCookie);

        authentication
            .AddCookie(AuthSchemes.Cookie)
            .AddScheme<ApiKeySchemeOptions, ApiKeyAuthenticationHandler>(AuthSchemes.ApiKey, _ => { });

        AddOidcProviders(authentication, configuration);

        services.AddAuthorization();

        // Enforcement: one global resource filter per request + the startup
        // check over the assemblies the host asked to cover.
        services.Configure<MvcOptions>(static options => options.Filters.Add<RequiresPermissionFilter>());
        services.AddHostedService(provider => new PermissionDemandStartupValidator(
            provider.GetRequiredService<IPermissionCatalog>(),
            scanAssemblies));

        return services;
    }

    private static void ConfigureCookie(CookieAuthenticationOptions options, IConfiguration configuration)
    {
        var settings = configuration.GetSection(CookieAuthOptions.SectionName).Get<CookieAuthOptions>() ?? new CookieAuthOptions();

        options.Cookie.Name = settings.Name;
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        // SameSite=Lax blocks cross-site non-GET — CSRF cover for every
        // mutation without the antiforgery ceremony.
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = settings.ExpireTimeSpan;
        options.SlidingExpiration = settings.SlidingExpiration;

        // Security-stamp recheck: a bumped tokens_version or a disabled
        // account rejects the cookie at its next request.
        options.Events.OnValidatePrincipal = static async context =>
        {
            if (context.Principal is not { } principal)
            {
                return;
            }

            using var scope = context.HttpContext.RequestServices.CreateScope();
            var validator = scope.ServiceProvider.GetRequiredService<IUserAuthenticationService>();

            if (!await validator.ValidateCookieAsync(principal, context.HttpContext.RequestAborted))
            {
                context.RejectPrincipal();
            }
        };

        // A bearer header means the API-key scheme, not a cookie attempt.
        options.ForwardDefaultSelector = static context =>
            context.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? AuthSchemes.ApiKey
                : AuthSchemes.Cookie;
    }

    private static void AddOidcProviders(AuthenticationBuilder authentication, IConfiguration configuration)
    {
        _ = authentication;
        _ = configuration;

        // The OIDC code-flow runs as a manual handler (OidcStartHandler /
        // OidcCallbackHandler in Identity.Application) — discovery, PKCE,
        // token exchange and id_token validation are owned there, not by
        // the ASP.NET framework's OpenIdConnect scheme. Configuration
        // is validated by OidcOptions' data annotations at startup; no
        // scheme registration is needed here.
    }
}

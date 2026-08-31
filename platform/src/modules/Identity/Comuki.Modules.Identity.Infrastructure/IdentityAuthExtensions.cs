using System.Reflection;
using Comuki.Modules.Identity.Application.Permissions;
using Comuki.Modules.Identity.Infrastructure.Oidc;
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
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

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
        _ = services.AddOptions<CookieAuthOptions>()
            .Bind(configuration.GetSection(CookieAuthOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        _ = services.AddHttpContextAccessor();
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

        _ = services.AddOptions<CookieAuthenticationOptions>(AuthSchemes.Cookie)
            .Configure<IConfiguration>(ConfigureCookie);

        _ = authentication
            .AddCookie(AuthSchemes.Cookie)
            .AddScheme<ApiKeySchemeOptions, ApiKeyAuthenticationHandler>(AuthSchemes.ApiKey, _ => { });

        AddOidcProviders(authentication, configuration);

        _ = services.AddAuthorization();

        // Enforcement: one global resource filter per request + the startup
        // check over the assemblies the host asked to cover.
        _ = services.Configure<MvcOptions>(static options => options.Filters.Add<RequiresPermissionFilter>());
        _ = services.AddHostedService(provider => new PermissionDemandStartupValidator(
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
        var oidc = configuration.GetSection(OidcOptions.SectionName).Get<OidcOptions>() ?? new OidcOptions();

        foreach (var provider in oidc.Providers)
        {
            if (string.IsNullOrWhiteSpace(provider.Name)
                || string.IsNullOrWhiteSpace(provider.Authority)
                || string.IsNullOrWhiteSpace(provider.ClientId)
                || string.IsNullOrWhiteSpace(provider.ClientSecretEnv))
            {
                throw new InvalidOperationException(
                    $"oidc provider '{provider.Name}' is incomplete: name, authority, clientId and clientSecretEnv are all required");
            }

            var scheme = AuthSchemes.Oidc(provider.Name);

            _ = authentication.AddOpenIdConnect(scheme, options =>
            {
                options.Authority = provider.Authority;
                options.ClientId = provider.ClientId;
                options.ClientSecret = Environment.GetEnvironmentVariable(provider.ClientSecretEnv)
                    ?? throw new InvalidOperationException(
                        $"oidc provider '{provider.Name}': environment variable '{provider.ClientSecretEnv}' with the client secret is not set");
                options.ResponseType = OpenIdConnectResponseType.Code;
                options.CallbackPath = $"/auth/oidc/{provider.Name}/callback";
                options.SignInScheme = AuthSchemes.Cookie;
                options.SaveTokens = false;
            });
        }
    }
}

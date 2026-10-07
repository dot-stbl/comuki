using Comuki.Modules.Identity.Application.ApiKeys;
using Comuki.Modules.Identity.Application.ApiKeys.Issue;
using Comuki.Modules.Identity.Application.ApiKeys.List;
using Comuki.Modules.Identity.Application.ApiKeys.Revoke;
using Comuki.Modules.Identity.Application.Assignments.Grant;
using Comuki.Modules.Identity.Application.Assignments.List;
using Comuki.Modules.Identity.Application.Assignments.Revoke;
using Comuki.Modules.Identity.Application.Authorization;
using Comuki.Modules.Identity.Application.Oidc;
using Comuki.Modules.Identity.Application.Options;
using Comuki.Modules.Identity.Application.Permissions;
using Comuki.Modules.Identity.Application.Sessions;
using Comuki.Modules.Identity.Application.Users;
using Comuki.Modules.Identity.Domain.Users;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Refit;

namespace Comuki.Modules.Identity.Application;

/// <summary>
/// Composition of the Identity application layer: the permission catalog
/// and evaluator, the password hasher, API-key hashing/issuing, command
/// handlers and their validators. Persistence ports are satisfied by the
/// infrastructure installer; nothing here touches EF.
/// </summary>
public static class IdentityApplicationExtensions
{
    /// <summary>Registers the Identity application services.</summary>
    /// <param name="services"></param>
    /// <returns></returns>
    public static IServiceCollection AddIdentityApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddMemoryCache();

        services.AddOptions<ApiKeyOptions>()
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IPermissionCatalog, RoleMatrixPermissionCatalog>();
        services.AddScoped<IPermissionEvaluator, PermissionEvaluator>();

        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddSingleton<ApiKeyHasher>();
        services.AddScoped<ApiKeyIssuer>();

        services.AddScoped<CreateUserHandler>();
        services.AddScoped<InviteUserHandler>();
        services.AddScoped<ListUsersHandler>();
        services.AddScoped<LoginHandler>();
        services.AddScoped<GrantRoleHandler>();
        services.AddScoped<ListGrantsHandler>();
        services.AddScoped<RevokeRoleHandler>();
        services.AddScoped<IssueApiKeyHandler>();
        services.AddScoped<ListApiKeysHandler>();
        services.AddScoped<RevokeApiKeyHandler>();
        services.AddScoped<LinkOidcSubjectHandler>();
        services.AddScoped<SetUserDisabledHandler>();
        services.AddScoped<OidcAccountLinker>();
        services.AddSingleton<IOidcClientSecrets, OidcClientSecrets>();
        // OIDC discovery: stays on a raw HttpClient. The hand-rolled
        // JsonDocument parse in OidcDiscoveryCache is the whole point
        // — Keycloak 26's bool-as-bool discovery fields break strict
        // STJ deserialization of the framework's OpenIdConnectConfiguration
        // type, which is what a Refit-generated proxy would use.
        services.AddHttpClient<IOidcDiscovery, OidcDiscoveryCache>();
        // OIDC token exchange: Refit-typed. The token-endpoint wire
        // shape is RFC-stable so the typed proxy is the right
        // abstraction here (the deprecated AddHttpClient<TInterface, TImpl>
        // typed-client factory is gone — Refit owns the HttpClient).
        services
            .AddRefitClient<IOidcTokenExchangeApi>()
            .AddStandardResilienceHandler();
        services.AddScoped<OidcTokenExchange>();
        services.AddScoped<IOidcTokenExchange>(static sp => sp.GetRequiredService<OidcTokenExchange>());
        services.AddSingleton<IOidcIdTokenValidator, OidcIdTokenValidator>();
        services.AddSingleton<OidcProviderResolver>();
        services.AddScoped<OidcStartHandler>();
        services.AddScoped<OidcCallbackHandler>();

        services.AddScoped<IValidator<CreateUserCommand>, CreateUserValidator>();
        services.AddScoped<IValidator<LoginCommand>, LoginValidator>();
        services.AddScoped<IValidator<GrantRoleCommand>, GrantRoleValidator>();
        services.AddScoped<IValidator<IssueApiKeyCommand>, IssueApiKeyValidator>();

        return services;
    }
}

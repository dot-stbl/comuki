using Comuki.Modules.Identity.Application.ApiKeys;
using Comuki.Modules.Identity.Application.ApiKeys.Issue;
using Comuki.Modules.Identity.Application.Assignments.Grant;
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
        services.AddScoped<LoginHandler>();
        services.AddScoped<GrantRoleHandler>();
        services.AddScoped<RevokeRoleHandler>();
        services.AddScoped<IssueApiKeyHandler>();
        services.AddScoped<OidcAccountLinker>();
        services.AddSingleton<IOidcClientSecrets, OidcClientSecrets>();
        services.AddHttpClient<IOidcDiscovery, OidcDiscoveryCache>();
        services.AddHttpClient<OidcTokenExchange>();
        services.AddSingleton<OidcIdTokenValidator>();
        services.AddScoped<OidcStartHandler>();
        services.AddScoped<OidcCallbackHandler>();

        services.AddScoped<IValidator<CreateUserCommand>, CreateUserValidator>();
        services.AddScoped<IValidator<LoginCommand>, LoginValidator>();
        services.AddScoped<IValidator<GrantRoleCommand>, GrantRoleValidator>();
        services.AddScoped<IValidator<IssueApiKeyCommand>, IssueApiKeyValidator>();

        return services;
    }
}

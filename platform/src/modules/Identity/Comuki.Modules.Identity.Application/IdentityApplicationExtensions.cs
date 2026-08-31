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
        _ = services.AddMemoryCache();

        _ = services.AddOptions<ApiKeyOptions>()
            .ValidateDataAnnotations()
            .ValidateOnStart();

        _ = services.AddSingleton<IPermissionCatalog, RoleMatrixPermissionCatalog>();
        _ = services.AddScoped<IPermissionEvaluator, PermissionEvaluator>();

        _ = services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
        _ = services.AddSingleton<ApiKeyHasher>();
        _ = services.AddScoped<ApiKeyIssuer>();

        _ = services.AddScoped<CreateUserHandler>();
        _ = services.AddScoped<LoginHandler>();
        _ = services.AddScoped<GrantRoleHandler>();
        _ = services.AddScoped<RevokeRoleHandler>();
        _ = services.AddScoped<IssueApiKeyHandler>();
        _ = services.AddScoped<OidcAccountLinker>();

        _ = services.AddScoped<IValidator<CreateUserCommand>, CreateUserValidator>();
        _ = services.AddScoped<IValidator<LoginCommand>, LoginValidator>();
        _ = services.AddScoped<IValidator<GrantRoleCommand>, GrantRoleValidator>();
        _ = services.AddScoped<IValidator<IssueApiKeyCommand>, IssueApiKeyValidator>();

        return services;
    }
}

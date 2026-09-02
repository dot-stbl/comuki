using Comuki.Modules.Identity.Application.Ports;
using Comuki.Modules.Identity.Infrastructure.Persistence;
using Comuki.Modules.Identity.Infrastructure.Persistence.Stores;
using Microsoft.Extensions.DependencyInjection;

namespace Comuki.Modules.Identity.Infrastructure;

/// <summary>Registration entry point for Identity persistence.</summary>
public static class IdentityPersistenceExtensions
{
    /// <summary>
    /// Registers <see cref="IdentityDbContext"/> (Npgsql + snake_case +
    /// private migrations history via <see cref="IdentityDbContext.ApplyOptions"/>)
    /// and the store implementations behind the Application ports.
    /// Scoped — one context per unit of work / request.
    /// </summary>
    /// <param name="services"></param>
    /// <param name="connectionString"></param>
    public static IServiceCollection AddIdentityPersistence(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContext<IdentityDbContext>(options =>
            IdentityDbContext.ApplyOptions(options, connectionString));

        services.AddScoped<IUserAccountStore, UserAccountStore>();
        services.AddScoped<IRoleAssignmentStore, RoleAssignmentStore>();
        services.AddScoped<IApiKeyStore, ApiKeyStore>();
        services.AddScoped<IOidcLinkStore, OidcLinkStore>();

        return services;
    }
}

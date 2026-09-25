using Comuki.Modules.Repositories.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Comuki.Modules.Repositories.Infrastructure;

/// <summary>
/// Registration entry point for the Repositories module.
/// </summary>
public static class RepositoriesPersistenceExtensions
{
    /// <summary>
    /// Registers <see cref="RepositoriesDbContext"/> via an EF Core
    /// <c>DbContextFactory&lt;T&gt;</c> (Npgsql + snake_case + private
    /// migrations history <c>repositories.__comuki_repositories</c>
    /// configured through <see cref="RepositoriesDbContext.ApplyOptions"/>).
    /// Host composition — the place that calls
    /// <c>AddRepositoriesModule(connectionString)</c> from <c>HostComposer</c>
    /// — lands in workstream 9; stores and any handlers arrive in
    /// workstream 2 once the Application layer starts carrying ports.
    /// </summary>
    /// <param name="services"></param>
    /// <param name="connectionString"></param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddRepositoriesModule(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContextFactory<RepositoriesDbContext>(options =>
            RepositoriesDbContext.ApplyOptions(options, connectionString));

        return services;
    }
}

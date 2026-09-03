using Comuki.Modules.Artifacts.Application.Packaging;
using Comuki.Modules.Artifacts.Infrastructure.Persistence;
using Comuki.Modules.Artifacts.Infrastructure.Store;
using Comuki.Shared.Contracts.Artifacts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Comuki.Modules.Artifacts.Infrastructure;

/// <summary>
/// Registration entry point for the Artifacts infrastructure layer. Wires
/// the MinIO-backed artifact store, the EF-backed bundle store and the
/// <see cref="ArtifactsOptions"/> binding. Application-layer composition
/// (<see cref="Application.ArtifactsApplicationExtensions.AddArtifactsApplication"/>)
/// is separate.
/// </summary>
public static class ArtifactsPersistenceExtensions
{
    /// <summary>
    /// Registers the <see cref="ArtifactsDbContext"/> (Npgsql + snake_case +
    /// private history table at <c>artifacts.__ef_migrations_history</c>),
    /// the EF-backed bundle store and the MinIO artifact store. The MinIO
    /// client is registered as a singleton; the store facade is also a
    /// singleton (stateless).
    /// </summary>
    /// <param name="services"></param>
    /// <param name="connectionString">Postgres connection string (used for the bookkeeping schema).</param>
    /// <param name="configuration">Configuration root — binds <see cref="ArtifactsOptions"/> from <c>Artifacts</c>.</param>
    public static IServiceCollection AddArtifactsPersistence(
        this IServiceCollection services,
        string connectionString,
        IConfiguration configuration)
    {
        services.AddOptions<ArtifactsOptions>()
            .Bind(configuration.GetSection(ArtifactsOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddDbContext<ArtifactsDbContext>(options =>
            ArtifactsDbContext.ApplyOptions(options, connectionString));

        services.AddScoped<IRunArtifactBundleStore, EfRunArtifactBundleStore>();

        services.AddSingleton(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<ArtifactsOptions>>().Value;
            return MinioClientFactory.Create(options);
        });

        services.AddSingleton<IRunArtifactStore, MinioRunArtifactStore>();

        return services;
    }
}

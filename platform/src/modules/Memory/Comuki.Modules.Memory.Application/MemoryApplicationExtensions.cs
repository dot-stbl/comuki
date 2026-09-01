using Comuki.Modules.Memory.Application.Digest;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Comuki.Modules.Memory.Application;

/// <summary>
/// Composition of the Memory application layer: the digest service over
/// the memory store port. The port is satisfied by the infrastructure
/// installer (EF/Npgsql) or by a fake in tests; nothing here touches EF.
/// </summary>
public static class MemoryApplicationExtensions
{
    /// <summary>Registers the Memory application services.</summary>
    /// <param name="services"></param>
    public static IServiceCollection AddMemoryApplication(this IServiceCollection services)
    {
        services.TryAddSingleton<MemoryDigest>();
        return services;
    }
}

using Comuki.Modules.Memory.Application.Digest;
using Comuki.Modules.Memory.Application.Learning;
using Comuki.Modules.Memory.Application.Learning.Rules;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Comuki.Modules.Memory.Application;

/// <summary>
/// Composition of the Memory application layer: the digest service over
/// the memory store port plus its <see cref="Shared.Contracts.Memory.IMemoryDigest"/>
/// contract adapter (what the chat graph's ThinkNode consumes), and the
/// learning loop's decision service with its rule publisher. The store
/// ports are satisfied by the infrastructure installer (EF/Npgsql) or by a
/// fake in tests; nothing here touches EF.
/// </summary>
public static class MemoryApplicationExtensions
{
    /// <summary>Registers the Memory application services.</summary>
    /// <param name="services"></param>
    public static IServiceCollection AddMemoryApplication(this IServiceCollection services)
    {
        services.TryAddSingleton<MemoryDigest>();
        services.AddSingleton<Shared.Contracts.Memory.IMemoryDigest, ComukiMemoryDigest>();
        services.AddSingleton<LearningApprovalService>();
        services.AddSingleton<ILearningRulePublisher, MemoryLearningRulePublisher>();
        return services;
    }
}

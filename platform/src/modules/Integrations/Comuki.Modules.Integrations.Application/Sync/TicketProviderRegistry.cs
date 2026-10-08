using Comuki.Modules.Integrations.Application.Ports.Sources;
using Comuki.Modules.Integrations.Application.Ports.Sync;

namespace Comuki.Modules.Integrations.Application.Sync;

/// <summary>
/// Resolves the registered provider implementations by kebab-case source
/// key. Every registration contributes (tests may register fakes before
/// the module — DI resolves the IEnumerable in registration order, so
/// the first match wins and a pre-registered fake shadows the real one).
/// </summary>
/// <remarks>Builds the registry from the registered provider implementations.</remarks>
/// <param name="registeredSources"></param>
/// <param name="registeredSyncPorts"></param>
public sealed class TicketProviderRegistry(
    IEnumerable<ITicketSourceProvider> registeredSources,
    IEnumerable<IIntegrationSyncPort> registeredSyncPorts)
{
    private readonly IReadOnlyList<ITicketSourceProvider> sources = [.. registeredSources];
    private readonly IReadOnlyList<IIntegrationSyncPort> syncPorts = [.. registeredSyncPorts];

    /// <summary>The source provider serving the key; null when unregistered.</summary>
    /// 
    public ITicketSourceProvider? FindSource(string sourceKey)
    {
        return sources.FirstOrDefault(provider => string.Equals(provider.SourceKey, sourceKey, StringComparison.Ordinal));
    }

    /// <summary>The sync port serving the key; null when unregistered (e.g. the native source).</summary>
    /// 
    public IIntegrationSyncPort? FindSync(string sourceKey)
    {
        return syncPorts.FirstOrDefault(port => string.Equals(port.SourceKey, sourceKey, StringComparison.Ordinal));
    }
}

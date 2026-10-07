using Comuki.Modules.Integrations.Domain.Connections;
using Comuki.Modules.Integrations.Domain.Items;

namespace Comuki.Modules.Integrations.Application.Ports.Admission;

/// <summary>
/// The integrations profile-router port — picks the worker profile key
/// for an admitted ticket. The integrations module never references the
/// engine; the host composes the implementation (mirroring
/// <c>IRunLauncher</c>). Without this seam every admitted ticket would
/// land on the same profile (typically <c>implement</c>) — a foreign PR
/// would then start writing code instead of reviewing it.
/// </summary>
public interface IIntegrationProfileRouter
{
    /// <summary>
    /// Resolves the profile key the run for <paramref name="ticket"/>
    /// should claim on, given the source connection (its settings carry
    /// per-source overrides; null for native tickets with no source).
    /// Implementations SHOULD honor an explicit per-connection override
    /// when present, fall back to the kind-based default, and never
    /// throw.
    /// </summary>
    /// <param name="connection"></param>
    /// <param name="ticket"></param>
    /// <returns></returns>
    public string ResolveProfileKey(SourceConnection? connection, InboundItem ticket);
}

namespace Comuki.Modules.Observability.Domain;

/// <summary>
/// Marker for the observability boundary exception family. Concrete
/// exceptions (<see cref="VictoriaUnavailableException"/>) derive from
/// it so the MCP error-mapping layer can branch on the family and
/// translate to the <c>observability.victoria_unavailable</c> /
/// <c>observability.permission_denied</c> typed ProblemDetails codes
/// the spec defines. The hierarchy lives in the Domain project so
/// application-layer code never depends on infrastructure-layer packages
/// (e.g. Refit / HTTP) just to surface a failure.
/// </summary>
public abstract class ObservabilityDomainException(string code, string message, Exception? inner = null)
    : Exception(message, inner)
{
    /// <summary>The stable machine-readable code; consumers branch on this string.</summary>
    public string Code { get; } = code;
}

namespace Comuki.Modules.Observability.Domain;

/// <summary>
/// Raised when the configured Victoria endpoint is unreachable for the
/// full timeout. The MCP tool surface translates this to a typed
/// ProblemDetails with <c>code = observability.victoria_unavailable</c>
/// per <c>specs/observability/spec.md</c> "Endpoint unreachable" scenario.
/// </summary>
public sealed class VictoriaUnavailableException(string endpoint, Exception? inner = null)
    : ObservabilityDomainException(
        VictoriaUnavailableCode,
        $"Victoria endpoint '{endpoint}' is unreachable: {inner?.Message ?? "no response within the timeout"}.")
{
    /// <summary>Stable error code: <c>observability.victoria_unavailable</c>.</summary>
    public const string VictoriaUnavailableCode = "observability.victoria_unavailable";

    /// <summary>The endpoint URL the client tried to reach.</summary>
    public string Endpoint { get; } = endpoint;

    /// <summary>The transport-level exception, when one is available.</summary>
    public Exception? Inner { get; } = inner;
}

namespace Comuki.Modules.Observability.Infrastructure.Endpoint;

/// <summary>
/// The reserved port numbers the deploy compose stack binds for the
/// Victoria observability pair. The
/// <see cref="Application.Ports.IVictoriaEndpointResolver"/>
/// reads these constants when constructing the base URLs at startup —
/// hardcoding them here keeps the wire contract a single source of
/// truth that matches <c>deploy/docker-compose.yml</c> and
/// <c>.agents/rules/process/ports.md</c>.
/// </summary>
internal static class VictoriaPorts
{
    /// <summary>VictoriaMetrics bind port (compose <c>8428</c>:8428).</summary>
    public const int MetricsContainerPort = 8428;

    /// <summary>VictoriaLogs bind port (compose <c>9428</c>:9428).</summary>
    public const int LogsContainerPort = 9428;
}

/// <summary>
/// Service-name defaults the deploy stack binds to the
/// <c>comuki-net</c> docker network. Operators can override each
/// base URL via the <c>Observability:Victoria:*BaseUrl</c> config
/// paths; the resolver falls back to these defaults when no override
/// is configured (the env override is
/// <c>COMUKI_OBSERVABILITY_VICTORIA_*_BASE_URL</c> per the standard
/// single-underscore nesting convention).
/// </summary>
internal static class VictoriaServices
{
    /// <summary>VictoriaMetrics service name in the compose network.</summary>
    public const string MetricsServiceName = "victoria-metrics";

    /// <summary>VictoriaLogs service name in the compose network.</summary>
    public const string LogsServiceName = "victoria-logs";
}

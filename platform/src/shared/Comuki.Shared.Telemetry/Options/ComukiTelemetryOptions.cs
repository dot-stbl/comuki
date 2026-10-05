using System.ComponentModel.DataAnnotations;

namespace Comuki.Shared.Telemetry.Options;

/// <summary>
/// Telemetry settings. When <see cref="OtlpEndpoint"/> is unset the
/// installer wires no OpenTelemetry SDK at all — the instruments stay
/// cheap no-ops, which keeps unit tests and local runs silent.
/// <see cref="LogsOtlpEndpoint"/> is independent: the logs leg goes to a
/// VictoriaLogs receiver (which serves OTLP at <c>/insert/opentelemetry/v1/logs</c>),
/// and is enabled when the URL is set — the metrics/traces leg stays
/// gated on <see cref="OtlpEndpoint"/>. Both URLs MAY point at the same
/// process (a future OTel collector sits on the host); today the deploy
/// stack separates the two because the gRPC receiver is on
/// VictoriaMetrics (:8431) and the logs HTTP receiver is on
/// VictoriaLogs (:9428).
/// </summary>
public sealed class ComukiTelemetryOptions
{
    public const string SectionName = "Telemetry";

    /// <summary>Service name stamped on the OTel resource.</summary>
    [Required]
    [MinLength(1)]
    public string ServiceName { get; init; } = "comuki-orchestrator";

    /// <summary>
    /// OTLP gRPC endpoint (e.g. <c>http://localhost:8431</c> — the
    /// VictoriaMetrics OTLP receiver of the deploy compose stack). Null
    /// disables the exporter and the whole SDK wiring.
    /// </summary>
    [Url]
    public Uri? OtlpEndpoint { get; init; }

    /// <summary>
    /// OTLP/HTTP endpoint for structured MEL logs (the VictoriaLogs
    /// receiver at <c>http://victoria-logs:9428/insert/opentelemetry/v1/logs</c>
    /// in the deploy stack). Null disables the log leg only; the
    /// traces/metrics leg is independent. The URL MUST include the
    /// full receiver path (<c>/insert/opentelemetry/v1/logs</c>) — the
    /// installer wires it to <c>OpenTelemetry.Exporter</c> verbatim.
    /// </summary>
    [Url]
    public Uri? LogsOtlpEndpoint { get; init; }
}

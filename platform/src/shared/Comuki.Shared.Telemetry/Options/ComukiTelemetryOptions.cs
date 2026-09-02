using System.ComponentModel.DataAnnotations;

namespace Comuki.Shared.Telemetry.Options;

/// <summary>
/// Telemetry settings. When <see cref="OtlpEndpoint"/> is unset the
/// installer wires no OpenTelemetry SDK at all — the instruments stay
/// cheap no-ops, which keeps unit tests and local runs silent.
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
}

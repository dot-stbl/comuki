using System.ComponentModel.DataAnnotations;

namespace Comuki.Modules.Observability.Application.Options;

/// <summary>
/// Victoria stack tuning — the <c>Observability:Victoria:*</c> section
/// of the host config. The fields are the deploy-side knobs the
/// operator dials per the spec: <see cref="RetentionPeriod"/> carries
/// the value the docker-compose stack reads on its next start (no
/// code-level default — the deploy baseline ships
/// <c>--retentionPeriod=1</c> per the platform); <see cref="ScrapeInterval"/>
/// is the resolution at which the typed <c>observability.metrics.query</c>
/// client asks VictoriaMetrics for samples (range 5s–5m per the spec,
/// 15s default). <see cref="LogsBaseUrl"/> and <see cref="MetricsBaseUrl"/>
/// are operator overrides for the typed query clients' base URL —
/// unset means the deploy stack's compose service name
/// (<c>http://victoria-logs:9428</c> / <c>http://victoria-metrics:8428</c>)
/// is used. <see cref="IValidatableObject.Validate"/> carries the
/// cross-field rule that an override, if set, must be an http(s) URL
/// (the <c>[Url]</c> attribute checks shape but not the scheme).
/// </summary>
public sealed class ObservabilityOptions : IValidatableObject
{
    /// <summary>Configuration section: <c>Observability:Victoria</c>.</summary>
    public const string SectionName = "Observability:Victoria";

    /// <summary>Victoria retention. Optional — the deploy baseline owns the default.</summary>
    public string? RetentionPeriod { get; init; }

    /// <summary>Victoria scrape interval; 15s default per the spec, range 5s–5m.</summary>
    [Range(typeof(TimeSpan), "00:00:05", "00:05:00",
        ErrorMessage = "Observability:Victoria:ScrapeInterval must be in the 5s..5m range (the spec-mandated bounds).")]
    public TimeSpan ScrapeInterval { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>Optional operator override for the VictoriaLogs base URL (scheme + host + port).</summary>
    [Url]
    public Uri? LogsBaseUrl { get; init; }

    /// <summary>Optional operator override for the VictoriaMetrics base URL (scheme + host + port).</summary>
    [Url]
    public Uri? MetricsBaseUrl { get; init; }

    /// <summary>
    /// Cross-field rule: a base URL override, if set, must be an
    /// absolute HTTP/HTTPS URL. The <c>[Url]</c> attribute checks the
    /// shape but does not require <c>http</c> / <c>https</c> as the
    /// scheme; this validator does.
    /// </summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (LogsBaseUrl is { Scheme: not ("http" or "https") })
        {
            yield return new ValidationResult(
                "Observability:Victoria:LogsBaseUrl must be an http(s) URL.",
                [nameof(LogsBaseUrl)]);
        }
        if (MetricsBaseUrl is { Scheme: not ("http" or "https") })
        {
            yield return new ValidationResult(
                "Observability:Victoria:MetricsBaseUrl must be an http(s) URL.",
                [nameof(MetricsBaseUrl)]);
        }
    }
}

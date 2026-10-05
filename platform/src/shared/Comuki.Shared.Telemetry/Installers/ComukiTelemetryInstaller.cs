using Comuki.Shared.Telemetry.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Comuki.Shared.Telemetry.Installers;

/// <summary>
/// Wires the OpenTelemetry SDK with the OTLP exporter for the Comuki
/// business signals: the three meters (<c>comuki.queue / runs / compute</c>)
/// and the activity sources of the instrumented assemblies. Registered once
/// per host in its composition root; the Migrator skips it deliberately —
/// a schema tool emits no business telemetry.
/// </summary>
public static class ComukiTelemetryInstaller
{
    /// <summary>
    /// Adds telemetry when <c>Telemetry:OtlpEndpoint</c> is configured;
    /// otherwise a no-op (options are still registered and validated).
    /// The MEL → OTLP log leg is independent: it activates only when
    /// <c>Telemetry:LogsOtlpEndpoint</c> is set (a VictoriaLogs-style
    /// receiver — see <see cref="ComukiTelemetryOptions.LogsOtlpEndpoint"/>).
    /// </summary>
    /// <param name="services"></param>
    /// <param name="configuration"></param>
    public static IServiceCollection AddComukiTelemetry(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ComukiTelemetryOptions>()
            .Bind(configuration.GetSection(ComukiTelemetryOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Read once for conditional wiring — the options type itself carries
        // no OTel SDK dependency, so binding before BuildServiceProvider is safe.
        var telemetryOptions = configuration.GetSection(ComukiTelemetryOptions.SectionName).Get<ComukiTelemetryOptions>()
            ?? new ComukiTelemetryOptions();
        if (telemetryOptions.OtlpEndpoint is null && telemetryOptions.LogsOtlpEndpoint is null)
        {
            return services;
        }

        // OTel SDK resource carries the same service identity for every
        // signal — the three legs are chained on the same builder, so the
        // traces/metrics/logs resource stays shared.
        var openTelemetry = services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(
                serviceName: telemetryOptions.ServiceName));

        if (telemetryOptions.OtlpEndpoint is not null)
        {
            openTelemetry
                .WithTracing(tracing => tracing
                    .AddSource(ComukiInstrumentation.OrchestrationSourceName)
                    .AddSource(ComukiInstrumentation.ComputeSourceName)
                    .AddSource(ComukiInstrumentation.HostSourceName)
                    .AddOtlpExporter(exporter => exporter.Endpoint = telemetryOptions.OtlpEndpoint))
                .WithMetrics(metrics => metrics
                    .AddMeter(ComukiInstrumentation.QueueMeterName)
                    .AddMeter(ComukiInstrumentation.RunsMeterName)
                    .AddMeter(ComukiInstrumentation.ComputeMeterName)
                    .AddMeter(ComukiInstrumentation.ArtifactsMeterName)
                    .AddMeter(ComukiInstrumentation.ProjectsMeterName)
                    .AddOtlpExporter(exporter => exporter.Endpoint = telemetryOptions.OtlpEndpoint));
        }

        if (telemetryOptions.LogsOtlpEndpoint is not null)
        {
            // MEL → OTLP/HTTP (protobuf) → VictoriaLogs at /insert/opentelemetry/v1/logs.
            // Logs use HTTP protobuf (not gRPC) because the VictoriaLogs
            // receiver serves that path. The exporter accepts the FULL
            // OTLP/HTTP URL — operators configure the full path
            // (http://victoria-logs:9428/insert/opentelemetry/v1/logs).
            // The console leg is registered by ComukiBootstrapExtensions
            // AddComukiConsole — we only add the OTLP sink here.
            //
            // Note on the logger-builder surface: the WithLogging callback
            // takes the abstract LoggerProviderBuilder, on which the
            // OtlpLogExporterHelperExtensions.AddOtlpExporter extension
            // (shipped with OpenTelemetry.Exporter.OpenTelemetryProtocol)
            // is defined. The IncludeFormattedMessage / IncludeScopes
            // fluent accessors live on the concrete
            // OpenTelemetryLoggerProviderBuilder (shipped with
            // OpenTelemetry.Extensions.Logging, not in this project's
            // package set), so we leave the SDK defaults — the
            // StructuredLogScope instrumentation and the formatter-flag
            // defaults on OpenTelemetryLoggerOptions are sufficient for
            // VictoriaLogs's expected OTel payload shape.
            openTelemetry.WithLogging(logs =>
            {
                logs.AddOtlpExporter(exporter =>
                {
                    exporter.Endpoint = telemetryOptions.LogsOtlpEndpoint;
                    exporter.Protocol = OtlpExportProtocol.HttpProtobuf;
                });
            });
        }

        return services;
    }
}

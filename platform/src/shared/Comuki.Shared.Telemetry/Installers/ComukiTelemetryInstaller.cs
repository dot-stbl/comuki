using Comuki.Shared.Telemetry.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
        if (telemetryOptions.OtlpEndpoint is null)
        {
            return services;
        }

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(
                serviceName: telemetryOptions.ServiceName))
            .WithTracing(tracing => tracing
                .AddSource(ComukiInstrumentation.OrchestrationSourceName)
                .AddSource(ComukiInstrumentation.ComputeSourceName)
                .AddSource(ComukiInstrumentation.HostSourceName)
                .AddOtlpExporter(exporter => exporter.Endpoint = telemetryOptions.OtlpEndpoint))
            .WithMetrics(metrics => metrics
                .AddMeter(ComukiInstrumentation.QueueMeterName)
                .AddMeter(ComukiInstrumentation.RunsMeterName)
                .AddMeter(ComukiInstrumentation.ComputeMeterName)
                .AddOtlpExporter(exporter => exporter.Endpoint = telemetryOptions.OtlpEndpoint));

        return services;
    }
}

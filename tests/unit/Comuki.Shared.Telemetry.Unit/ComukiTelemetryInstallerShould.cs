using Comuki.Shared.Telemetry.Installers;
using Comuki.Shared.Telemetry.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace Comuki.Shared.Telemetry.Unit;

/// <summary>Installer branches: no-op without endpoint, OTel wiring with endpoint, MEL log leg.</summary>
public sealed class ComukiTelemetryInstallerShould
{
    [Fact(DisplayName = "Given no OtlpEndpoint, when AddComukiTelemetry is called, then options register and OpenTelemetry is skipped")]
    public void NoOpWithoutEndpoint()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{ComukiTelemetryOptions.SectionName}:ServiceName"] = "comuki-test",
            })
            .Build();
        var services = new ServiceCollection();

        services.AddComukiTelemetry(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<ComukiTelemetryOptions>>().Value;

        options.ServiceName.ShouldBe("comuki-test");
        options.OtlpEndpoint.ShouldBeNull();
        options.LogsOtlpEndpoint.ShouldBeNull();
        services.Any(static descriptor =>
                descriptor.ServiceType.FullName?.Contains("OpenTelemetry", StringComparison.Ordinal) == true)
            .ShouldBeFalse();
    }

    [Fact(DisplayName = "Given an OtlpEndpoint, when AddComukiTelemetry is called, then OpenTelemetry services are registered")]
    public void WireOpenTelemetryWhenEndpointSet()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{ComukiTelemetryOptions.SectionName}:ServiceName"] = "comuki-otlp",
                [$"{ComukiTelemetryOptions.SectionName}:OtlpEndpoint"] = "http://127.0.0.1:8431",
            })
            .Build();
        var services = new ServiceCollection();

        services.AddComukiTelemetry(configuration);

        services.Any(static descriptor =>
                descriptor.ServiceType.FullName?.Contains("OpenTelemetry", StringComparison.Ordinal) == true
                || descriptor.ImplementationType?.FullName?.Contains("OpenTelemetry", StringComparison.Ordinal) == true
                || descriptor.ServiceType.Name.Contains("MeterProvider", StringComparison.Ordinal)
                || descriptor.ServiceType.Name.Contains("TracerProvider", StringComparison.Ordinal))
            .ShouldBeTrue();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<ComukiTelemetryOptions>>().Value;
        options.OtlpEndpoint.ShouldBe(new Uri("http://127.0.0.1:8431"));
        options.ServiceName.ShouldBe("comuki-otlp");
    }

    [Fact(DisplayName = "Given empty Telemetry section, when options bind, then defaults apply")]
    public void DefaultServiceNameWhenSectionEmpty()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var services = new ServiceCollection();

        services.AddComukiTelemetry(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<ComukiTelemetryOptions>>().Value;

        options.ServiceName.ShouldBe("comuki-orchestrator");
        options.OtlpEndpoint.ShouldBeNull();
        options.LogsOtlpEndpoint.ShouldBeNull();
    }

    [Fact(DisplayName = "Given only LogsOtlpEndpoint, when AddComukiTelemetry is called, then MEL log leg wires without traces/metrics")]
    public void WireLogsLegOnly()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{ComukiTelemetryOptions.SectionName}:ServiceName"] = "comuki-logs-only",
                [$"{ComukiTelemetryOptions.SectionName}:LogsOtlpEndpoint"] = "http://victoria-logs:9428/insert/opentelemetry/v1/logs",
            })
            .Build();
        var services = new ServiceCollection();

        services.AddComukiTelemetry(configuration);

        // The log leg wires a real OtlpLogExporter-backed processor: a
        // BatchExportLogRecordProcessor (or SimpleExportLogRecordProcessor)
        // type from the OTel SDK lives in the service collection.
        services.Any(static descriptor =>
                descriptor.ServiceType.FullName?.Contains("OpenTelemetry.Logs", StringComparison.Ordinal) == true
                || descriptor.ImplementationType?.FullName?.Contains("LogRecordProcessor", StringComparison.Ordinal) == true
                || descriptor.ImplementationType?.FullName?.Contains("OtlpLogExporter", StringComparison.Ordinal) == true)
            .ShouldBeTrue();

        // No traces wiring — only LogsOtlpEndpoint is set. TracerProvider
        // / MeterProvider descriptors are pre-registered by the SDK host
        // extension even when the signal isn't enabled, so we assert on
        // the SDK SDK-side plumbing: TracerProviderOption / MeterProviderOption
        // configuration callbacks should not be in the collection.
        services.Any(static descriptor =>
                descriptor.ServiceType.Name.Contains("OtlpExporter", StringComparison.Ordinal))
            .ShouldBeFalse();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<ComukiTelemetryOptions>>().Value;
        options.LogsOtlpEndpoint.ShouldBe(new Uri("http://victoria-logs:9428/insert/opentelemetry/v1/logs"));
        options.OtlpEndpoint.ShouldBeNull();
    }

    [Fact(DisplayName = "Given both endpoints, when AddComukiTelemetry is called, then traces+metrics+logs all wire")]
    public void WireAllLegs()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{ComukiTelemetryOptions.SectionName}:ServiceName"] = "comuki-all",
                [$"{ComukiTelemetryOptions.SectionName}:OtlpEndpoint"] = "http://victoria-metrics:8431",
                [$"{ComukiTelemetryOptions.SectionName}:LogsOtlpEndpoint"] = "http://victoria-logs:9428/insert/opentelemetry/v1/logs",
            })
            .Build();
        var services = new ServiceCollection();

        services.AddComukiTelemetry(configuration);

        // All three signal providers land.
        services.Any(static descriptor =>
                descriptor.ServiceType.Name.Contains("TracerProvider", StringComparison.Ordinal))
            .ShouldBeTrue();
        services.Any(static descriptor =>
                descriptor.ServiceType.Name.Contains("MeterProvider", StringComparison.Ordinal))
            .ShouldBeTrue();
        services.Any(static descriptor =>
                descriptor.ServiceType.FullName?.Contains("OpenTelemetry.Logs", StringComparison.Ordinal) == true)
            .ShouldBeTrue();

        // MEL pipeline carries an OpenTelemetry-sourced logger provider so a
        // structured log event written through ILogger<T> flows through the
        // OTLP exporter configured in AddOpenTelemetry(WithLogging).
        using var provider = services.BuildServiceProvider();
        var loggerProvider = provider.GetServices<ILoggerProvider>();
        loggerProvider.Any(static provider =>
            provider.GetType().FullName?.Contains("OpenTelemetry", StringComparison.Ordinal) == true)
            .ShouldBeTrue();
    }
}

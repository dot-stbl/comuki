using Comuki.Shared.Telemetry.Installers;
using Comuki.Shared.Telemetry.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace Comuki.Shared.Telemetry.Unit;

/// <summary>Installer branches: no-op without endpoint, OTel wiring with endpoint.</summary>
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

        _ = services.AddComukiTelemetry(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<ComukiTelemetryOptions>>().Value;

        options.ServiceName.ShouldBe("comuki-test");
        options.OtlpEndpoint.ShouldBeNull();
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

        _ = services.AddComukiTelemetry(configuration);

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

        _ = services.AddComukiTelemetry(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<ComukiTelemetryOptions>>().Value;

        options.ServiceName.ShouldBe("comuki-orchestrator");
        options.OtlpEndpoint.ShouldBeNull();
    }
}

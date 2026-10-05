using Comuki.Modules.Observability.Application.Options;
using Comuki.Modules.Observability.Infrastructure.Endpoint;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Observability.Unit;

/// <summary>
/// Unit tests for the typed <see cref="VictoriaEndpointResolver"/>:
/// the override-then-compose-service-name fallback per the
/// <c>specs/observability/spec.md</c> "Default endpoint" section. The
/// resolver is the seam between the typed options and the wire-level
/// HTTP clients; here we assert that seam, not the HTTP round-trip.
/// </summary>
public sealed class VictoriaEndpointResolverShould
{
    /// <summary>Default resolver: logs port is 9428, metrics is 8428 (the operator-baseline compose ports).</summary>
    [Fact(DisplayName = "Given no overrides, when ResolveLogsUrl, then http://victoria-logs:9428/")]
    public void DefaultLogsUrlIsComposeService()
    {
        var resolver = BuildResolver();

        var uri = resolver.ResolveLogsUrl(TestContext.Current.CancellationToken);

        uri.ToString().ShouldStartWith("http://victoria-logs:9428/");
    }

    /// <summary>Default metrics URL falls back to the compose service name too.</summary>
    [Fact(DisplayName = "Given no overrides, when ResolveMetricsUrl, then http://victoria-metrics:8428/")]
    public void DefaultMetricsUrlIsComposeService()
    {
        var resolver = BuildResolver();

        var uri = resolver.ResolveMetricsUrl(TestContext.Current.CancellationToken);

        uri.ToString().ShouldStartWith("http://victoria-metrics:8428/");
    }

    /// <summary>Override is the explicit operator contract: local dev sets a host:port here.</summary>
    [Fact(DisplayName = "Given LogsBaseUrl override, when ResolveLogsUrl, then the override wins")]
    public void LogsOverrideWins()
    {
        var resolver = BuildResolver(new ObservabilityOptions
        {
            LogsBaseUrl = new Uri("http://127.0.0.1:19428"),
        });

        var uri = resolver.ResolveLogsUrl(TestContext.Current.CancellationToken);

        uri.ToString().ShouldBe("http://127.0.0.1:19428/");
    }

    /// <summary>Same override rule on the metrics URL.</summary>
    [Fact(DisplayName = "Given MetricsBaseUrl override, when ResolveMetricsUrl, then the override wins")]
    public void MetricsOverrideWins()
    {
        var resolver = BuildResolver(new ObservabilityOptions
        {
            MetricsBaseUrl = new Uri("https://metrics.internal.example.com:18428"),
        });

        var uri = resolver.ResolveMetricsUrl(TestContext.Current.CancellationToken);

        uri.ToString().ShouldBe("https://metrics.internal.example.com:18428/");
    }

    /// <summary>
    /// Construct the resolver with a null-object IHttpClientFactory
    /// (the tests in this class exercise <c>ResolveLogsUrl</c> /
    /// <c>ResolveMetricsUrl</c> — the HTTP probe path is covered by
    /// <see cref="VictoriaEndpointResolverProbeShould"/>).
    /// </summary>
    private static VictoriaEndpointResolver BuildResolver(ObservabilityOptions? options = null)
    {
        var monitor = Options.Create(options ?? new ObservabilityOptions());
        var factory = new NullHttpClientFactory();
        var logger = NullLogger<VictoriaEndpointResolver>.Instance;
        return new VictoriaEndpointResolver(monitor, factory, logger);
    }

    /// <summary>Null-object HttpClient factory for tests that don't touch the probe path.</summary>
    private sealed class NullHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new();
        }
    }
}

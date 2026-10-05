using System.Net;
using Comuki.Modules.Observability.Application.Options;
using Comuki.Modules.Observability.Infrastructure.Endpoint;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Observability.Unit;

/// <summary>
/// Unit tests for <see cref="VictoriaEndpointResolver.ProbeAsync"/>:
/// the health probe the host registers as <c>IHealthCheck</c>. We
/// stub the <see cref="IHttpClientFactory"/> with a
/// <see cref="DelegatingHandler"/> that returns a fixed
/// <see cref="HttpResponseMessage"/>, so the contract is "the probe
/// correctly maps HTTP 2xx / non-2xx / network failure to a stable
/// short string the host surfaces as the health-check status".
/// </summary>
public sealed class VictoriaEndpointResolverProbeShould
{
    /// <summary>HTTP 200 → <c>"victoria:ok"</c>.</summary>
    [Fact(DisplayName = "Given a 200 from /health, when ProbeAsync, then it returns \"victoria:ok\"")]
    public async Task OkResponseMapsToVictoriaOkAsync()
    {
        var resolver = BuildResolver(HttpStatusCode.OK);

        var status = await resolver.ProbeAsync(TestContext.Current.CancellationToken);

        status.ShouldBe("victoria:ok");
    }

    /// <summary>HTTP non-2xx → <c>"victoria:http_{code}"</c> (the host renders the code as part of the message).</summary>
    [Fact(DisplayName = "Given a 503 from /health, when ProbeAsync, then it returns \"victoria:http_503\"")]
    public async Task NonOkResponseMapsToStatusCodeAsync()
    {
        var resolver = BuildResolver(HttpStatusCode.ServiceUnavailable);

        var status = await resolver.ProbeAsync(TestContext.Current.CancellationToken);

        status.ShouldBe("victoria:http_503");
    }

    /// <summary>Network failure → <c>"victoria:unreachable"</c>; the exception is logged at debug.</summary>
    [Fact(DisplayName = "Given the connection refused, when ProbeAsync, then it returns \"victoria:unreachable\"")]
    public async Task NetworkFailureMapsToUnreachableAsync()
    {
        var resolver = BuildResolver(new HttpRequestException("connection refused"));

        var status = await resolver.ProbeAsync(TestContext.Current.CancellationToken);

        status.ShouldBe("victoria:unreachable");
    }

    /// <summary>Probe hit the resolver's <see cref="ObservabilityOptions.MetricsBaseUrl"/> endpoint + <c>/health</c>.</summary>
    [Fact(DisplayName = "Given a MetricsBaseUrl override, when ProbeAsync, then it pings override/health")]
    public async Task ProbeUriHonoursMetricsBaseUrlOverrideAsync()
    {
        var probe = new RecordingHandler();
        var options = new ObservabilityOptions
        {
            MetricsBaseUrl = new Uri("http://127.0.0.1:19428"),
        };
        var resolver = new VictoriaEndpointResolver(
            Options.Create(options),
            new StubFactory(probe),
            NullLogger<VictoriaEndpointResolver>.Instance);

        await resolver.ProbeAsync(TestContext.Current.CancellationToken);

        probe.LastRequestUri.ShouldNotBeNull();
        probe.LastRequestUri.ToString().ShouldStartWith("http://127.0.0.1:19428/health");
    }

    private static VictoriaEndpointResolver BuildResolver(HttpStatusCode status)
    {
        return BuildResolverCore(_ => new HttpResponseMessage(status), throwOnSend: null);
    }

    private static VictoriaEndpointResolver BuildResolver(HttpRequestException throwOnSend)
    {
        return BuildResolverCore(_ => throw throwOnSend, throwOnSend);
    }

    private static VictoriaEndpointResolver BuildResolverCore(
        Func<HttpRequestMessage, HttpResponseMessage> responder,
        HttpRequestException? throwOnSend)
    {
        var handler = new StaticHandler(responder, throwOnSend);
        var factory = new StubFactory(handler);
        return new VictoriaEndpointResolver(
            Options.Create(new ObservabilityOptions()),
            factory,
            NullLogger<VictoriaEndpointResolver>.Instance);
    }

    /// <summary>Static response handler — returns a fixed message or throws.</summary>
    private sealed class StaticHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responder,
        HttpRequestException? throwOnSend) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return throwOnSend is not null
                ? throw throwOnSend
                : Task.FromResult(responder(request));
        }
    }

    /// <summary>Recording handler — asserts the URL the resolver hit.</summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    /// <summary>HttpClient factory stub — every <c>name</c> maps to the supplied handler.</summary>
    private sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new(handler, disposeHandler: false);
        }
    }
}

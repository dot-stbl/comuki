using System.Net;
using Comuki.Modules.Observability.Application.Options;
using Comuki.Modules.Observability.Domain.Metrics;
using Comuki.Modules.Observability.Infrastructure.VictoriaMetrics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Refit;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Observability.Unit;

/// <summary>
/// Unit tests for <see cref="VictoriaMetricsQueryClient"/>: wire-format
/// conversion (<c>ToUnixSeconds</c>) + envelope unwrap behaviour
/// (status=error → typed <see cref="Domain.VictoriaUnavailableException"/>)
/// per the typed-client contract. The Refit surface is stubbed through
/// the public <c>internal</c> test-ctor.
/// </summary>
public sealed class VictoriaMetricsQueryClientShould
{
    /// <summary>status=error (with a typed message) surfaces as <see cref="Domain.VictoriaUnavailableException"/> — not an empty result list.</summary>
    [Fact(DisplayName = "Given an HTTP 200 response with status=error, when QueryAsync, then VictoriaUnavailableException is raised carrying the typed error message")]
    public async Task StatusErrorEnvelopeRaisesTypedExceptionAsync()
    {
        var api = Substitute.For<IVictoriaMetricsApi>();
        api.QueryAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(NewErrorEnvelope("parse error: unexpected token at position 4", "error"));

        var client = NewClient(api);

        var exception = await Should.ThrowAsync<Domain.VictoriaUnavailableException>(
            () => client.QueryAsync(new MetricsQuery(PromQl: "up", Time: DateTimeOffset.UtcNow), CancellationToken.None));

        exception.Endpoint.ShouldBe("victoria-metrics");
        exception.InnerException.ShouldNotBeNull();
        exception.InnerException.Message.ShouldContain("parse error: unexpected token at position 4");
    }

    /// <summary>An empty envelope (no <c>result</c>) is a real-but-empty result, NOT a failure.</summary>
    [Fact(DisplayName = "Given an HTTP 200 response with status=success and no rows, when QueryAsync, then an empty list is returned (no exception)")]
    public async Task EmptySuccessEnvelopeReturnsEmptyListAsync()
    {
        var api = Substitute.For<IVictoriaMetricsApi>();
        api.QueryAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(NewEmptySuccessEnvelope());

        var client = NewClient(api);

        var series = await client.QueryAsync(
            new MetricsQuery(PromQl: "no_such_metric", Time: DateTimeOffset.UtcNow),
            CancellationToken.None);

        series.ShouldBeEmpty();
    }

    /// <summary>A non-2xx HTTP response surfaces as <see cref="Domain.VictoriaUnavailableException"/> — the typed client wraps the transport-level <see cref="HttpRequestException"/> at the public-port boundary so MCP callers can branch on a single stable exception type.</summary>
    [Fact(DisplayName = "Given an HTTP 5xx response, when QueryAsync, then VictoriaUnavailableException is raised (transport-level wrapping)")]
    public async Task Http5xxRaisesTypedBoundaryExceptionAsync()
    {
        var api = Substitute.For<IVictoriaMetricsApi>();
        api.QueryAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(NewTransportErrorResponse(HttpStatusCode.InternalServerError));

        var client = NewClient(api);

        var exception = await Should.ThrowAsync<Domain.VictoriaUnavailableException>(
            () => client.QueryAsync(new MetricsQuery(PromQl: "up", Time: DateTimeOffset.UtcNow), CancellationToken.None));

        exception.Endpoint.ShouldBe("victoria-metrics");
    }

    /// <summary>ToUnixSeconds: null bound → null result (the Refit [Query] string? layer then omits the query-arg).</summary>
    [Fact(DisplayName = "Given a null DateTimeOffset, when ToUnixSeconds, then null is returned")]
    public void ToUnixSecondsReturnsNullForNullBound()
    {
        VictoriaMetricsQueryHelpers.ToUnixSeconds(null).ShouldBeNull();
    }

    /// <summary>ToUnixSeconds: a non-null bound → fractional-seconds string in the canonical Prometheus wire form.</summary>
    [Fact(DisplayName = "Given a non-null DateTimeOffset, when ToUnixSeconds, then a fractional unix-seconds string is returned")]
    public void ToUnixSecondsFormatsNonNullBound()
    {
        var instant = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_123_456);
        var formatted = VictoriaMetricsQueryHelpers.ToUnixSeconds(instant);
        formatted.ShouldNotBeNull();
        formatted.ShouldBe("1700000123.456");
    }

    private static VictoriaMetricsQueryClient NewClient(IVictoriaMetricsApi api)
    {
        var options = Options.Create(new ObservabilityOptions());
        return new VictoriaMetricsQueryClient(
            api,
            options,
            NullLogger<VictoriaMetricsQueryClient>.Instance);
    }

    private static IApiResponse<PrometheusResponseEnvelope<PrometheusValueWire>> NewErrorEnvelope(string error, string errorType)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/api/v1/query");
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = request,
        };
        var body = new PrometheusResponseEnvelope<PrometheusValueWire>(
            Status: "error",
            Data: null,
            Error: error,
            ErrorType: errorType);
        return new ApiResponse<PrometheusResponseEnvelope<PrometheusValueWire>>(request, response, body, settings: null!, error: null);
    }

    private static IApiResponse<PrometheusResponseEnvelope<PrometheusValueWire>> NewEmptySuccessEnvelope()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/api/v1/query");
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = request,
        };
        var body = new PrometheusResponseEnvelope<PrometheusValueWire>(
            Status: "success",
            Data: new PrometheusDataWire<PrometheusValueWire>(ResultType: "vector", Result: []));
        return new ApiResponse<PrometheusResponseEnvelope<PrometheusValueWire>>(request, response, body, settings: null!, error: null);
    }

    private static IApiResponse<PrometheusResponseEnvelope<PrometheusValueWire>> NewTransportErrorResponse(HttpStatusCode status)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/api/v1/query");
        var response = new HttpResponseMessage(status)
        {
            RequestMessage = request,
        };
        return new ApiResponse<PrometheusResponseEnvelope<PrometheusValueWire>>(
            request, response, /* content */ null!, settings: null!, error: null);
    }
}

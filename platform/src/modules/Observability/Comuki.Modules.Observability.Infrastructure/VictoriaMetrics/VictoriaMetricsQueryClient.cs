using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Comuki.Modules.Observability.Application.Options;
using Comuki.Modules.Observability.Application.Ports;
using Comuki.Modules.Observability.Domain;
using Comuki.Modules.Observability.Domain.Metrics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Refit;

namespace Comuki.Modules.Observability.Infrastructure.VictoriaMetrics;

/// <summary>
/// Refit-backed <see cref="IVictoriaMetricsQueryClient"/> implementation.
/// The Refit surface is registered by the composition root through
/// <c>AddRefitClient&lt;IVictoriaMetricsApi&gt;().ConfigureHttpClient(...).AddStandardResilienceHandler()</c>;
/// the typed client receives the Refit-generated proxy through DI. The
/// Prometheus HTTP API expects unix <em>seconds</em> for
/// <c>time</c>/<c>start</c>/<c>end</c> and Prometheus <c>duration</c>
/// strings for <c>step</c> — the typed client converts the typed
/// <see cref="DateTimeOffset"/> inputs to seconds, picks a default
/// <c>step</c> from <see cref="ObservabilityOptions.ScrapeInterval"/>
/// when absent, and threads the ambient OTel activity's
/// <see cref="Activity.TraceId"/> as a log-line trace annotation
/// (the metrics surface does not have a wire-level trace filter —
/// Prometheus has no equivalent of the LogsQL <c>trace_id:</c> clause;
/// the trace shows in the call-site logger only). Transport-layer
/// failures are translated to a typed
/// <see cref="VictoriaUnavailableException"/> at the public port
/// boundary so the MCP error-mapper can branch on the stable
/// <c>observability.victoria_unavailable</c> code.
/// </summary>
internal sealed class VictoriaMetricsQueryClient(
    IVictoriaMetricsApi api,
    IOptions<ObservabilityOptions> options,
    ILogger<VictoriaMetricsQueryClient> logger) : IVictoriaMetricsQueryClient
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<MetricSeries>> QueryAsync(
        MetricsQuery query,
        CancellationToken cancellationToken = default)
    {
        var explicitTraceId = VictoriaMetricsQueryHelpers.EffectiveTraceId(query.TraceId);
        logger.LogDebug(
            "executing PromQL query {PromQl} (trace {TraceId})",
            query.PromQl, explicitTraceId ?? "ambient");

        try
        {
            if (query.Time is not null)
            {
                var envelope = await api.QueryAsync(
                    query: query.PromQl,
                    time: VictoriaMetricsQueryHelpers.ToUnixSeconds(query.Time),
                    cancellationToken);

                return VictoriaMetricsQueryHelpers.MapValues(VictoriaMetricsQueryHelpers.UnwrapEnvelope(envelope));
            }

            var step = VictoriaMetricsQueryHelpers.ToPrometheusDuration(query.Step ?? options.Value.ScrapeInterval);
            var rangeEnvelope = await api.QueryRangeAsync(
                query: query.PromQl,
                start: VictoriaMetricsQueryHelpers.ToUnixSeconds(query.Start),
                end: VictoriaMetricsQueryHelpers.ToUnixSeconds(query.End),
                step: step,
                cancellationToken);

            return VictoriaMetricsQueryHelpers.MapValues(VictoriaMetricsQueryHelpers.UnwrapEnvelope(rangeEnvelope));
        }
        catch (ApiException exception)
        {
            throw new VictoriaUnavailableException("victoria-metrics", exception);
        }
        catch (HttpRequestException transport)
        {
            throw new VictoriaUnavailableException("victoria-metrics", transport);
        }
        catch (TaskCanceledException timeout) when (!cancellationToken.IsCancellationRequested)
        {
            throw new VictoriaUnavailableException("victoria-metrics", timeout);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> SeriesAsync(
        string labelSelector,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(labelSelector))
        {
            throw new ArgumentException("labelSelector is required", nameof(labelSelector));
        }

        try
        {
            var envelope = await api.SeriesAsync($"{{{labelSelector}}}", cancellationToken);
            var rows = VictoriaMetricsQueryHelpers.UnwrapEnvelope(envelope);
            return VictoriaMetricsQueryHelpers.AggregateLabelKeys(rows);
        }
        catch (ApiException exception)
        {
            throw new VictoriaUnavailableException("victoria-metrics", exception);
        }
        catch (HttpRequestException transport)
        {
            throw new VictoriaUnavailableException("victoria-metrics", transport);
        }
        catch (TaskCanceledException timeout) when (!cancellationToken.IsCancellationRequested)
        {
            throw new VictoriaUnavailableException("victoria-metrics", timeout);
        }
    }
}

/// <summary>
/// File-static helpers for the <see cref="VictoriaMetricsQueryClient"/>:
/// wire-format conversion + envelope unwrap live here per
/// <c>class-layout-and-tooling.md §1a</c> (no private methods on the
/// typed client). All pure functions; no I/O.
/// </summary>
internal static class VictoriaMetricsQueryHelpers
{
    /// <summary>
    /// Convert a <see cref="DateTimeOffset"/> to Prometheus' wire format
    /// (unix seconds, fixed-point). Prometheus expects a fractional
    /// seconds string per
    /// <see href="https://prometheus.io/docs/prometheus/latest/querying/api/#time-series-selectors"/>
    /// — the typed client produces the canonical
    /// <c>"&lt;seconds&gt;.&lt;fraction&gt;"</c> shape with invariant
    /// culture (the wire format is locale-independent). Returns
    /// <see langword="null"/> when the bound is absent so the Refit
    /// <c>[Query] string?</c> parameter omits the query-arg entirely
    /// (sending <c>string.Empty</c> produces <c>start=&amp;end=</c> on
    /// the wire, which is a malformed query, not a "no bound" signal).
    /// </summary>
    /// <param name="value">Bound to format; <see langword="null"/> when the caller did not supply a window.</param>
    public static string? ToUnixSeconds(DateTimeOffset? value)
    {
        return value is null
            ? null
            : (value.Value.ToUnixTimeMilliseconds() / 1000d).ToString("0.###", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Format a <see cref="TimeSpan"/> as a Prometheus duration string
    /// (the wire-accepted shorthand that avoids needing to send
    /// <c>step=15</c> as a unix-seconds float). The shape is the
    /// documented <c>"15s"</c>, <c>"5m"</c> form per the Prometheus
    /// parsing reference.
    /// </summary>
    public static string ToPrometheusDuration(TimeSpan step)
    {
        return step.TotalHours >= 1d
        ? $"{(int)step.TotalHours}h"
        : step.TotalMinutes >= 1d
            ? $"{(int)step.TotalMinutes}m"
            : step.TotalSeconds < 1d
                ? "1s"
                : $"{(int)step.TotalSeconds}s";
    }

    /// <summary>
    /// Pick the trace id the call threads onto the log line. Explicit
    /// override wins; otherwise the ambient OTel activity's
    /// <see cref="Activity.TraceId"/> is used. Returns <see langword="null"/>
    /// when neither is set — the Prometheus HTTP API has no
    /// trace-level filter, so the trace shows in the log only.
    /// </summary>
    public static string? EffectiveTraceId(string? explicitTraceId)
    {
        return explicitTraceId ?? Activity.Current?.TraceId.ToString();
    }

    /// <summary>
    /// Unwrap an <see cref="IApiResponse{T}"/> to its body or raise a
    /// transport-level exception. Prometheus endpoints respond with
    /// HTTP 200 even when <c>"status":"error"</c> — a real failure
    /// mode the wire contract documents (e.g. parse errors in the
    /// PromQL expression, an unknown series, an unreachable storage
    /// node) — and the typed client threads that case as
    /// <see cref="VictoriaUnavailableException"/> so the upstream
    /// caller can branch on the typed boundary the same way it
    /// branches on a transport-level timeout.
    /// </summary>
    public static TResponse UnwrapEnvelope<TResponse>(IApiResponse<TResponse> response)
    {
        // boundary: HttpResponseMessage.StatusCode is non-null on a constructed response
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"VictoriaMetrics returned HTTP {(int)response.StatusCode!.Value}.");
        }

        var body = response.Content
            ?? throw new HttpRequestException("VictoriaMetrics returned an empty response body.");

        // A 200 response with status="error" is a typed Prometheus failure —
        // surface it as a VictoriaUnavailableException so the MCP error
        // mapper turns it into the same `observability.victoria_unavailable`
        // ProblemDetails the transport-level failure already produces.
        if (body is PrometheusResponseEnvelope<PrometheusValueWire> envelope
            && !string.Equals(envelope.Status, "success", StringComparison.Ordinal))
        {
            var message = envelope.Error is null
                ? "VictoriaMetrics returned status=error with no error message."
                : $"VictoriaMetrics returned status=error: {envelope.Error}";
            throw new VictoriaUnavailableException("victoria-metrics", new HttpRequestException(message));
        }

        return body;
    }

    /// <summary>
    /// Map the Prometheus envelope onto typed <see cref="MetricSeries"/>
    /// rows. The wire shape is per-<see cref="PrometheusDataWire{TWire}.ResultType"/>:
    /// instant (vector) carries a single <c>[ts, v]</c> pair in
    /// <see cref="PrometheusValueWire.Value"/>; range (matrix) carries
    /// a list of pairs in <see cref="PrometheusValueWire.Values"/>. We
    /// pick the side that's present and parse samples off it.
    /// Timestamps on the wire are unix <em>seconds</em> (Prometheus convention).
    /// </summary>
    public static IReadOnlyList<MetricSeries> MapValues(PrometheusResponseEnvelope<PrometheusValueWire> envelope)
    {
        if (envelope.Data?.Result is null || envelope.Data.Result.Count == 0)
        {
            return [];
        }

        var result = new List<MetricSeries>(envelope.Data.Result.Count);
        foreach (var value in envelope.Data.Result)
        {
            var rawSamples = (IEnumerable<IReadOnlyList<object>>?)
                (value.Values
                 ?? (value.Value is null ? null : new[] { value.Value }));

            if (rawSamples is null)
            {
                continue;
            }

            var samples = new List<MetricSample>();
            foreach (var raw in rawSamples)
            {
                if (TryReadSample(raw, out var sample))
                {
                    samples.Add(sample);
                }
            }

            result.Add(new MetricSeries(value.Metric.Labels, samples));
        }

        return result;
    }

    /// <summary>
    /// Parse a single Prometheus <c>value</c> / <c>values</c> element.
    /// The wire shape carries each sample as a 2-element array of
    /// <c>[unixSec, value]</c> strings; we tolerate a one-element array
    /// (scalar) for instant queries. Timestamps are unix seconds.
    /// </summary>
    public static bool TryReadSample(object raw, out MetricSample sample)
    {
        sample = default!;

        if (raw is not JsonElement { ValueKind: JsonValueKind.Array } element)
        {
            return false;
        }

        if (element.GetArrayLength() < 2)
        {
            return false;
        }

        var unixElement = element[0];
        var valueElement = element[1];
        if (!unixElement.TryGetDouble(out var unixSeconds))
        {
            return false;
        }

        if (!valueElement.TryGetDouble(out var doubleValue))
        {
            return false;
        }

        var unixMs = (long)(unixSeconds * 1000d);
        sample = new MetricSample(unixMs, doubleValue);
        return true;
    }

    /// <summary>
    /// Aggregate the per-series label rows into a <c>label-key → distinct
    /// values</c> dictionary the dashboard observability page renders.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> AggregateLabelKeys(
        IReadOnlyList<SeriesMatchWire> rows)
    {
        if (rows is null || rows.Count == 0)
        {
            return new Dictionary<string, IReadOnlyList<string>>();
        }

        var bucket = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            foreach (var (key, value) in row.Labels)
            {
                if (!bucket.TryGetValue(key, out var values))
                {
                    values = new HashSet<string>(StringComparer.Ordinal);
                    bucket[key] = values;
                }
                values.Add(value);
            }
        }

        var snapshot = new Dictionary<string, IReadOnlyList<string>>(
            bucket.Count, StringComparer.Ordinal);
        foreach (var (key, values) in bucket)
        {
            var sorted = values.ToArray();
            Array.Sort(sorted, StringComparer.Ordinal);
            snapshot[key] = sorted;
        }
        return snapshot;
    }
}

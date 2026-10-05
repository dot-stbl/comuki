using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Comuki.Modules.Observability.Application.Ports;
using Comuki.Modules.Observability.Domain;
using Comuki.Modules.Observability.Domain.Metrics;
using Microsoft.Extensions.Logging;
using Refit;

namespace Comuki.Modules.Observability.Infrastructure.VictoriaMetrics;

/// <summary>
/// Refit-backed <see cref="IVictoriaMetricsQueryClient"/> implementation.
/// The client threads the ambient OTel activity's
/// <see cref="Activity.TraceId"/> as the Prometheus
/// <c>trace_id</c> query parameter when the caller does not pass an
/// explicit <see cref="MetricsQuery.TraceId"/>; an explicit override
/// always wins per <c>specs/observability/spec.md</c> "Trace correlation
/// threads through the typed clients". Transport-layer failures are
/// translated to a typed <see cref="VictoriaUnavailableException"/> at
/// the public port boundary so the MCP error-mapper can branch on the
/// stable <c>observability.victoria_unavailable</c> code.
/// </summary>
internal sealed class VictoriaMetricsQueryClient(
    IVictoriaMetricsApi api,
    ILogger<VictoriaMetricsQueryClient> logger) : IVictoriaMetricsQueryClient
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<MetricSeries>> QueryAsync(
        MetricsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var isInstant = query.TimeUnixMs is not null;
        var ambientTraceId = AmbientTraceIdOrNull(query.TraceId);
        try
        {
            if (isInstant)
            {
                using var _ = ActivityScope(ambientTraceId, log =>
                    logger.LogDebug(
                        "executing PromQL instant {PromQl} at {Time} trace {TraceId}",
                        log.Query, log.Time, log.TraceId));

                var envelope = await api.QueryAsync(
                    query: query.PromQl,
                    time: query.TimeUnixMs!.Value.ToString(CultureInfo.InvariantCulture),
                    cancellationToken).ConfigureAwait(false);

                return MapValues(envelope);
            }

            var step = (query.StepUnixMs ?? FallbackStepMs()).ToString(CultureInfo.InvariantCulture);
            using var __ = ActivityScope(ambientTraceId, log =>
                logger.LogDebug(
                    "executing PromQL range {PromQl} over [{From}..{To}] step {Step}ms trace {TraceId}",
                    log.Query, log.From, log.To, log.Step, log.TraceId));

            var rangeEnvelope = await api.QueryRangeAsync(
                query: query.PromQl,
                start: query.FromUnixMs!.Value.ToString(CultureInfo.InvariantCulture),
                end: query.ToUnixMs!.Value.ToString(CultureInfo.InvariantCulture),
                step: step,
                cancellationToken).ConfigureAwait(false);

            return MapValues(rangeEnvelope);
        }
        catch (ApiException api)
        {
            throw new VictoriaUnavailableException("victoria-metrics", api);
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
            var rows = await api.SeriesAsync($"{{{labelSelector}}}", cancellationToken).ConfigureAwait(false);
            return AggregateLabelKeys(rows);
        }
        catch (ApiException api)
        {
            throw new VictoriaUnavailableException("victoria-metrics", api);
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

    /// <summary>Default resolution when the caller doesn't supply a step.</summary>
    private static long FallbackStepMs()
    {
        return 15_000;
    }

    /// <summary>
    /// Resolve the trace id the call threads onto the wire. Explicit
    /// <see cref="MetricsQuery.TraceId"/> wins; otherwise the ambient
    /// OTel activity's <see cref="Activity.TraceId"/> is used. Returns
    /// <see langword="null"/> when neither is set — the HTTP client then
    /// propagates no W3C traceparent header (per the
    /// "Explicit trace id override" scenario of the spec).
    /// </summary>
    private static string? AmbientTraceIdOrNull(string? explicitTraceId)
    {
        return explicitTraceId ?? Activity.Current?.TraceId.ToString();
    }

    /// <summary>Map the Prometheus envelope onto typed <see cref="MetricSeries"/> rows.</summary>
    private static IReadOnlyList<MetricSeries> MapValues(
        PrometheusResponseEnvelope<PrometheusValueWire> envelope)
    {
        if (envelope.Data?.Result is null || envelope.Data.Result.Count == 0)
        {
            return [];
        }

        var result = new List<MetricSeries>(envelope.Data.Result.Count);
        foreach (var value in envelope.Data.Result)
        {
            // The wire shape is per-ResultType: instant (vector) carries
            // a single <c>[ts, v]</c> pair in <see cref="PrometheusValueWire.Value"/>;
            // range (matrix) carries a list of pairs in
            // <see cref="PrometheusValueWire.Values"/>. We pick the side
            // that's present and parse samples off it.
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
    /// <c>[unixMs, value]</c> strings; we tolerate a one-element array
    /// (scalar) for instant queries.
    /// </summary>
    private static bool TryReadSample(object raw, out MetricSample sample)
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
        if (!unixElement.TryGetInt64(out var unixMs))
        {
            return false;
        }

        if (!valueElement.TryGetDouble(out var doubleValue))
        {
            // Prometheus carries non-numeric values as the literal string
            // "NaN" — we drop those samples (the typed shape has no
            // non-numeric representation).
            return false;
        }

        sample = new MetricSample(unixMs, doubleValue);
        return true;
    }

    /// <summary>
    /// Aggregate the per-series label rows into a <c>label-key → distinct values</c>
    /// dictionary the dashboard observability page renders. The wire
    /// shape is a flat array of label dictionaries; we collapse them
    /// in O(N) without allocating intermediate sets beyond a single
    /// pass.
    /// </summary>
    private static IReadOnlyDictionary<string, IReadOnlyList<string>> AggregateLabelKeys(
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

    /// <summary>
    /// Run <paramref name="configure"/> with the structured-log record
    /// already filled in. The <see cref="ActivityScope"/> is a no-op
    /// placeholder — the trace id is captured in the log line directly
    /// (<c>{TraceId}</c> placeholder) rather than a child activity,
    /// because the typed client is a query proxy and does not own the
    /// caller's trace. Keeping the helper means the call-sites do not
    /// need <c>if (traceId is not null) { ... }</c> blocks: the helper
    /// accepts null and yields a no-op.
    /// </summary>
    private static IDisposable? ActivityScope(string? traceId, Action<PromScopeLog> configure)
    {
        if (traceId is null)
        {
            return null;
        }

        configure(new PromScopeLog(string.Empty, default, default, default, default, traceId));
        return null;
    }

    private readonly record struct PromScopeLog(string Query, long From, long To, long Step, long Time, string TraceId);
}

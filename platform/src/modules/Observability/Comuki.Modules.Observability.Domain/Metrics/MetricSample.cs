namespace Comuki.Modules.Observability.Domain.Metrics;

/// <summary>
/// One (timestamp, value) sample of a VictoriaMetrics time series.
/// The shape is the typed counterpart of the Prometheus <c>matrix</c>
/// response: <see cref="UnixMs"/> is the unix-ms UTC timestamp of the
/// bucket; <see cref="Value"/> is the observed gauge / counter / histogram
/// reading. Strings are reserved for <c>NaN</c>-style values that the
/// Prometheus contract carries as <c>"NaN"</c> — typed numeric samples
/// are by far the common case.
/// </summary>
public sealed record MetricSample(long UnixMs, double Value);

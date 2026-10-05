namespace Comuki.Modules.Observability.Domain.Metrics;

/// <summary>
/// One PromQL series: a label set plus its samples in time order. The
/// label dictionary is the input to <c>observability.metrics.series</c>
/// and the unit-of-rendering for the dashboard observability page. Keys
/// follow the Prometheus <c>label_name="value"</c> shape (lower-case,
/// snake-case); the dictionary is case-sensitive on read but the
/// upstream API normalises case at write time.
/// </summary>
public sealed record MetricSeries(
    IReadOnlyDictionary<string, string> Labels,
    IReadOnlyList<MetricSample> Samples);

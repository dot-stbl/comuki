using System.Text.Json;
using Comuki.AgentEval.Reporting;

namespace Comuki.AgentEval.History;

/// <summary>
/// Appends a single compact JSON line per eval run to a flat history
/// file (JSONL: one object per line, no pretty-print). The shape is
/// read back by <see cref="TrendReader"/>; this class never reads or
/// rewrites existing lines — append-only, idempotent against the
/// appender not being the only writer.
/// </summary>
public static class HistoryAppender
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    /// <summary>
    /// Appends <paramref name="report"/> as one JSONL line to
    /// <paramref name="historyPath"/>. Creates the parent directory
    /// if missing.
    /// </summary>
    public static async Task AppendAsync(string historyPath, EvalReport report, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);

        var directory = Path.GetDirectoryName(Path.GetFullPath(historyPath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var line = JsonSerializer.Serialize(ToHistoryEntry(report), JsonOptions);
        await File.AppendAllTextAsync(historyPath, line + Environment.NewLine, cancellationToken).ConfigureAwait(false);
    }

    private static HistoryEntry ToHistoryEntry(EvalReport report) =>
        new(
            report.StartedAt,
            report.Mode,
            report.CorpusDirectory,
            report.SummaryValue.Total,
            report.SummaryValue.Passed,
            report.SummaryValue.Failed,
            report.SummaryValue.Skipped,
            report.AverageQualityScore,
            report.CostValue.UsdMicros);
}

using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Comuki.AgentEval.History;

/// <summary>
/// One line of <see cref="HistoryAppender"/>'s flat history file.
/// </summary>
/// <remarks>
/// Init-only properties (no positional primary constructor) so
/// <c>System.Text.Json</c>'s default reflection-based deserializer
/// can materialize this type via its parameterless constructor.
/// </remarks>
public sealed class HistoryEntry
{
    /// <summary>UTC wall-clock start of the run.</summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>Execution mode — fake | replay | live.</summary>
    public required string Mode { get; init; }

    /// <summary>The corpus directory the run loaded entries from.</summary>
    public required string Corpus { get; init; }

    /// <summary>Total scenarios the run executed.</summary>
    public int Total { get; init; }

    /// <summary>Scenarios that passed every assertion.</summary>
    public int Passed { get; init; }

    /// <summary>Scenarios that failed at least one assertion.</summary>
    public int Failed { get; init; }

    /// <summary>Scenarios skipped (zero in WS10 today — the runner does not have a "skipped" code path).</summary>
    public int Skipped { get; init; }

    /// <summary>Mean of per-entry quality scores in [0, 1].</summary>
    public double AverageQualityScore { get; init; }

    /// <summary>Aggregate cost across the run in micro-USD.</summary>
    public long CostUsdMicros { get; init; }
}

/// <summary>
/// Reads <see cref="HistoryAppender"/>'s JSONL history file into
/// <see cref="HistoryEntry"/> rows, tolerating corrupted lines by
/// skipping them (a single bad line must not break reading the rest).
/// Renders the entries as a plain-text table — used by Program.cs's
/// <c>--trend</c> flag.
/// </summary>
public static class TrendReader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Reads <paramref name="historyPath"/> line-by-line, skipping lines that fail to parse.</summary>
    public static IReadOnlyList<HistoryEntry> ReadTrend(string historyPath)
    {
        if (!File.Exists(historyPath))
        {
            return [];
        }

        var entries = new List<HistoryEntry>();
        foreach (var line in File.ReadAllLines(historyPath))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            HistoryEntry? entry;
            try
            {
                entry = JsonSerializer.Deserialize<HistoryEntry>(line, JsonOptions);
            }
            catch (JsonException)
            {
                // Corrupted line — skip rather than fail the whole read.
                continue;
            }

            if (entry is not null)
            {
                entries.Add(entry);
            }
        }

        return entries;
    }

    /// <summary>Renders <paramref name="entries"/> as a plain-text table — column-aligned, monospace-friendly.</summary>
    public static string RenderTrendTable(IReadOnlyList<HistoryEntry> entries)
    {
        if (entries.Count == 0)
        {
            return "(no history entries yet)\n";
        }

        var headers = new[] { "timestamp", "mode", "corpus", "pass/total", "avg quality", "costUsd" };
        var rows = new List<string[]>(entries.Count + 1) { headers };
        foreach (var entry in entries)
        {
            rows.Add(
            [
                entry.Timestamp.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                entry.Mode,
                entry.Corpus,
                $"{entry.Passed}/{entry.Total}",
                entry.AverageQualityScore.ToString("0.000", CultureInfo.InvariantCulture),
                (entry.CostUsdMicros / 1_000_000.0).ToString("0.######", CultureInfo.InvariantCulture),
            ]);
        }

        var columnWidths = new int[headers.Length];
        for (var columnIndex = 0; columnIndex < headers.Length; columnIndex++)
        {
            var maxWidth = rows.Max(row => row[columnIndex].Length);
            columnWidths[columnIndex] = maxWidth;
        }

        var builder = new StringBuilder();
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            for (var columnIndex = 0; columnIndex < headers.Length; columnIndex++)
            {
                if (columnIndex > 0)
                {
                    builder.Append("  ");
                }

                builder.Append(rows[rowIndex][columnIndex].PadRight(columnWidths[columnIndex]));
            }
            builder.Append('\n');
        }

        return builder.ToString();
    }
}

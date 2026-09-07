using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Comuki.Engine.Orchestration.Unit.Eval;

/// <summary>
/// Renders an <see cref="EvalReport"/> to disk: a JSON machine-readable
/// payload (for diffing between runs) and a Markdown human-readable
/// summary (for the failure log). Both writers are pure: no I/O
/// outside the call site's <see cref="File"/> calls. Path constants
/// live on the writer so callers don't sprinkle magic strings.
/// </summary>
public static class EvalReportWriter
{
    /// <summary>Markdown heading prefix the report starts with.</summary>
    public const string MarkdownHeadingPrefix = "# ";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    /// <summary>Writes the JSON payload to <paramref name="path"/>.</summary>
    /// <param name="report">The report to serialize.</param>
    /// <param name="path">Destination file path.</param>
    public static void WriteJson(EvalReport report, string path)
    {
        var body = SerializeJson(report);
        File.WriteAllText(path, body);
    }

    /// <summary>Writes the Markdown report to <paramref name="path"/>.</summary>
    /// <param name="report">The report to render.</param>
    /// <param name="path">Destination file path.</param>
    public static void WriteMarkdown(EvalReport report, string path)
    {
        var body = RenderMarkdown(report);
        File.WriteAllText(path, body);
    }

    /// <summary>Renders the report to a Markdown string (no I/O).</summary>
    /// <param name="report">The report to render.</param>
    /// <returns>Markdown source.</returns>
    public static string RenderMarkdown(EvalReport report)
    {
        var builder = new StringBuilder();
        _ = builder
            .Append(MarkdownHeadingPrefix)
            .Append(report.Suite)
            .Append(" — ")
            .Append(report.RunAt.ToString("u", CultureInfo.InvariantCulture))
            .AppendLine()
            .AppendLine()
            .Append("**Pass:** ")
            .Append(report.PassedCount)
            .Append(" / ")
            .Append(report.Results.Count)
            .Append("    **Fail:** ")
            .Append(report.FailedCount)
            .AppendLine()
            .AppendLine()
            .AppendLine("| id | name | kind | duration_ms | result |")
            .AppendLine("|---|---|---|---|---|");

        foreach (var result in report.Results)
        {
            _ = builder
                .Append("| ")
                .Append(result.Task.Id)
                .Append(" | ")
                .Append(result.Task.Name)
                .Append(" | ")
                .Append(result.Task.Kind)
                .Append(" | ")
                .Append(result.DurationMs)
                .Append(" | ")
                .Append(result.Passed ? "PASS" : "FAIL")
                .AppendLine(" |");
        }

        _ = builder.AppendLine();

        foreach (var result in report.Results.Where(static r => !r.Passed))
        {
            _ = builder
                .Append("## ")
                .Append(result.Task.Id)
                .Append(" — ")
                .Append(result.Task.Name)
                .AppendLine()
                .AppendLine()
                .Append("- kind: `")
                .Append(result.Task.Kind)
                .AppendLine("`")
                .Append("- expected: ")
                .Append(SummarizeExpected(result.Task))
                .AppendLine()
                .Append("- actual:   ")
                .Append(string.Join(" -> ", result.ActualTransitionLog))
                .AppendLine();

            foreach (var mismatch in result.Mismatches)
            {
                _ = builder
                    .Append("- mismatch `")
                    .Append(mismatch.Field)
                    .Append("`: ")
                    .Append(mismatch.Expected)
                    .Append("  !=  ")
                    .Append(mismatch.Actual)
                    .AppendLine();
            }

            _ = builder.AppendLine();
        }

        return builder.ToString();
    }

    /// <summary>Serializes the report to JSON (no I/O).</summary>
    /// <param name="report">The report to serialize.</param>
    /// <returns>Pretty-printed JSON.</returns>
    public static string SerializeJson(EvalReport report)
    {
        var payload = new EvalReportPayload(
            report.Suite,
            report.RunAt,
            report.PassedCount,
            report.FailedCount,
            report.Results.Select(ToPayload).ToArray());

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static string SummarizeExpected(EvalTask task)
    {
        return task.Expected.ExpectsFailure
            ? $"throws containing '{task.Expected.ExpectedFailureMessage}'"
            : $"final=`{task.Expected.FinalStatus}`, log=`{string.Join(" -> ", task.Expected.TransitionLog)}`";
    }

    private static EvalTaskResultPayload ToPayload(EvalTaskResult result)
    {
        return new EvalTaskResultPayload(
            result.Task.Id,
            result.Task.Name,
            result.Task.Kind.ToString(),
            result.Passed,
            result.DurationMs,
            result.ActualTransitionLog,
            result.Mismatches.Select(static mismatch => new EvalMismatchPayload(mismatch.Field, mismatch.Expected, mismatch.Actual)).ToArray());
    }

    private sealed record EvalReportPayload(
        string Suite,
        DateTimeOffset RunAt,
        int PassedCount,
        int FailedCount,
        IReadOnlyList<EvalTaskResultPayload> Results);

    private sealed record EvalTaskResultPayload(
        string Id,
        string Name,
        string Kind,
        bool Passed,
        long DurationMs,
        IReadOnlyList<string> ActualTransitionLog,
        IReadOnlyList<EvalMismatchPayload> Mismatches);

    private sealed record EvalMismatchPayload(string Field, string Expected, string Actual);
}

using System.Globalization;
using System.Text;
using System.Text.Json;
using Comuki.Engine.Orchestration.Unit.Eval.Eval;

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

    /// <summary>Writes the JSON payload to <paramref name="path"/>.</summary>
    /// <param name="report">The report to serialize.</param>
    /// <param name="path">Destination file path.</param>
    public static void WriteJson(EvalReport report, string path)
    {
        File.WriteAllText(path, SerializeJson(report));
    }

    /// <summary>Writes the Markdown report to <paramref name="path"/>.</summary>
    /// <param name="report">The report to render.</param>
    /// <param name="path">Destination file path.</param>
    public static void WriteMarkdown(EvalReport report, string path)
    {
        File.WriteAllText(path, RenderMarkdown(report));
    }

    /// <summary>Renders the report to a Markdown string (no I/O).</summary>
    /// <param name="report">The report to render.</param>
    /// <returns>Markdown source.</returns>
    public static string RenderMarkdown(EvalReport report)
    {
        var builder = new StringBuilder();
        AppendHeading(builder, report);
        AppendSummary(builder, report);
        AppendTable(builder, report.Results);
        AppendFailureDrilldown(builder, report.Results);
        return builder.ToString();
    }

    private static void AppendHeading(StringBuilder builder, EvalReport report)
    {
        builder.Append(MarkdownHeadingPrefix);
        builder.Append(report.Suite);
        builder.Append(" — ");
        builder.Append(report.RunAt.ToString("u", CultureInfo.InvariantCulture));
        builder.AppendLine();
        builder.AppendLine();
    }

    private static void AppendSummary(StringBuilder builder, EvalReport report)
    {
        builder.Append("**Pass:** ");
        builder.Append(report.PassedCount);
        builder.Append(" / ");
        builder.Append(report.Results.Count);
        builder.Append("    **Fail:** ");
        builder.Append(report.FailedCount);
        builder.AppendLine();
        builder.AppendLine();
    }

    private static void AppendTable(StringBuilder builder, IReadOnlyList<EvalTaskResult> results)
    {
        builder.AppendLine("| id | name | kind | duration_ms | result |");
        builder.AppendLine("|---|---|---|---|---|");
        foreach (var result in results)
        {
            builder.Append("| ");
            builder.Append(result.Task.Id);
            builder.Append(" | ");
            builder.Append(result.Task.Name);
            builder.Append(" | ");
            builder.Append(result.Task.Kind);
            builder.Append(" | ");
            builder.Append(result.DurationMs);
            builder.Append(" | ");
            builder.Append(result.Passed ? "PASS" : "FAIL");
            builder.AppendLine(" |");
        }

        builder.AppendLine();
    }

    private static void AppendFailureDrilldown(StringBuilder builder, IReadOnlyList<EvalTaskResult> results)
    {
        foreach (var result in results.Where(static r => !r.Passed))
        {
            builder.Append("## ");
            builder.Append(result.Task.Id);
            builder.Append(" — ");
            builder.Append(result.Task.Name);
            builder.AppendLine();
            builder.AppendLine();
            builder.Append("- kind: `");
            builder.Append(result.Task.Kind);
            builder.AppendLine("`");
            builder.Append("- expected: ");
            builder.Append(SummarizeExpected(result.Task));
            builder.AppendLine();
            builder.Append("- actual:   ");
            builder.Append(string.Join(" -> ", result.ActualTransitionLog));
            builder.AppendLine();

            foreach (var mismatch in result.Mismatches)
            {
                builder.Append("- mismatch `");
                builder.Append(mismatch.Field);
                builder.Append("`: ");
                builder.Append(mismatch.Expected);
                builder.Append("  !=  ");
                builder.Append(mismatch.Actual);
                builder.AppendLine();
            }

            builder.AppendLine();
        }
    }

    /// <summary>Serializes the report to JSON (no I/O).</summary>
    /// <param name="report">The report to serialize.</param>
    /// <returns>Pretty-printed JSON.</returns>
    public static string SerializeJson(EvalReport report)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
        };
        var payload = new EvalReportPayload(
            report.Suite,
            report.RunAt,
            report.PassedCount,
            report.FailedCount,
            [.. report.Results.Select(ToPayload)]);

        return JsonSerializer.Serialize(payload, options);
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
            [.. result.Mismatches.Select(static mismatch => new EvalMismatchPayload(mismatch.Field, mismatch.Expected, mismatch.Actual))]);
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

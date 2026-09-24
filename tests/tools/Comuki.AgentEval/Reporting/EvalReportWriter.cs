using System.Globalization;
using System.Text;
using System.Text.Json;
using Comuki.AgentEval.Judges;
using Comuki.AgentTest.Runner.Reporting;

namespace Comuki.AgentEval.Reporting;

/// <summary>
/// Writes an <see cref="EvalReport"/> to <c>basePath.json</c> and
/// <c>basePath.md</c> from the same object (no drift between the
/// JSON and the markdown). Mirrors <see cref="ReportWriter"/>'s
/// table style and adds the WS10-specific per-entry section that
/// lists each deterministic verdict (name, PASS/FAIL/n-a, message)
/// and, when a judge ran, the per-criterion scores and overall score
/// against the rubric's <c>MinScore</c>.
/// </summary>
public static class EvalReportWriter
{
    private static readonly JsonSerializerOptions jsonOptions = new(JsonSerializerOptions.Web)
    {
        WriteIndented = true,
    };

    /// <summary>
    /// Writes both report files next to <paramref name="basePath"/>
    /// (a path with no extension — <c>.json</c>/<c>.md</c> are
    /// appended). Returns the one-line stdout verdict
    /// (<c>PASS x/y</c> / <c>FAIL x/y — see &lt;path&gt;</c>) — same
    /// shape <see cref="ReportWriter.Verdict"/> produces, with the
    /// test count being the number of corpus entries (not the runner's
    /// per-tier scenario count).
    /// </summary>
    public static async Task<string> WriteAsync(EvalReport report, string basePath, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(basePath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var jsonPath = basePath + ".json";
        var markdownPath = basePath + ".md";

        await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(report, jsonOptions), cancellationToken);
        await File.WriteAllTextAsync(markdownPath, RenderMarkdown(report, Path.GetFileName(markdownPath)), cancellationToken);

        return Verdict(report, markdownPath);
    }

    /// <summary>The one-line stdout verdict for a report, without writing any file.</summary>
    public static string Verdict(EvalReport report, string markdownPath)
    {
        var summary = report.SummaryValue;
        return summary.Failed == 0
            ? string.Format(CultureInfo.InvariantCulture, "PASS {0}/{1}", summary.Passed, summary.Total)
            : string.Format(
                CultureInfo.InvariantCulture,
                "FAIL {0}/{1} — see {2}",
                summary.Failed,
                summary.Total,
                markdownPath);
    }

    private static string RenderMarkdown(EvalReport report, string fileName)
    {
        var summary = report.SummaryValue;
        var cost = report.CostValue;
        var entries = report.EntriesValue;

        var builder = new StringBuilder();
        builder.Append("# Comuki.AgentEval report — ").Append(fileName).Append('\n').Append('\n');
        builder.Append("Mode: `").Append(report.Mode).Append("` · Corpus: `").Append(report.CorpusDirectory)
            .Append("` · Started: ").Append(report.StartedAt.ToString("O", CultureInfo.InvariantCulture))
            .Append(" · Duration: ").Append(report.DurationMs).Append("ms\n\n");

        builder.Append("| total | passed | failed | avg quality |\n");
        builder.Append("|---|---|---|---|\n");
        builder.Append('|').Append(summary.Total)
            .Append('|').Append(summary.Passed)
            .Append('|').Append(summary.Failed)
            .Append("|").Append(report.AverageQualityScore.ToString("0.000", CultureInfo.InvariantCulture))
            .Append("|\n\n");

        if (cost.UsdMicros > 0 || cost.TokensIn > 0 || cost.TokensOut > 0)
        {
            builder.Append("Cost: ")
                .Append((cost.UsdMicros / 1_000_000.0).ToString("0.000000", CultureInfo.InvariantCulture))
                .Append(" USD (").Append(cost.TokensIn).Append(" in / ")
                .Append(cost.TokensOut).Append(" out tokens)\n\n");
        }

        if (entries.Count == 0)
        {
            builder.Append("No entries.\n");
            return builder.ToString();
        }

        builder.Append("## Entries\n\n");
        foreach (var entry in entries)
        {
            AppendEntry(builder, entry);
        }

        return builder.ToString();
    }

    private static void AppendEntry(StringBuilder builder, EvalEntryResult entry)
    {
        var verdictText = entry.Passed ? "PASS" : "FAIL";
        builder.Append("### `").Append(entry.ScenarioName).Append("` — ").Append(verdictText)
            .Append(" · difficulty=").Append(string.IsNullOrEmpty(entry.Difficulty) ? "?" : entry.Difficulty)
            .Append(" · duration=").Append(entry.DurationMs).Append("ms")
            .Append(" · quality=").Append(entry.Score.QualityScore.ToString("0.000", CultureInfo.InvariantCulture))
            .Append('\n');

        if (entry.Score.Deterministic.Count > 0)
        {
            builder.Append('\n');
            builder.Append("| judge | result | message |\n|---|---|---|\n");
            foreach (var verdict in entry.Score.Deterministic)
            {
                var result = verdict.Passed switch
                {
                    true => "PASS",
                    false => "FAIL",
                    null => "n-a",
                };
                builder.Append('|').Append(verdict.JudgeName)
                    .Append('|').Append(result)
                    .Append('|').Append(verdict.Message.Replace('|', '/'))
                    .Append("|\n");
            }
        }

        if (entry.Score.Judge.Kind == JudgeOutcomeKind.Scored && entry.Score.Judge.Verdict is { } verdictResult)
        {
            builder.Append('\n');
            builder.Append("LLM-as-judge (scored):\n\n");
            var rubricMin = "n/a";
            builder.Append("- verdict: ").Append(string.IsNullOrEmpty(verdictResult.Verdict) ? "?" : verdictResult.Verdict).Append('\n');
            builder.Append("- overall: ").Append(verdictResult.OverallScore.ToString("0.000", CultureInfo.InvariantCulture)).Append('\n');
            if (!string.IsNullOrEmpty(verdictResult.Notes))
            {
                builder.Append("- notes: ").Append(verdictResult.Notes).Append('\n');
            }

            foreach (var score in verdictResult.Scores)
            {
                builder.Append("- score[")
                    .Append(score.CriterionId)
                    .Append("] = ")
                    .Append(score.Score.ToString("0.000", CultureInfo.InvariantCulture))
                    .Append(" — ")
                    .Append(score.Rationale.Replace('\n', ' '))
                    .Append('\n');
            }

            // The rubric's MinScore is read off the corresponding entry's
            // EvalExtension by callers — the entry itself is what the
            // judge compared against. We surface it through the per-entry
            // section by re-reading the entry's rubric via the report's
            // caller-supplied data; EvalEntryResult does not carry the
            // rubric itself (only EvalScore.Judge.Verdict carries the
            // verdict and computed OverallScore), so we don't have
            // MinScore here. The overall-vs-pass verdict line at the top
            // already conveys whether the rubric passed.
            _ = rubricMin;
        }
        else if (entry.Score.Judge.Kind == JudgeOutcomeKind.Error)
        {
            builder.Append("\nLLM-as-judge: **error** — ").Append(entry.Score.Judge.Message ?? "(no message)").Append('\n');
        }
        else if (entry.Score.Judge.Kind == JudgeOutcomeKind.Skipped && entry.Score.Judge.Message is { } skippedReason)
        {
            builder.Append("\nLLM-as-judge: skipped — ").Append(skippedReason);
            if (HasRubric(entry))
            {
                builder.Append(" (rubric declared but judge skipped — quality not fully checked)");
            }
            builder.Append('\n');
        }

        builder.Append('\n');
    }

    private static bool HasRubric(EvalEntryResult entry)
    {
        // EvalEntryResult deliberately does not carry the rubric itself,
        // only the verdict; we cannot distinguish a rubric-declared-but-
        // skipped from a no-rubric-skipped from the entry shape. Surface
        // a generic message and let the EvalRun caller note the rubric
        // case if it cares. Kept here as a hook for future entry-shape
        // changes that surface the rubric.
        return false;
    }

    /// <summary>The one-line stdout verdict for an entry — exposed so callers can print incremental progress.</summary>
    public static string EntryVerdict(EvalEntryResult entry)
    {
        return entry.Passed
            ? string.Format(CultureInfo.InvariantCulture, "PASS {0}", entry.ScenarioName)
            : string.Format(CultureInfo.InvariantCulture, "FAIL {0}", entry.ScenarioName);
    }
}

using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Comuki.AgentTest.Runner.Reporting;

/// <summary>
/// Writes a <see cref="RunReport"/> to <c>&lt;path&gt;.json</c> and
/// <c>&lt;path&gt;.md</c> — both rendered from the same object (design.md:
/// "no drift between what the JSON says and what the markdown says").
/// </summary>
public static class ReportWriter
{
    private static readonly JsonSerializerOptions jsonOptions = new(JsonSerializerOptions.Web)
    {
        WriteIndented = true,
    };

    /// <summary>
    /// Writes both report files next to <paramref name="basePath"/> (a path
    /// with no extension — <c>.json</c>/<c>.md</c> are appended) and returns
    /// the one-line stdout verdict (design.md: <c>PASS 11/12</c> /
    /// <c>FAIL 1/12 — see report.md</c>).
    /// </summary>
    /// <param name="report"></param>
    /// <param name="basePath"></param>
    /// <param name="cancellationToken"></param>
    public static async Task<string> WriteAsync(RunReport report, string basePath, CancellationToken cancellationToken = default)
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
    /// <param name="report"></param>
    /// <param name="markdownPath">Path shown in a failing verdict's "see &lt;path&gt;" pointer.</param>
    public static string Verdict(RunReport report, string markdownPath)
    {
        return report.Summary.Failed == 0
            ? string.Format(CultureInfo.InvariantCulture, "PASS {0}/{1}", report.Summary.Passed, report.Summary.Total)
            : string.Format(
                CultureInfo.InvariantCulture,
                "FAIL {0}/{1} — see {2}",
                report.Summary.Failed,
                report.Summary.Total,
                markdownPath);
    }

    private static string RenderMarkdown(RunReport report, string fileName)
    {
        var builder = new StringBuilder();
        builder.Append("# ").Append(report.Tier).Append(" report — ").Append(fileName).Append('\n').Append('\n');
        builder.Append("Mode: `").Append(report.Mode).Append("` · Started: ")
            .Append(report.StartedAt.ToString("O", CultureInfo.InvariantCulture))
            .Append(" · Duration: ").Append(report.DurationMs).Append("ms\n\n");

        builder.Append("| total | passed | failed | skipped |\n");
        builder.Append("|---|---|---|---|\n");
        builder.Append('|').Append(report.Summary.Total)
            .Append('|').Append(report.Summary.Passed)
            .Append('|').Append(report.Summary.Failed)
            .Append('|').Append(report.Summary.Skipped)
            .Append("|\n\n");

        if (report.Cost.UsdMicros > 0 || report.Cost.TokensIn > 0 || report.Cost.TokensOut > 0)
        {
            builder.Append("Cost: ")
                .Append((report.Cost.UsdMicros / 1_000_000.0).ToString("0.000000", CultureInfo.InvariantCulture))
                .Append(" USD (").Append(report.Cost.TokensIn).Append(" in / ")
                .Append(report.Cost.TokensOut).Append(" out tokens)\n\n");
        }

        if (report.Failures.Count == 0)
        {
            builder.Append("No failures.\n");
            return builder.ToString();
        }

        builder.Append("## First failure\n\n");
        var first = report.Failures[0];
        builder.Append("- Scenario: `").Append(first.Scenario).Append("`\n");
        builder.Append("- Stage: `").Append(first.Stage).Append("`\n");
        builder.Append("- Message: ").Append(first.Message).Append('\n');
        if (first.ArtifactPaths.Count > 0)
        {
            builder.Append("- Artifacts:\n");
            foreach (var artifactPath in first.ArtifactPaths)
            {
                builder.Append("  - `").Append(artifactPath).Append("`\n");
            }
        }

        if (report.Failures.Count > 1)
        {
            builder.Append("\n## All failures\n\n");
            builder.Append("| scenario | stage | message |\n|---|---|---|\n");
            foreach (var failure in report.Failures)
            {
                builder.Append('|').Append(failure.Scenario)
                    .Append('|').Append(failure.Stage)
                    .Append('|').Append(failure.Message.Replace('|', '/'))
                    .Append("|\n");
            }
        }

        return builder.ToString();
    }
}

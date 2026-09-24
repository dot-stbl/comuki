using System.Text.Json;
using Comuki.AgentEval.History;
using Comuki.AgentEval.Reporting;
using Comuki.AgentTest.Runner.Reporting.Report;
using Shouldly;
using Xunit;

namespace Comuki.AgentEval.Unit;

/// <summary>
/// Proves <see cref="HistoryAppender.AppendAsync"/> writes one compact
/// JSONL line per call, never overwrites earlier lines, and each line
/// round-trips through <c>JsonDocument.Parse(string)</c> back to the
/// exact same data the report carried.
/// </summary>
public sealed class HistoryAppenderShould
{
    [Fact(DisplayName = "Given two sequential appends to the same temp file, when the file is read, then it contains exactly two independently parseable JSON lines")]
    public async Task TwoSequentialAppendsProduceTwoIndependentlyParseableLinesAsync()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"comuki-agent-eval-history-{Guid.NewGuid():N}.jsonl");
        try
        {
            var firstReport = MakeReport("first", "fake", total: 3, passed: 3, failed: 0);
            var secondReport = MakeReport("second", "fake", total: 5, passed: 4, failed: 1);

            await HistoryAppender.AppendAsync(tempPath, firstReport, TestContext.Current.CancellationToken);
            await HistoryAppender.AppendAsync(tempPath, secondReport, TestContext.Current.CancellationToken);

            var lines = File.ReadAllLines(tempPath);
            lines.Length.ShouldBe(2);
            var first = JsonDocument.Parse(lines[0]).RootElement;
            var second = JsonDocument.Parse(lines[1]).RootElement;

            first.GetProperty("timestamp").GetDateTimeOffset().ShouldBe(firstReport.StartedAt);
            first.GetProperty("mode").GetString().ShouldBe("fake");
            first.GetProperty("corpus").GetString().ShouldBe("first");
            first.GetProperty("total").GetInt32().ShouldBe(3);
            first.GetProperty("passed").GetInt32().ShouldBe(3);
            first.GetProperty("failed").GetInt32().ShouldBe(0);

            second.GetProperty("timestamp").GetDateTimeOffset().ShouldBe(secondReport.StartedAt);
            second.GetProperty("corpus").GetString().ShouldBe("second");
            second.GetProperty("total").GetInt32().ShouldBe(5);
            second.GetProperty("passed").GetInt32().ShouldBe(4);
            second.GetProperty("failed").GetInt32().ShouldBe(1);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    [Fact(DisplayName = "Given a first line was appended before any second append, when the second append happens, then the first line's content is byte-for-byte unchanged")]
    public async Task SecondAppendDoesNotMutateFirstLineAsync()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"comuki-agent-eval-history-{Guid.NewGuid():N}.jsonl");
        try
        {
            var firstReport = MakeReport("first", "fake", total: 2, passed: 2, failed: 0);
            var secondReport = MakeReport("second", "replay", total: 4, passed: 3, failed: 1);

            await HistoryAppender.AppendAsync(tempPath, firstReport, TestContext.Current.CancellationToken);
            var firstLineBefore = File.ReadAllLines(tempPath)[0];

            await HistoryAppender.AppendAsync(tempPath, secondReport, TestContext.Current.CancellationToken);
            var lines = File.ReadAllLines(tempPath);

            lines[0].ShouldBe(firstLineBefore);
            lines.Length.ShouldBe(2);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    [Fact(DisplayName = "Given a path whose parent directory does not exist, when append runs, then it creates the parent directory and the line is still parseable")]
    public async Task CreatesParentDirectoryWhenMissingAsync()
    {
        var nestedPath = Path.Combine(
            Path.GetTempPath(),
            $"comuki-agent-eval-history-{Guid.NewGuid():N}",
            "subdir",
            "history.jsonl");
        try
        {
            var report = MakeReport("nested", "fake", total: 1, passed: 1, failed: 0);

            await HistoryAppender.AppendAsync(nestedPath, report, TestContext.Current.CancellationToken);

            File.Exists(nestedPath).ShouldBeTrue();
            var line = File.ReadAllLines(nestedPath)[0];
            var doc = JsonDocument.Parse(line);
            doc.RootElement.GetProperty("corpus").GetString().ShouldBe("nested");
        }
        finally
        {
            var parent = Path.GetDirectoryName(nestedPath)!;
            var grandparent = Path.GetDirectoryName(parent)!;
            if (Directory.Exists(grandparent))
            {
                Directory.Delete(grandparent, recursive: true);
            }
        }
    }

    private static EvalReport MakeReport(string corpus, string mode, int total, int passed, int failed)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var summary = new RunSummary
        {
            Total = total,
            Passed = passed,
            Failed = failed,
            Skipped = 0,
        };
        return new EvalReport
        {
            SchemaVersion = 1,
            Mode = mode,
            CorpusDirectory = corpus,
            StartedAt = startedAt,
            DurationMs = 1000L,
            Summary = summary,
            AverageQualityScore = passed / (double)Math.Max(total, 1),
            Cost = new RunCost(),
            Entries = [],
        };
    }
}

using Shouldly;
using Xunit;

namespace Comuki.Engine.Orchestration.Unit.Eval;

/// <summary>
/// Unit tests for <see cref="EvalReportWriter"/>: JSON serialization
/// shape and Markdown rendering contract.
/// </summary>
public sealed class EvalReportWriterShould
{
    private static EvalReport BuildReport()
    {
        var passingTask = new EvalTask(
            Id: "01-pass",
            Name: "Pass task",
            Kind: EvalTaskKind.Run,
            Operations: [new EvalOperation(EvalAction.Create, string.Empty)],
            Expected: new EvalExpected(FinalStatus: "Queued", TransitionLog: ["Queued"]));

        var failingTask = new EvalTask(
            Id: "02-fail",
            Name: "Fail task",
            Kind: EvalTaskKind.Run,
            Operations: [new EvalOperation(EvalAction.Create, string.Empty)],
            Expected: new EvalExpected(FinalStatus: "Succeeded", TransitionLog: ["Queued", "Succeeded"]));

        var results = new[]
        {
            new EvalTaskResult(passingTask, Passed: true, ActualTransitionLog: ["Queued"], Mismatches: [], DurationMs: 5),
            new EvalTaskResult(
                failingTask,
                Passed: false,
                ActualTransitionLog: ["Queued"],
                Mismatches: [new EvalMismatch("final-status", "Succeeded", "Queued")],
                DurationMs: 7),
        };

        return new EvalReport(
            Suite: "engine.orchestration.status-machines",
            RunAt: new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
            Results: results);
    }

    [Fact(DisplayName = "Given a report with one pass and one fail, when serialized, then it carries the suite + counters + per-task payload")]
    public void SerializeJsonShape()
    {
        var report = BuildReport();

        var json = EvalReportWriter.SerializeJson(report);

        json.ShouldContain("\"suite\":");
        json.ShouldContain("engine.orchestration.status-machines");
        json.ShouldContain("\"passed_count\": 1");
        json.ShouldContain("\"failed_count\": 1");
        json.ShouldContain("01-pass");
        json.ShouldContain("02-fail");
        json.ShouldContain("\"passed\": true");
        json.ShouldContain("\"passed\": false");
        json.ShouldContain("\"final-status\"");
    }

    [Fact(DisplayName = "Given a report, when rendered to markdown, then it shows a header + pass/fail counters + per-task table + per-failure drilldown")]
    public void RenderMarkdownShape()
    {
        var report = BuildReport();

        var markdown = EvalReportWriter.RenderMarkdown(report);

        markdown.ShouldStartWith("# engine.orchestration.status-machines");
        markdown.ShouldContain("**Pass:** 1 / 2");
        markdown.ShouldContain("**Fail:** 1");
        markdown.ShouldContain("| 01-pass |");
        markdown.ShouldContain("| 02-fail |");
        markdown.ShouldContain("## 02-fail — Fail task");
        markdown.ShouldContain("final-status");
        markdown.ShouldContain("Succeeded");
        markdown.ShouldContain("Queued");
    }

    [Fact(DisplayName = "Given a fully-passing report, when rendered to markdown, then there are no failure sections")]
    public void MarkdownHasNoFailureSectionsWhenAllPass()
    {
        var passingTask = new EvalTask(
            Id: "all-pass",
            Name: "All pass",
            Kind: EvalTaskKind.Run,
            Operations: [new EvalOperation(EvalAction.Create, string.Empty)],
            Expected: new EvalExpected(FinalStatus: "Queued", TransitionLog: ["Queued"]));

        var report = new EvalReport(
            Suite: "suite",
            RunAt: new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
            Results: [new EvalTaskResult(passingTask, Passed: true, ActualTransitionLog: ["Queued"], Mismatches: [], DurationMs: 1)]);

        var markdown = EvalReportWriter.RenderMarkdown(report);

        markdown.ShouldContain("**Pass:** 1 / 1");
        markdown.ShouldNotContain("## ");
        markdown.ShouldNotContain("mismatch");
    }
}

using System.Diagnostics;
using Comuki.AgentTest.Runner.Execution.Support;
using Comuki.AgentTest.Runner.Journal;
using Comuki.AgentTest.Runner.Reporting;
using Comuki.AgentTest.Runner.Scenarios;
using Comuki.Shared.Contracts.Compute;
using Comuki.Shared.Contracts.Journal;

namespace Comuki.AgentTest.Runner.Execution;

/// <summary>
/// Drives one <see cref="ScenarioDefinition"/> through an
/// <see cref="IAgentLoopHarness"/>: seed the ticket, provision the worker,
/// wait for a terminal status, then check every declared assertion —
/// journal conditions and the final run/work-item status for T2a (WS6);
/// <see cref="ScenarioDefinition.ExpectedTrajectory"/>/<see cref="ScenarioAssertions.Diff"/>/
/// <see cref="ScenarioAssertions.Cost"/>/<see cref="ScenarioAssertions.Judge"/>
/// are parsed and carried but not yet evaluated — WS7 (trajectory/diff),
/// WS8/WS9 (cost/budget) and WS10 (judge) extend this same class rather
/// than rewriting it, per the WS6 brief's "mode seams ready without
/// implementing them."
/// </summary>
/// <param name="harness">The concrete seam to the real orchestrator/compute for this tier.</param>
public sealed class ScenarioRunner(IAgentLoopHarness harness)
{
    /// <summary>
    /// Runs <paramref name="scenario"/> to completion (or until
    /// <see cref="ScenarioRunOptions.Timeout"/> elapses) and returns its
    /// outcome. Never throws for an execution-path failure (a bad image
    /// label, a claim that never lands, a failed assertion) — those all
    /// become a <see cref="ScenarioResult.Passed"/> false result with a
    /// named <see cref="ScenarioResult.FailedStage"/> and
    /// <see cref="ScenarioResult.FailureMessage"/>. A genuinely
    /// unrecoverable harness fault (e.g. the orchestrator host itself is
    /// down) still throws — that is a suite-setup problem, not a scenario
    /// failure the report format is meant to carry.
    /// </summary>
    /// <param name="scenario"></param>
    /// <param name="options"></param>
    /// <param name="cancellationToken"></param>
    public async Task<ScenarioResult> RunAsync(
        ScenarioDefinition scenario,
        ScenarioRunOptions options,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        WorkerHandle? handle = null;

        try
        {
            var seeded = await harness.SeedTicketAsync(scenario, cancellationToken);

            try
            {
                handle = await harness.StartWorkerAsync(scenario, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return ScenarioResult.Failure(scenario.Name, "compute.start", FirstLine(exception.Message), stopwatch.Elapsed, []);
            }

            var timeline = await harness.ReadTimelineAsync(seeded.RunId, cancellationToken);
            var status = await harness.ReadWorkItemStatusAsync(seeded.WorkItemId, cancellationToken);

            var reachedTerminal = await WaitForAsync.PollAsync(
                async () =>
                {
                    timeline = await harness.ReadTimelineAsync(seeded.RunId, cancellationToken);
                    status = await harness.ReadWorkItemStatusAsync(seeded.WorkItemId, cancellationToken);
                    return IsTerminal(status);
                },
                options.Timeout,
                options.PollInterval,
                cancellationToken);

            var artifactPaths = await DumpTimelineAsync(scenario.Name, timeline, options, cancellationToken);

            if (!reachedTerminal)
            {
                return ScenarioResult.Failure(
                    scenario.Name,
                    "run.status",
                    $"work item did not reach a terminal status within {options.Timeout}; last observed status: '{status}'. "
                        + "This usually means the container's Translator never claimed the queued item — check that "
                        + "worker.image/profileKey/profilesRef in the scenario match Intake:Worker:* on the host.",
                    stopwatch.Elapsed,
                    artifactPaths);
            }

            foreach (var journalAssertion in scenario.Assertions.Journal)
            {
                bool actual;
                try
                {
                    actual = JournalConditionEvaluator.Evaluate(journalAssertion.Condition, timeline);
                }
                catch (ScenarioValidationException exception)
                {
                    return ScenarioResult.Failure(scenario.Name, "assertions.journal", exception.Message, stopwatch.Elapsed, artifactPaths);
                }

                if (actual != journalAssertion.Expected)
                {
                    return ScenarioResult.Failure(
                        scenario.Name,
                        "assertions.journal",
                        $"condition '{journalAssertion.Condition}' evaluated to {actual}, expected {journalAssertion.Expected}",
                        stopwatch.Elapsed,
                        artifactPaths);
                }
            }

            return scenario.Assertions.Run is { } runAssertion
                && !string.Equals(status, runAssertion.FinalStatus, StringComparison.OrdinalIgnoreCase)
                ? ScenarioResult.Failure(
                    scenario.Name,
                    "assertions.run",
                    $"final status was '{status}', expected '{runAssertion.FinalStatus}'",
                    stopwatch.Elapsed,
                    artifactPaths)
                : ScenarioResult.Success(scenario.Name, stopwatch.Elapsed, artifactPaths);
        }
        finally
        {
            if (handle is not null)
            {
                await harness.StopWorkerAsync(handle, CancellationToken.None);
            }
        }
    }

    private static async Task<IReadOnlyList<string>> DumpTimelineAsync(
        string scenarioName,
        IReadOnlyList<RunEventEntry> timeline,
        ScenarioRunOptions options,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.ArtifactsDirectory))
        {
            return [];
        }

        var written = await TimelineArtifactWriter.WriteAsync(options.ArtifactsDirectory, scenarioName, timeline, cancellationToken);
        return [written];
    }

    private static bool IsTerminal(string status)
    {
        return string.Equals(status, "Succeeded", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "Failed", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Docker API exceptions carry a JSON body on later lines — the first line alone is the clean, human message a report should show.</summary>
    private static string FirstLine(string message)
    {
        var newlineIndex = message.IndexOf('\n');
        return newlineIndex < 0 ? message : message[..newlineIndex];
    }
}

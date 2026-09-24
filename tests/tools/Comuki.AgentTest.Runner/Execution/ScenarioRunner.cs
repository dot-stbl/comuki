using System.Diagnostics;
using Comuki.AgentTest.Runner.Execution.Budget;
using Comuki.AgentTest.Runner.Execution.Support;
using Comuki.AgentTest.Runner.Journal;
using Comuki.AgentTest.Runner.Reporting;
using Comuki.AgentTest.Runner.Reporting.Report;
using Comuki.AgentTest.Runner.Scenarios;
using Comuki.Shared.Contracts.Compute;
using Comuki.Shared.Contracts.Journal;

namespace Comuki.AgentTest.Runner.Execution;

/// <summary>
/// Drives one <see cref="ScenarioDefinition"/> through an
/// <see cref="IAgentLoopHarness"/>: seed the ticket, provision the worker,
/// wait for a terminal status, then check every declared assertion —
/// journal conditions and the final run/work-item status (T2a, WS6), plus
/// <see cref="ScenarioDefinition.ExpectedTrajectory"/> and
/// <see cref="ScenarioAssertions.Diff"/> (T2b, WS7 tasks 7.2/7.3),
/// <see cref="ScenarioAssertions.Cost"/> (WS8) and the
/// <see cref="ScenarioDefinition.Budget"/> live-mode cap (WS9).
/// <see cref="ScenarioAssertions.Judge"/> is still parsed and carried but
/// not yet evaluated — WS10 extends this same class rather than
/// rewriting it, per the WS6 brief's "mode seams ready without
/// implementing them."
/// </summary>
/// <param name="harness">The concrete seam to the real orchestrator/compute for this tier.</param>
public sealed class ScenarioRunner(IAgentLoopHarness harness)
{
    /// <summary>
    /// Name of the env var this runner reads for the process-wide live-mode
    /// ceiling (<see cref="BudgetCap.Resolve"/>). Settled on
    /// <c>COMUKI_LIVE_BUDGET_MAX_USD</c> in WS9 so live-eval.mjs, record-cassette.mjs,
    /// and an explicit integration test all consume the same variable.
    /// </summary>
    public const string GlobalBudgetEnvVar = "COMUKI_LIVE_BUDGET_MAX_USD";
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
        RunCost cost = new();

        try
        {
            var seeded = await harness.SeedTicketAsync(scenario, cancellationToken);

            try
            {
                handle = await harness.StartWorkerAsync(scenario, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return ScenarioResult.Failure(scenario.Name, "compute.start", FirstLine(exception.Message), stopwatch.Elapsed, [], cost);
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

            cost = await harness.ReadCostAsync(seeded.WorkItemId, cancellationToken);

            if (scenario.Budget is { } scenarioBudget)
            {
                var cap = BudgetCap.Resolve(scenarioBudget.MaxUsd, GlobalBudgetEnvVar);
                if (cap.IsOverBudget(cost.UsdMicros))
                {
                    return ScenarioResult.Failure(
                        scenario.Name,
                        "budget",
                        $"scenario exceeded its live-mode budget: spent {cost.UsdMicros} micro-USD (cap was {(cap.UsdMicros is { } m ? m : 0L)} micro-USD across scenario.budget.maxUsd={scenarioBudget.MaxUsd:0.######} and {GlobalBudgetEnvVar}).",
                        stopwatch.Elapsed,
                        artifactPaths,
                        cost);
                }
            }

            if (!reachedTerminal)
            {
                return ScenarioResult.Failure(
                    scenario.Name,
                    "run.status",
                    $"work item did not reach a terminal status within {options.Timeout}; last observed status: '{status}'. "
                        + "This usually means the container's Translator never claimed the queued item — check that "
                        + "worker.image/profileKey/profilesRef in the scenario match Intake:Worker:* on the host.",
                    stopwatch.Elapsed,
                    artifactPaths,
                    cost);
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
                    return ScenarioResult.Failure(scenario.Name, "assertions.journal", exception.Message, stopwatch.Elapsed, artifactPaths, cost);
                }

                if (actual != journalAssertion.Expected)
                {
                    return ScenarioResult.Failure(
                        scenario.Name,
                        "assertions.journal",
                        $"condition '{journalAssertion.Condition}' evaluated to {actual}, expected {journalAssertion.Expected}",
                        stopwatch.Elapsed,
                        artifactPaths,
                        cost);
                }
            }

            if (TrajectoryAssertionEvaluator.Evaluate(scenario.ExpectedTrajectory, timeline) is { } trajectoryFailure)
            {
                return ScenarioResult.Failure(scenario.Name, "expectedTrajectory", trajectoryFailure, stopwatch.Elapsed, artifactPaths, cost);
            }

            if (scenario.Assertions.Diff is { } diffAssertion)
            {
                var workingDirectory = await harness.ResolveWorkingDirectoryAsync(seeded.WorkItemId, cancellationToken);
                if (workingDirectory is not null
                    && await DiffAssertionEvaluator.EvaluateAsync(diffAssertion, workingDirectory, cancellationToken) is { } diffFailure)
                {
                    return ScenarioResult.Failure(scenario.Name, "assertions.diff", diffFailure, stopwatch.Elapsed, artifactPaths, cost);
                }
            }

            if (scenario.Assertions.Cost is { MaxUsdMicros: { } ceilingMicros })
            {
                if (cost.UsdMicros > ceilingMicros)
                {
                    return ScenarioResult.Failure(
                        scenario.Name,
                        "assertions.cost",
                        $"run spent {cost.UsdMicros} micro-USD which exceeds the assertions.cost.maxUsdMicros ceiling of {ceilingMicros}.",
                        stopwatch.Elapsed,
                        artifactPaths,
                        cost);
                }
            }

            return scenario.Assertions.Run is { } runAssertion
                && !string.Equals(status, runAssertion.FinalStatus, StringComparison.OrdinalIgnoreCase)
                ? ScenarioResult.Failure(
                    scenario.Name,
                    "assertions.run",
                    $"final status was '{status}', expected '{runAssertion.FinalStatus}'",
                    stopwatch.Elapsed,
                    artifactPaths,
                    cost)
                : ScenarioResult.Success(scenario.Name, stopwatch.Elapsed, artifactPaths, cost);
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

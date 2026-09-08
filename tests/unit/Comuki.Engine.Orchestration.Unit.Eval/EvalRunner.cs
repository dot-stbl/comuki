using System.Diagnostics;
using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Domain.Runs;
using Comuki.Engine.Orchestration.Domain.WorkItems;
using Comuki.Engine.Orchestration.Unit.Eval.Eval;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Engine.Orchestration.Unit.Eval;

/// <summary>
/// Applies a single golden task against the engine orchestrator state
/// machines and scores the captured outcome against
/// <see cref="EvalTask.Expected"/>. The runner is intentionally pure:
/// no DB, no clock injection, no DI. Time is supplied by the caller via
/// the <paramref name="now"/> parameter so tests stay deterministic.
/// </summary>
public static class EvalRunner
{
    /// <summary>
    /// Runs <paramref name="evalTask"/> end-to-end and returns a fully
    /// populated <see cref="EvalTaskResult"/>. Wall-clock duration is
    /// measured with <see cref="Stopwatch"/>; per-task work is in
    /// microseconds, so the reported value is a coarse sanity number,
    /// not a perf benchmark.
    /// </summary>
    /// <param name="evalTask">The golden task to drive.</param>
    /// <param name="now">Clock used for every aggregate factory + transition.</param>
    /// <returns>Result — captured timeline + mismatches vs expected.</returns>
    public static EvalTaskResult Run(EvalTask evalTask, DateTimeOffset now)
    {
        var stopwatch = Stopwatch.StartNew();
        var mismatches = new List<EvalMismatch>();
        var log = new List<string>();

        try
        {
            Apply(evalTask, now, log, mismatches);
        }
        catch (Exception exception)
        {
            mismatches.Add(new EvalMismatch("runner.exception", string.Empty, $"{exception.GetType().Name}: {exception.Message}"));
        }

        stopwatch.Stop();

        var passed = EvaluateOutcome(evalTask, log, mismatches);
        return new EvalTaskResult(evalTask, passed, log, mismatches, stopwatch.ElapsedMilliseconds);
    }

    private static void Apply(EvalTask evalTask, DateTimeOffset now, List<string> log, List<EvalMismatch> mismatches)
    {
        switch (evalTask.Kind)
        {
            case EvalTaskKind.Run:
                ApplyRun(evalTask, now, log);
                break;
            case EvalTaskKind.WorkItem:
                ApplyWorkItem(evalTask, now, log, mismatches);
                break;
            default:
                mismatches.Add(new EvalMismatch("kind", "Run | WorkItem", evalTask.Kind.ToString()));
                break;
        }
    }

    private static void ApplyRun(EvalTask evalTask, DateTimeOffset now, List<string> log)
    {
        Run? run = null;
        foreach (var op in evalTask.Operations)
        {
            switch (op.Action)
            {
                case EvalAction.Create:
                    run = Domain.Runs.Run.Create(ProjectId.New(), now);
                    log.Add(run.Status.ToString());
                    break;
                case EvalAction.Transition:
                case EvalAction.TransitionExpectFailure:
                    if (run is null)
                    {
                        return;
                    }

                    run.TransitionTo(ParseRunStatus(op.Status), now);
                    log.Add(run.Status.ToString());
                    break;
                default:
                    log.Add($"# unknown op {op.Action} for run kind");
                    break;
            }
        }
    }

    private static void ApplyWorkItem(EvalTask evalTask, DateTimeOffset now, List<string> log, List<EvalMismatch> mismatches)
    {
        WorkItem? item = null;
        var runId = RunId.New();
        foreach (var op in evalTask.Operations)
        {
            switch (op.Action)
            {
                case EvalAction.Create:
                    var initial = ParseWorkItemStatus(op.Status, mismatches);
                    item = WorkItem.Create(
                        runId,
                        profileKey: "implement",
                        image: "ghcr.io/test/worker:latest",
                        profilesRef: "abc1234",
                        brief: /*lang=json,strict*/ "{\"goal\":\"eval\"}",
                        initialStatus: initial,
                        now: now);
                    log.Add(item.Status.ToString());
                    break;
                case EvalAction.AssignLease:
                    if (item is null)
                    {
                        return;
                    }

                    item.AssignLease(WorkerId.New(), now.AddMinutes(5), now);
                    log.Add(item.Status.ToString());
                    break;
                case EvalAction.Heartbeat:
                    if (item is null)
                    {
                        return;
                    }

                    item.Heartbeat(now.AddMinutes(5), now);
                    log.Add(item.Status.ToString());
                    break;
                case EvalAction.ReleaseLease:
                    if (item is null)
                    {
                        return;
                    }

                    item.ReleaseLease(now);
                    log.Add(item.Status.ToString());
                    break;
                case EvalAction.Transition:
                case EvalAction.TransitionExpectFailure:
                    if (item is null)
                    {
                        return;
                    }

                    item.TransitionTo(ParseWorkItemStatus(op.Status, mismatches), now);
                    log.Add(item.Status.ToString());
                    break;
                default:
                    log.Add($"# unknown op {op.Action} for work-item kind");
                    break;
            }
        }
    }

    private static bool EvaluateOutcome(EvalTask evalTask, IReadOnlyList<string> actual, List<EvalMismatch> mismatches)
    {
        if (evalTask.Expected.ExpectsFailure)
        {
            var failMismatch = mismatches.FirstOrDefault(static mismatch => mismatch.Field == "runner.exception");
            if (failMismatch is null)
            {
                mismatches.Add(new EvalMismatch("expects-failure", evalTask.Expected.ExpectedFailureMessage, "no exception thrown"));
                return false;
            }

            if (!string.IsNullOrEmpty(evalTask.Expected.ExpectedFailureMessage)
                && !failMismatch.Actual.Contains(evalTask.Expected.ExpectedFailureMessage, StringComparison.OrdinalIgnoreCase))
            {
                mismatches.Add(new EvalMismatch("failure-message", evalTask.Expected.ExpectedFailureMessage, failMismatch.Actual));
                return false;
            }

            return true;
        }

        if (!string.IsNullOrEmpty(evalTask.Expected.FinalStatus))
        {
            var last = actual.Count > 0 ? actual[^1] : string.Empty;
            if (!string.Equals(last, evalTask.Expected.FinalStatus, StringComparison.OrdinalIgnoreCase))
            {
                mismatches.Add(new EvalMismatch("final-status", evalTask.Expected.FinalStatus, last));
            }
        }

        if (evalTask.Expected.TransitionLog.Count > 0)
        {
            var expected = string.Join(",", evalTask.Expected.TransitionLog);
            var actualLog = string.Join(",", actual);
            if (!string.Equals(expected, actualLog, StringComparison.OrdinalIgnoreCase))
            {
                mismatches.Add(new EvalMismatch("transition-log", expected, actualLog));
            }
        }

        return mismatches.Count == 0;
    }

    private static RunStatus ParseRunStatus(string value)
    {
        return Enum.TryParse<RunStatus>(value, ignoreCase: true, out var status)
            ? status
            : throw new ArgumentException($"unknown run status '{value}'", nameof(value));
    }

    private static WorkItemStatus ParseWorkItemStatus(string value, List<EvalMismatch> mismatches)
    {
        if (!Enum.TryParse<WorkItemStatus>(value, ignoreCase: true, out var status))
        {
            mismatches.Add(new EvalMismatch("op.status", "Queued | Blocked | Running | Succeeded | Failed | Cancelled", value));
            return WorkItemStatus.Queued;
        }

        return status;
    }
}

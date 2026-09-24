using Comuki.AgentTest.Runner.Journal;
using Comuki.AgentTest.Runner.Scenarios;
using Comuki.Shared.Contracts.Journal;

namespace Comuki.AgentTest.Runner.Execution;

/// <summary>
/// Evaluates a scenario's <c>expectedTrajectory[].toolsUsed</c>/
/// <c>forbiddenTools</c> against the tools actually observed on the run's
/// timeline (WS7 task 7.2 — "the first test anywhere that observes
/// agents/comuki-worker-sdk lock/skill/MCP enforcement in situ").
/// Simplification, documented rather than hidden: the journal carries one
/// flat tool-call stream per work item, not per-stage boundaries, so every
/// declared stage's assertions are checked against the whole run's observed
/// tool set — a scenario with more than one meaningfully distinct stage
/// needs a journal-level stage marker this evaluator does not yet have;
/// today's scenarios (<c>add-null-check</c>) declare exactly one stage
/// (<c>work-item</c>), so this is not yet a real gap.
/// </summary>
public static class TrajectoryAssertionEvaluator
{
    /// <summary>Returns a failure message, or null when every stage's assertions hold.</summary>
    public static string? Evaluate(IReadOnlyList<ExpectedTrajectoryStage> stages, IReadOnlyList<RunEventEntry> timeline)
    {
        if (stages.Count == 0)
        {
            return null;
        }

        var observed = ObservedToolCalls.Extract(timeline);
        foreach (var stage in stages)
        {
            var missing = stage.ToolsUsed.Where(tool => !observed.Contains(tool)).ToList();
            if (missing.Count > 0)
            {
                return $"stage '{stage.Stage}': expected tool(s) [{string.Join(", ", missing)}] never appeared on the "
                    + $"timeline (observed: [{string.Join(", ", observed)}])";
            }

            var forbidden = stage.ForbiddenTools.Where(observed.Contains).ToList();
            if (forbidden.Count > 0)
            {
                return $"stage '{stage.Stage}': forbidden tool(s) [{string.Join(", ", forbidden)}] were observed on the "
                    + "timeline — worker-sdk lock/allowlist enforcement did not hold";
            }
        }

        return null;
    }
}

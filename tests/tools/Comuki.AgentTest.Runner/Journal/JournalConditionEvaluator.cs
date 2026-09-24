using System.Globalization;
using Comuki.Engine.Orchestration.Domain.Journal;
using Comuki.Shared.Contracts.Journal;

namespace Comuki.AgentTest.Runner.Journal;

/// <summary>
/// Evaluates a scenario's <c>assertions.journal[].condition</c> strings
/// against a run's real timeline (<see cref="RunEventEntry"/> rows read
/// through <see cref="IRunJournal.ReadTimelineAsync"/>). design.md's
/// worked example names <c>WorkspacePrepared</c>/<c>AgentRunning</c> as
/// illustrative condition strings — this evaluator gives both a concrete,
/// T2a-grounded meaning (see the two <c>const</c> fields below) rather than
/// inventing journal event types the platform does not emit. The vocabulary
/// is intentionally small and open for extension as later workstreams
/// (T2b/WS7 forbidden-tool trajectories, T4/WS10 judge scores) need more.
/// </summary>
public static class JournalConditionEvaluator
{
    /// <summary>
    /// True once the queue claim transitioned the work item Queued -&gt;
    /// Running (<see cref="RunEventTypes.WorkItemStatusChanged"/> with a
    /// "to": "Running" payload) — the earliest observable proof the
    /// container's Translator reached the orchestrator and started the
    /// item, i.e. its workspace is prepared enough to run pi/TestFakePi.
    /// </summary>
    public const string WorkspacePrepared = "WorkspacePrepared";

    /// <summary>
    /// True once at least one <see cref="RunEventTypes.WorkerReported"/>
    /// entry appears — proof TestFakePi (or a real pi, T2b+) actually
    /// streamed activity through the gRPC bidi stream into the journal.
    /// </summary>
    public const string AgentRunning = "AgentRunning";

    private static readonly IReadOnlyDictionary<string, Func<IReadOnlyList<RunEventEntry>, bool>> conditions =
        new Dictionary<string, Func<IReadOnlyList<RunEventEntry>, bool>>(StringComparer.OrdinalIgnoreCase)
        {
            [WorkspacePrepared] = static timeline => timeline.Any(static entry =>
                entry.Type == RunEventTypes.WorkItemStatusChanged
                    && entry.PayloadJson.Contains("\"Running\"", StringComparison.Ordinal)),
            [AgentRunning] = static timeline => timeline.Any(static entry =>
                entry.Type == RunEventTypes.WorkerReported),
        };

    /// <summary>The condition names this evaluator understands, for error messages.</summary>
    public static IReadOnlyCollection<string> KnownConditions => (IReadOnlyCollection<string>)conditions.Keys;

    /// <summary>
    /// Evaluates <paramref name="condition"/> against <paramref name="timeline"/>.
    /// </summary>
    /// <param name="condition">One of <see cref="KnownConditions"/> (case-insensitive).</param>
    /// <param name="timeline">The run's timeline, oldest first.</param>
    /// <exception cref="Scenarios.ScenarioValidationException">The condition name is not recognized.</exception>
    public static bool Evaluate(string condition, IReadOnlyList<RunEventEntry> timeline)
    {
        return conditions.TryGetValue(condition, out var predicate)
            ? predicate(timeline)
            : throw new Scenarios.ScenarioValidationException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "unknown journal condition '{0}'; known conditions: {1}",
                    condition,
                    string.Join(", ", KnownConditions)));
    }
}

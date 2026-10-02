namespace Comuki.Modules.Procedures.Application.Materialization;

/// <summary>The outcome of handling a late result.</summary>
public enum LateResultOutcome
{
    /// <summary>The result arrived in time and is authoritative.</summary>
    Accepted,

    /// <summary>The result arrived after the pinned version was superseded — recorded as non-authoritative evidence.</summary>
    NonAuthoritativeEvidence,
}

/// <summary>
/// The record of a late result — retained for replay and audit even when
/// it does not change the run outcome (spec: "a late completion after
/// generation fencing changes no outcome and is retained as evidence").
/// </summary>
/// <param name="NodeId">The graph node that produced the result.</param>
/// <param name="PinnedVersionId">The version the run pinned at admission.</param>
/// <param name="AttemptGeneration">The generation the result belongs to.</param>
/// <param name="Outcome">Whether the result was accepted or recorded as evidence.</param>
/// <param name="ArrivedAt">When the result arrived (UTC).</param>
public sealed record LateResultRecord(
    string NodeId,
    string PinnedVersionId,
    string AttemptGeneration,
    LateResultOutcome Outcome,
    DateTimeOffset ArrivedAt);

/// <summary>
/// Handles results that arrive after the pinned version was superseded
/// or after generation fencing moved on. A late result never changes the
/// run outcome — it is recorded as non-authoritative evidence for the
/// replay trace. Pure; the host wires persistence.
/// </summary>
public static class LateResultHandler
{
    /// <summary>
    /// Classifies a result as authoritative or non-authoritative based on
    /// whether the node is still in the current plan and the generation matches.
    /// </summary>
    /// <param name="nodeId">The node that produced the result.</param>
    /// <param name="pinnedVersionId">The version the run pinned at admission.</param>
    /// <param name="currentVersionId">The version the plan is currently on.</param>
    /// <param name="attemptGeneration">The generation the result belongs to.</param>
    /// <param name="currentGeneration">The generation the run is currently in.</param>
    /// <param name="clock">Time source for the ArrivedAt timestamp.</param>
    /// <returns>The classification record.</returns>
    public static LateResultRecord Handle(
        string nodeId,
        string pinnedVersionId,
        string currentVersionId,
        string attemptGeneration,
        string currentGeneration,
        TimeProvider clock)
    {
        var versionMatches = string.Equals(pinnedVersionId, currentVersionId, StringComparison.Ordinal);
        var generationMatches = string.Equals(attemptGeneration, currentGeneration, StringComparison.Ordinal);
        var outcome = versionMatches && generationMatches
            ? LateResultOutcome.Accepted
            : LateResultOutcome.NonAuthoritativeEvidence;

        return new LateResultRecord(nodeId, pinnedVersionId, attemptGeneration, outcome, clock.GetUtcNow());
    }
}

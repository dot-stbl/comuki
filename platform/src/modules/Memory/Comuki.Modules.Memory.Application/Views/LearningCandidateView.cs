using Comuki.Modules.Memory.Domain.Learning;

namespace Comuki.Modules.Memory.Application.Views;

/// <summary>
/// Read-model of one learning candidate for the approvals API and the
/// learning.suggest acknowledgement. Status is the wire key of
/// <see cref="LearningStatus"/> (pending / approved / rejected).
/// </summary>
/// <param name="Id">Candidate id.</param>
/// <param name="ProjectId">The project whose workers suggested the rule.</param>
/// <param name="Topic">Short topic key (e.g. <c>build.dotnet</c>).</param>
/// <param name="Observation">What the worker observed — the evidence.</param>
/// <param name="ProposedRule">The candidate rule text, stated as an imperative.</param>
/// <param name="SourceRef">Where the signal came from (e.g. <c>worker:{id}</c>).</param>
/// <param name="RepeatCount">How many times the same signal repeated.</param>
/// <param name="Status">Wire key of the review state.</param>
/// <param name="DecisionReason">Why the human rejected it; null unless rejected.</param>
/// <param name="CreatedAt">When the candidate was first seen.</param>
/// <param name="DecidedAt">When the human decided; null while pending.</param>
public sealed record LearningCandidateView(
    Guid Id,
    Guid ProjectId,
    string Topic,
    string Observation,
    string ProposedRule,
    string SourceRef,
    int RepeatCount,
    string Status,
    string? DecisionReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DecidedAt)
{
    /// <summary>Maps the domain entity.</summary>
    /// <param name="candidate"></param>
    /// <returns></returns>
    public static LearningCandidateView Of(LearningCandidate candidate)
    {
        return new LearningCandidateView(
            candidate.Id.Value,
            candidate.ProjectId,
            candidate.Topic,
            candidate.Observation,
            candidate.ProposedRule,
            candidate.SourceRef,
            candidate.RepeatCount,
            LearningStatusKeys.Key(candidate.Status),
            candidate.DecisionReason,
            candidate.CreatedAt,
            candidate.DecidedAt);
    }
}

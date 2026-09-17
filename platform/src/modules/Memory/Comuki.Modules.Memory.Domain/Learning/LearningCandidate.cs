using Comuki.Modules.Memory.Domain.Ids;

namespace Comuki.Modules.Memory.Domain.Learning;

/// <summary>
/// One queued rule candidate from the project learning loop: a worker's
/// <c>learning.suggest</c> proposal (a build gotcha, an architectural
/// constraint, a recurring pattern), a repeated verify failure, a reject
/// reason. The row counts repeats and waits for a human decision —
/// approving promotes the rule through the learning-rule publisher (v1: a
/// standing memory fact with source <c>learning-approved</c>; v2: a PR into
/// the client's git, never a silent write into memory facts).
/// </summary>
public sealed class LearningCandidate
{
    internal LearningCandidate()
    {
    }

    /// <summary>Candidate id (UUIDv7, client-side).</summary>
    public LearningCandidateId Id { get; private set; }

    /// <summary>The project whose workers suggested the rule — the candidate's scope.</summary>
    public Guid ProjectId { get; private set; }

    /// <summary>Short topic key the rule belongs to (e.g. <c>build.dotnet</c>).</summary>
    public string Topic { get; private set; } = string.Empty;

    /// <summary>What the worker observed — the evidence behind the proposal.</summary>
    public string Observation { get; private set; } = string.Empty;

    /// <summary>The candidate rule text, stated as an imperative — what a human reviews.</summary>
    public string ProposedRule { get; private set; } = string.Empty;

    /// <summary>Where the signal came from (e.g. <c>worker:{id}</c>, a PR comment ref).</summary>
    public string SourceRef { get; private set; } = string.Empty;

    /// <summary>How many times the signal repeated; each sighting increments.</summary>
    public int RepeatCount { get; private set; }

    /// <summary>Review state: pending until a human decides.</summary>
    public LearningStatus Status { get; private set; }

    /// <summary>Why the human rejected it; null unless rejected (and optional then).</summary>
    public string? DecisionReason { get; private set; }

    /// <summary>When the candidate was first seen.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>When the human decided; null while pending.</summary>
    public DateTimeOffset? DecidedAt { get; private set; }

    /// <summary>Creates a pending candidate with the first sighting counted.</summary>
    /// <param name="projectId">The owning project — the candidate's scope.</param>
    /// <param name="topic"></param>
    /// <param name="observation"></param>
    /// <param name="proposedRule"></param>
    /// <param name="sourceRef"></param>
    /// <param name="now"></param>
    /// <exception cref="ArgumentException"></exception>
    public static LearningCandidate Create(
        Guid projectId,
        string topic,
        string observation,
        string proposedRule,
        string sourceRef,
        DateTimeOffset now)
    {
        return projectId == Guid.Empty
            ? throw new ArgumentException("project id must not be empty", nameof(projectId))
            : string.IsNullOrWhiteSpace(topic)
            ? throw new ArgumentException("topic must not be empty", nameof(topic))
            : string.IsNullOrWhiteSpace(observation)
            ? throw new ArgumentException("observation must not be empty", nameof(observation))
            : string.IsNullOrWhiteSpace(proposedRule)
            ? throw new ArgumentException("proposed rule must not be empty", nameof(proposedRule))
            : string.IsNullOrWhiteSpace(sourceRef)
            ? throw new ArgumentException("source ref must not be empty", nameof(sourceRef))
            : new LearningCandidate
            {
                Id = LearningCandidateId.New(),
                ProjectId = projectId,
                Topic = topic.Trim(),
                Observation = observation.Trim(),
                ProposedRule = proposedRule.Trim(),
                SourceRef = sourceRef.Trim(),
                RepeatCount = 1,
                Status = LearningStatus.Pending,
                DecisionReason = null,
                CreatedAt = now,
                DecidedAt = null,
            };
    }

    /// <summary>Records one more sighting of the same signal.</summary>
    public void RegisterRepeat()
    {
        RepeatCount++;
    }

    /// <summary>Marks the candidate approved; deciding twice is refused.</summary>
    /// <param name="now"></param>
    /// <exception cref="InvalidOperationException">The candidate is already decided.</exception>
    public void Approve(DateTimeOffset now)
    {
        if (Status != LearningStatus.Pending)
        {
            throw new InvalidOperationException($"learning candidate {Id} is already {Status}");
        }

        Status = LearningStatus.Approved;
        DecidedAt = now;
    }

    /// <summary>Marks the candidate rejected with an optional human reason; deciding twice is refused.</summary>
    /// <param name="now"></param>
    /// <param name="reason"></param>
    /// <exception cref="InvalidOperationException">The candidate is already decided.</exception>
    public void Reject(DateTimeOffset now, string? reason = null)
    {
        if (Status != LearningStatus.Pending)
        {
            throw new InvalidOperationException($"learning candidate {Id} is already {Status}");
        }

        Status = LearningStatus.Rejected;
        DecisionReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        DecidedAt = now;
    }
}

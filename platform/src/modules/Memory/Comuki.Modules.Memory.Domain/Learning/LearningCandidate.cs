using Comuki.Modules.Memory.Domain.Ids;

namespace Comuki.Modules.Memory.Domain.Learning;

/// <summary>
/// One queued rule candidate from the project learning loop: a recurring
/// PR-comment pattern, a repeated verify failure, a reject reason. The row
/// only counts repeats and waits for a human decision — approving produces
/// a PR into the client's git (profiles/rules), never a silent write into
/// memory facts (different lifecycle, per the add-chat-memory contract).
/// </summary>
public sealed class LearningCandidate
{
    internal LearningCandidate()
    {
    }

    /// <summary>Candidate id (UUIDv7, client-side).</summary>
    public LearningCandidateId Id { get; private set; }

    /// <summary>The candidate rule/skill/check text — what a human reviews.</summary>
    public string Pattern { get; private set; } = string.Empty;

    /// <summary>Where the signal came from (PR comment ref, verify failure ref).</summary>
    public string SourceRef { get; private set; } = string.Empty;

    /// <summary>How many times the signal repeated; each sighting increments.</summary>
    public int RepeatCount { get; private set; }

    /// <summary>Review state: pending until a human decides.</summary>
    public LearningStatus Status { get; private set; }

    /// <summary>When the candidate was first seen.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>When the human decided; null while pending.</summary>
    public DateTimeOffset? DecidedAt { get; private set; }

    /// <summary>Creates a pending candidate with the first sighting counted.</summary>
    /// <param name="pattern"></param>
    /// <param name="sourceRef"></param>
    /// <param name="now"></param>
    /// <exception cref="ArgumentException"></exception>
    public static LearningCandidate Create(string pattern, string sourceRef, DateTimeOffset now)
    {
        return string.IsNullOrWhiteSpace(pattern)
            ? throw new ArgumentException("pattern must not be empty", nameof(pattern))
            : string.IsNullOrWhiteSpace(sourceRef)
            ? throw new ArgumentException("source ref must not be empty", nameof(sourceRef))
            : new LearningCandidate
            {
                Id = LearningCandidateId.New(),
                Pattern = pattern.Trim(),
                SourceRef = sourceRef.Trim(),
                RepeatCount = 1,
                Status = LearningStatus.Pending,
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

    /// <summary>Marks the candidate rejected; deciding twice is refused.</summary>
    /// <param name="now"></param>
    /// <exception cref="InvalidOperationException">The candidate is already decided.</exception>
    public void Reject(DateTimeOffset now)
    {
        if (Status != LearningStatus.Pending)
        {
            throw new InvalidOperationException($"learning candidate {Id} is already {Status}");
        }

        Status = LearningStatus.Rejected;
        DecidedAt = now;
    }
}

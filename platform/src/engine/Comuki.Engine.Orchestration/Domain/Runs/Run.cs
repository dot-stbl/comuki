using Comuki.Shared.Kernel.Ids;

namespace Comuki.Engine.Orchestration.Domain.Runs;

/// <summary>
/// Run aggregate — one goal from intake (ticket / chat) decomposed by the
/// brain into a plan of work items. Ids are UUIDv7 generated client-side;
/// status transitions are guarded by <see cref="RunTransitions"/>.
/// </summary>
public sealed class Run
{
    internal Run()
    {
    }

    /// <summary>Strong-typed run id (UUIDv7).</summary>
    public RunId Id { get; private set; }

    /// <summary>Project scope the run belongs to.</summary>
    public ProjectId ProjectId { get; private set; }

    /// <summary>Current lifecycle status; mutated only via <see cref="TransitionTo"/>.</summary>
    public RunStatus Status { get; private set; }

    /// <summary>When the run was admitted into the queue.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Last status change timestamp.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Creates a run in <see cref="RunStatus.Queued"/> — the only legal entry status.</summary>
    /// <param name="projectId"></param>
    /// <param name="now"></param>
    public static Run Create(ProjectId projectId, DateTimeOffset now)
    {
        return new Run
        {
            Id = RunId.New(),
            ProjectId = projectId,
            Status = RunStatus.Queued,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>Applies a status transition; illegal transitions throw — see <see cref="RunTransitions"/>.</summary>
    /// <param name="to"></param>
    /// <param name="now"></param>
    /// <exception cref="InvalidOperationException"></exception>
    public void TransitionTo(RunStatus to, DateTimeOffset now)
    {
        if (!RunTransitions.IsLegal(Status, to))
        {
            throw new InvalidOperationException($"illegal run transition {Status} -> {to}");
        }

        Status = to;
        UpdatedAt = now;
    }
}

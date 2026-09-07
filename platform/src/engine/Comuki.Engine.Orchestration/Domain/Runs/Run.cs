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

    /// <summary>
    /// Autonomy trust class for this run — <see cref="RunTrustClass.Supervised"/>
    /// by default. Mutated only via <see cref="PromoteTo"/> /
    /// <see cref="DemoteTo"/>. Starts at <see cref="RunTrustClass.Supervised"/>
    /// for every new run regardless of project-level defaults; per-run trust is
    /// the conservative choice that the ratchet then promotes.
    /// </summary>
    public RunTrustClass TrustClass { get; private set; }

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
            TrustClass = RunTrustClass.Supervised,
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

    /// <summary>
    /// Promotes the trust class one rung up the
    /// <see cref="RunTrustClass"/> ladder — <see cref="RunTrustClass.Supervised"/>
    /// to <see cref="RunTrustClass.Pilot"/>, or <see cref="RunTrustClass.Pilot"/>
    /// to <see cref="RunTrustClass.Trusted"/>. Calls at <see cref="RunTrustClass.Trusted"/>
    /// are no-ops (already at the top rung). Changes update
    /// <see cref="UpdatedAt"/>.
    /// </summary>
    /// <param name="now"></param>
    public void PromoteTo(DateTimeOffset now)
    {
        var next = TrustClass switch
        {
            RunTrustClass.Supervised => RunTrustClass.Pilot,
            RunTrustClass.Pilot => RunTrustClass.Trusted,
            RunTrustClass.Trusted => RunTrustClass.Trusted,
            _ => TrustClass,
        };

        if (next == TrustClass)
        {
            return;
        }

        TrustClass = next;
        UpdatedAt = now;
    }

    /// <summary>
    /// Demotes the trust class back to <see cref="RunTrustClass.Supervised"/>.
    /// The ratchet treats every failure as a clean reset — there is no
    /// intermediate <c>Pilot</c>-demote state. Calls when already at
    /// <see cref="RunTrustClass.Supervised"/> are no-ops.
    /// </summary>
    /// <param name="now"></param>
    public void DemoteTo(DateTimeOffset now)
    {
        if (TrustClass == RunTrustClass.Supervised)
        {
            return;
        }

        TrustClass = RunTrustClass.Supervised;
        UpdatedAt = now;
    }
}

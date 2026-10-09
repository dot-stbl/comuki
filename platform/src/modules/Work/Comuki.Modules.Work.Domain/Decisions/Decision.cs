namespace Comuki.Modules.Work.Domain.Decisions;

/// <summary>
/// One authorised decision on a <see cref="WorkTask"/>.
/// Decisions are not silently typed — each <see cref="Kind"/> has its own
/// deterministic handler in
/// <c>Comuki.Modules.Work.Application.Decisions</c>, and the same handler is
/// invoked regardless of <see cref="Provenance"/>. The command handler
/// (<c>Work.Decide</c>) is the only entry point; Brain cannot hold a token
/// that calls the underlying state transition directly (task 5.6 invariant).
/// </summary>
public sealed record Decision(
    DecisionKind Kind,
    DecisionProvenance Provenance,
    string ActorId,
    string? ReplacedBrief,
    DateTimeOffset ProposedAt)
{
    /// <summary>Deterministic factory for a retry-after-exhaustion decision — no human / no Brain path, no replacement brief.</summary>
    public static Decision Retry(string actorId, DateTimeOffset now)
    {
        return new(DecisionKind.Retry, DecisionProvenance.Deterministic, actorId, null, now);
    }

    /// <summary>Deterministic factory for a failed-resolution decision — Blocked → Resolved with outcome Failed.</summary>
    public static Decision FailedResolution(string actorId, DateTimeOffset now)
    {
        return new(DecisionKind.FailedResolution, DecisionProvenance.Deterministic, actorId, null, now);
    }

    /// <summary>Human-raised replacement — the new Task inherits the inbound id; the legacy Task is preserved as a historical reference.</summary>
    public static Decision Replacement(string actorId, string replacedBrief, DateTimeOffset now)
    {
        return new(DecisionKind.Replacement, DecisionProvenance.HumanReviewer, actorId, replacedBrief, now);
    }

    /// <summary>Human-raised waiver — distinct-human approval is enforced at the <c>Work.Resolve</c> seam, not here.</summary>
    public static Decision Waiver(string actorId, DateTimeOffset now)
    {
        return new(DecisionKind.Waiver, DecisionProvenance.HumanReviewer, actorId, null, now);
    }

    /// <summary>Cancellation from any non-terminal status — typically raised by the human who owns the Task.</summary>
    public static Decision Cancellation(string actorId, DecisionProvenance provenance, DateTimeOffset now)
    {
        return new(DecisionKind.Cancellation, provenance, actorId, null, now);
    }
}

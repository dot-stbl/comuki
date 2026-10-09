namespace Comuki.Modules.Work.Domain.Attempts;

/// <summary>
/// Read-only projection of one attempt's terminal record — passed to
/// <see cref="WorkTask.EnsureReviewerSeparation"/>
/// so the resolver can reject a same-actor <c>Resolve</c> against the
/// attempt that authored the prior attempt (the umbrella's task 8.3
/// reviewer-separation invariant; surfaces as
/// <c>409 work.completion.reviewer-separation</c>).
/// </summary>
public sealed record ReviewerSeparationAttempt(string AuthoringActorId, DateTimeOffset TerminalAt);

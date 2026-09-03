namespace Comuki.Modules.Intake.Domain.Tickets;

/// <summary>
/// What kind of tracker-side object this ticket represents: a plain
/// issue or a pull request / merge request. The webhook mapper picks
/// the value; the profile router reads it to choose between
/// <c>general</c> (issues) and <c>pr-review</c> (PRs).
/// </summary>
public enum InboundTicketKind
{
    /// <summary>A regular tracker issue (the default).</summary>
    Issue,

    /// <summary>A pull request / merge request (inbound review surface).</summary>
    PullRequest,
}

using Comuki.Modules.Intake.Domain.Tickets;

namespace Comuki.Modules.Intake.Application.Ports.Tickets;

/// <summary>
/// One sync-back transition to push into a tracker: the finished run's
/// status plus the browsable run URL for the comment, and the ticket
/// kind so the provider can decide whether the success path closes the
/// ticket (issues yes, pull-requests no — a Comuki review is a comment,
/// not a merge decision).
/// </summary>
/// <param name="ExternalId">Fully-qualified issue id the transition targets.</param>
/// <param name="ExternalUrl">Browsable issue URL (embedded in the comment).</param>
/// <param name="RunStatus">Terminal run status name (PascalCase).</param>
/// <param name="RunUrl">Browsable run URL.</param>
/// <param name="Kind">Issue or pull request — drives provider-specific behavior.</param>
public sealed record TicketTransition(
    string ExternalId,
    string? ExternalUrl,
    string RunStatus,
    Uri RunUrl,
    InboundTicketKind Kind);

namespace Comuki.Modules.Intake.Application.Ports;

/// <summary>
/// One sync-back transition to push into a tracker: the finished run's
/// status plus the browsable run URL for the comment.
/// </summary>
/// <param name="ExternalId">Fully-qualified issue id the transition targets.</param>
/// <param name="ExternalUrl">Browsable issue URL (embedded in the comment).</param>
/// <param name="RunStatus">Terminal run status name (PascalCase).</param>
/// <param name="RunUrl">Browsable run URL.</param>
public sealed record TicketTransition(
    string ExternalId,
    string? ExternalUrl,
    string RunStatus,
    Uri RunUrl);

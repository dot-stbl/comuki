namespace Comuki.Host.Intake.Models;

/// <summary>Inbox claim body (POST /api/v1/inbox/claim).</summary>
public sealed class ClaimTicketRequest
{
    /// <summary>The pending ticket to claim.</summary>
    public required Guid TicketId { get; init; }
}

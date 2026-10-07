namespace Comuki.Host.Integration.Models;

/// <summary>Inbox claim body (POST /api/v1/integration/inbox/claim).</summary>
public sealed class ClaimInboundItemRequest
{
    /// <summary>The pending ticket to claim.</summary>
    public required Guid TicketId { get; init; }
}

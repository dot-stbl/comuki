namespace Comuki.Host.Intake.Models;

/// <summary>Native ticket creation body (POST /api/v1/tickets).</summary>
public sealed class CreateNativeTicketRequest
{
    /// <summary>Project the ticket (and its run) belongs to.</summary>
    public required Guid ProjectId { get; init; }

    /// <summary>Ticket title.</summary>
    public required string Title { get; init; } = string.Empty;

    /// <summary>Ticket body.</summary>
    public string Body { get; init; } = string.Empty;

    /// <summary>Caller-supplied dedupe id; generated when empty.</summary>
    public string? ExternalId { get; init; }

    /// <summary>Author label.</summary>
    public string? Author { get; init; }
}

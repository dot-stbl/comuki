namespace Comuki.Host.Chat.Models;

/// <summary>Approve-request body: resolves the pending plan interrupt.</summary>
public sealed class ChatApproveRequest
{
    /// <summary>Approve when true, reject otherwise.</summary>
    public required bool Approved { get; init; }

    /// <summary>Optional rejection reason.</summary>
    public string? Reason { get; init; }
}

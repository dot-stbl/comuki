namespace Comuki.Host.Chat.Models.Requests;

/// <summary>Post-message request body: one chat turn.</summary>
public sealed class PostChatMessageRequest
{
    /// <summary>Raw user message (plain text or a /command).</summary>
    public required string Message { get; init; }
}

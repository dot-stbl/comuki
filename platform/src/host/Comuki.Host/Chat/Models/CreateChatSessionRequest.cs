namespace Comuki.Host.Chat.Models;

/// <summary>Create-session request body.</summary>
public sealed class CreateChatSessionRequest
{
    /// <summary>Optional project scope the session talks about.</summary>
    public Guid? ProjectId { get; init; }

    /// <summary>Optional human title; defaults to a placeholder.</summary>
    public string? Title { get; init; }
}

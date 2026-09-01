using Comuki.Modules.Chat.Domain.Sessions;

namespace Comuki.Host.Chat.Models;

/// <summary>Session read model.</summary>
public sealed class ChatSessionView
{
    /// <summary>Session id.</summary>
    public required Guid Id { get; init; }

    /// <summary>Project scope; null when the session has none yet.</summary>
    public Guid? ProjectId { get; init; }

    /// <summary>Human title.</summary>
    public required string Title { get; init; }

    /// <summary>Lifecycle status wire string (lower-cased).</summary>
    public required string Status { get; init; }

    /// <summary>When the session was created.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Last activity stamp.</summary>
    public required DateTimeOffset UpdatedAt { get; init; }

    /// <summary>Maps the domain aggregate.</summary>
    /// <param name="session"></param>
    public static ChatSessionView Of(ChatSession session)
    {
        return new ChatSessionView
        {
            Id = session.Id.Value,
            ProjectId = session.ProjectId?.Value,
            Title = session.Title,
            Status = session.Status.ToString().ToLowerInvariant(),
            CreatedAt = session.CreatedAt,
            UpdatedAt = session.UpdatedAt,
        };
    }
}

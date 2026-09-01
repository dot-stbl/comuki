using Comuki.Modules.Chat.Domain.Ids;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Chat.Domain.Sessions;

/// <summary>
/// Chat session — one conversation between a subject and the harness. The
/// graph state (current node, pending approve, wizard variables) lives in
/// the Voluta checkpoint under the session id; this entity carries only the
/// row-level identity and lifecycle.
/// </summary>
public sealed class ChatSession
{
    internal ChatSession()
    {
    }

    /// <summary>Strong-typed session id (UUIDv7).</summary>
    public ChatSessionId Id { get; private set; }

    /// <summary>Project scope the session talks about; null until /init creates one.</summary>
    public ProjectId? ProjectId { get; private set; }

    /// <summary>The subject (user or api key) that owns the session.</summary>
    public Guid SubjectId { get; private set; }

    /// <summary>Human-readable title; defaults to a placeholder until the first message.</summary>
    public string Title { get; private set; } = string.Empty;

    /// <summary>Session lifecycle status.</summary>
    public ChatSessionStatus Status { get; private set; }

    /// <summary>When the session was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Last activity (message or approve); drives the archive sweep.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Creates an active session.</summary>
    /// <param name="projectId"></param>
    /// <param name="subjectId"></param>
    /// <param name="title"></param>
    /// <param name="now"></param>
    public static ChatSession Create(ProjectId? projectId, Guid subjectId, string title, DateTimeOffset now)
    {
        return new ChatSession
        {
            Id = ChatSessionId.New(),
            ProjectId = projectId,
            SubjectId = subjectId,
            Title = string.IsNullOrWhiteSpace(title) ? "New chat" : title.Trim(),
            Status = ChatSessionStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>Soft-archives the session; archiving twice is a no-op.</summary>
    /// <param name="now"></param>
    public void Archive(DateTimeOffset now)
    {
        if (Status == ChatSessionStatus.Archived)
        {
            return;
        }

        Status = ChatSessionStatus.Archived;
        UpdatedAt = now;
    }

    /// <summary>Stamps activity; called on every appended message.</summary>
    /// <param name="now"></param>
    public void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
    }
}

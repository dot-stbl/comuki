namespace Comuki.Modules.Memory.Domain.Chat;

/// <summary>
/// The persisted Voluta graph state of a chat session: current node, a
/// pending approve-interrupt, collected variables — one jsonb snapshot per
/// session, overwritten on every checkpoint. Sessions resume from this
/// row days later (30d auto-archive is a follow-up on top).
/// </summary>
public sealed class ChatCheckpoint
{
    internal ChatCheckpoint()
    {
    }

    /// <summary>The session id (Voluta thread id) — primary key.</summary>
    public string SessionId { get; private set; } = string.Empty;

    /// <summary>Serialized graph state (jsonb column).</summary>
    public string GraphState { get; private set; } = string.Empty;

    /// <summary>When the checkpoint was last written.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Creates a checkpoint row (insert on first save, update after).</summary>
    /// <param name="sessionId"></param>
    /// <param name="graphStateJson"></param>
    /// <param name="now"></param>
    /// <exception cref="ArgumentException"></exception>
    public static ChatCheckpoint Create(string sessionId, string graphStateJson, DateTimeOffset now)
    {
        return string.IsNullOrWhiteSpace(sessionId)
            ? throw new ArgumentException("session id must not be empty", nameof(sessionId))
            : string.IsNullOrWhiteSpace(graphStateJson)
            ? throw new ArgumentException("graph state must not be empty", nameof(graphStateJson))
            : new ChatCheckpoint
            {
                SessionId = sessionId.Trim(),
                GraphState = graphStateJson,
                UpdatedAt = now,
            };
    }

    /// <summary>Replaces the graph state (update path).</summary>
    /// <param name="graphStateJson"></param>
    /// <param name="now"></param>
    public void Replace(string graphStateJson, DateTimeOffset now)
    {
        GraphState = graphStateJson;
        UpdatedAt = now;
    }
}

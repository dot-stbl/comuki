namespace Comuki.Modules.Chat.Domain.Ids;

/// <summary>
/// Strong-typed identifier of a chat session. UUIDv7 generated client-side.
/// </summary>
/// <param name="Value"></param>
public readonly record struct ChatSessionId(Guid Value)
{
    /// <summary>Generates a new session id.</summary>
    /// <returns></returns>
    public static ChatSessionId New()
    {
        return new(Guid.CreateVersion7());
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return Value.ToString();
    }
}

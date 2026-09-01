using Comuki.Modules.Memory.Domain.Chat;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Memory.Unit;

/// <summary>
/// Chat short-term memory entities: message role canonicalization plus
/// argument guards, and checkpoint create/replace.
/// </summary>
public sealed class ChatEntitiesShould
{
    private static readonly DateTimeOffset now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Given a message, when Create is called, then the role is canonicalized")]
    public void CanonicalizeMessageRole()
    {
        var message = ChatMessage.Create("session-1", "  User ", "hello", now);

        message.Role.ShouldBe("user");
        message.SessionId.ShouldBe("session-1");
        message.Content.ShouldBe("hello");
    }

    [Fact(DisplayName = "Given an empty content, when Create is called, then ArgumentException is thrown")]
    public void RefuseEmptyMessageContent()
    {
        Should.Throw<ArgumentException>(static () => ChatMessage.Create("session-1", "user", "", now));
    }

    [Fact(DisplayName = "Given a checkpoint, when Replace is called, then state and timestamp move")]
    public void ReplaceCheckpointState()
    {
        var checkpoint = ChatCheckpoint.Create("session-1", /*lang=json,strict*/ """{"node":"start"}""", now);
        var updatedAt = now.AddMinutes(3);

        checkpoint.Replace(/*lang=json,strict*/ """{"node":"approve"}""", updatedAt);

        checkpoint.GraphState.ShouldBe(/*lang=json,strict*/ """{"node":"approve"}""");
        checkpoint.UpdatedAt.ShouldBe(updatedAt);
    }

    [Fact(DisplayName = "Given an empty graph state, when Replace is called, then ArgumentException is thrown")]
    public void RefuseEmptyGraphState()
    {
        var checkpoint = ChatCheckpoint.Create("session-1", /*lang=json,strict*/ """{"node":"start"}""", now);

        Should.Throw<ArgumentException>(() => checkpoint.Replace("  ", now));
    }
}

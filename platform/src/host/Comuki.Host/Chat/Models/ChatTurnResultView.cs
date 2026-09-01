using System.Text.Json;
using Comuki.Modules.Chat.Application.Sessions;

namespace Comuki.Host.Chat.Models;

/// <summary>
/// Turn outcome: the reply view plus the pending approve card when the
/// thread interrupted. The card carries the canonical plan JSON the
/// dashboard renders; approve/reject posts to
/// <c>/api/v1/chat/sessions/{id}/approve</c>.
/// </summary>
public sealed class ChatTurnResultView
{
    /// <summary>Journal rows the action appended (digest, tool, reply).</summary>
    public required IReadOnlyList<ChatMessageView> Messages { get; init; }

    /// <summary>True when the thread waits for an approve decision.</summary>
    public required bool AwaitingApproval { get; init; }

    /// <summary>Parsed plan of the pending card; null when not awaiting.</summary>
    public JsonDocument? PendingPlan { get; init; }

    /// <summary>Maps the application turn result.</summary>
    /// <param name="result"></param>
    public static ChatTurnResultView Of(ChatTurnResult result)
    {
        return new ChatTurnResultView
        {
            Messages = [.. result.NewMessages.Select(ChatMessageView.Of)],
            AwaitingApproval = result.AwaitingApproval,
            PendingPlan = result.PendingPlanJson is { Length: > 0 }
                ? JsonDocument.Parse(result.PendingPlanJson)
                : null,
        };
    }
}

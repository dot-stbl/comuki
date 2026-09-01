using Comuki.Modules.Chat.Domain.Messages;
using Comuki.Modules.Chat.Domain.Sessions;

namespace Comuki.Modules.Chat.Application.Sessions;

/// <summary>
/// Drives chat turns: posts a user message through the graph, resolves the
/// pending approve decision, and returns what the turn journaled.
/// </summary>
public interface IChatTurnService
{
    /// <summary>Runs one user turn on the session thread; throws <see cref="ChatApprovePendingException"/> while an approve is pending.</summary>
    /// <param name="session">Session aggregate.</param>
    /// <param name="message">Raw user message.</param>
    /// <param name="cancellationToken"></param>
    public Task<ChatTurnResult> PostAsync(ChatSession session, string message, CancellationToken cancellationToken = default);

    /// <summary>Resolves the pending approve interrupt (approve → act, reject → done).</summary>
    /// <param name="session">Session aggregate.</param>
    /// <param name="approved">Approve when true, reject otherwise.</param>
    /// <param name="reason">Optional rejection reason.</param>
    /// <param name="cancellationToken"></param>
    public Task<ChatTurnResult> ApproveAsync(ChatSession session, bool approved, string? reason, CancellationToken cancellationToken = default);
}

/// <summary>
/// What one turn produced: the journal rows appended this action, whether
/// the thread ended waiting for an approve decision, and the canonical plan
/// JSON of that pending card.
/// </summary>
/// <param name="NewMessages">Journal rows appended by the action (digest, tool, reply, decision).</param>
/// <param name="AwaitingApproval">True when the thread is interrupted on the approve card.</param>
/// <param name="PendingPlanJson">Canonical plan JSON when <paramref name="AwaitingApproval"/>; null otherwise.</param>
public sealed record ChatTurnResult(
    IReadOnlyList<ChatMessage> NewMessages,
    bool AwaitingApproval,
    string? PendingPlanJson);

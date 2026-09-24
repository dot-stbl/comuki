namespace Comuki.Engine.Orchestration.Domain.Inbox;

/// <summary>
/// Dedupe marker: a one-row-per-message-id ledger that lets an admission
/// call detect a duplicate delivery. The PK uniqueness constraint on
/// <c>message_id</c> is what resolves concurrent double-claims to exactly
/// one winner — <see cref="Infrastructure.Inbox.InboxEf.TryClaimAsync"/>
/// is the only entry point.
/// </summary>
public sealed class InboxReceipt
{
    internal InboxReceipt()
    {
    }

    /// <summary>The wire identity of the delivered message — free-form string, not a Guid, so WS9's admission call may use a non-UUID identity.</summary>
    public string MessageId { get; private set; } = string.Empty;

    /// <summary>When this id was first claimed.</summary>
    public DateTimeOffset ReceivedAt { get; private set; }

    /// <summary>
    /// Stages a receipt for a newly-claimed message id. <paramref name="messageId"/>
    /// must be non-empty.
    /// </summary>
    /// <param name="messageId"></param>
    /// <param name="now"></param>
    /// <exception cref="ArgumentException"></exception>
    public static InboxReceipt Create(string messageId, DateTimeOffset now)
    {
        return string.IsNullOrWhiteSpace(messageId)
            ? throw new ArgumentException("inbox receipt message id must not be empty", nameof(messageId))
            : new InboxReceipt
            {
                MessageId = messageId,
                ReceivedAt = now,
            };
    }
}

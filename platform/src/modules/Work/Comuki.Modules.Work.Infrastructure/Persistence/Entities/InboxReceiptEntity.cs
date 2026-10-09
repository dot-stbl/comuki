namespace Comuki.Modules.Work.Infrastructure.Persistence.Entities;

/// <summary>
/// EF entity for <c>work.inbox_receipts</c> — the Work-side mirror
/// dedupe ledger (per <c>add-work-management/design.md</c> Open
/// Question A's mirror-table recommendation). The PK on
/// <c>message_id</c> is the <c>TryClaimAsync</c> race arbiter; a
/// losing or retried caller observes the existing row and no-ops.
/// The Work inbox sits on the same <c>orchestration</c>-style
/// pattern the engine ships, scoped to the <c>work</c> schema so
/// the Work context's dedupe keys never collide with the engine's
/// admission-dedupe keys.
/// </summary>
public sealed record InboxReceiptEntity(
    string MessageId,
    DateTimeOffset ClaimedAt)
{
    public InboxReceiptEntity() : this(MessageId: string.Empty, ClaimedAt: default)
    {
    }
}

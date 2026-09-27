using Comuki.Engine.Orchestration.Domain.Inbox;
using Shouldly;
using Xunit;

namespace Comuki.Engine.Orchestration.Unit.StatusMachine;

/// <summary>
/// Invariant guards of <see cref="InboxReceipt"/>: <see cref="InboxReceipt.Create"/>
/// rejects empty message ids and seeds a row with the supplied
/// <c>received_at</c>. The dedupe-race property (one row per
/// <c>message_id</c>) lives behind the PK uniqueness constraint and is
/// covered by the <c>InboxDedupeShould</c> integration tests, where a
/// real Postgres distinguishes <c>INSERT ... ON CONFLICT</c> winner from
/// loser.
/// </summary>
public sealed class InboxReceiptShould
{
    private static readonly DateTimeOffset now = new(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Given a non-empty message id, when Create is called, then the receipt carries the id and received_at")]
    public void CreateReceipt()
    {
        var receipt = InboxReceipt.Create("admission-42", now);

        receipt.MessageId.ShouldBe("admission-42");
        receipt.ReceivedAt.ShouldBe(now);
    }

    [Theory(DisplayName = "Given a blank message id, when Create is called, then it throws")]
    [InlineData("")]
    [InlineData(" ")]
    public void RejectBlankMessageIdOnCreate(string messageId)
    {
        Should.Throw<ArgumentException>(() => InboxReceipt.Create(messageId, now));
    }
}

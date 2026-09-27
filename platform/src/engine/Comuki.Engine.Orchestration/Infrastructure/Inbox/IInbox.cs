namespace Comuki.Engine.Orchestration.Infrastructure.Inbox;

/// <summary>
/// Dedupe ledger for incoming messages: <see cref="TryClaimAsync"/>
/// returns <c>true</c> the first time a given id is claimed and
/// <c>false</c> on every later call with the same id, including a
/// concurrent double-claim. The underlying PK uniqueness constraint on
/// <c>message_id</c> resolves the race to exactly one winner.
/// </summary>
public interface IInbox
{
    /// <summary>
    /// Attempts to claim <paramref name="messageId"/> as newly-seen.
    /// Returns <c>true</c> the first time a given id is claimed (caller
    /// should proceed); <c>false</c> on every later call with the same
    /// id — including a concurrent double-claim, which the underlying PK
    /// uniqueness constraint resolves to exactly one winner (caller
    /// should treat <c>false</c> as "a duplicate delivery, skip").
    /// </summary>
    /// <param name="messageId">Free-form wire identity of the delivered message.</param>
    /// <param name="cancellationToken"></param>
    public Task<bool> TryClaimAsync(string messageId, CancellationToken cancellationToken);
}

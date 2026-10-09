using System.Collections.Concurrent;
using Comuki.Modules.Work.Application.Ports.Inbox;
using Comuki.Modules.Work.Infrastructure.Inbox;

namespace Comuki.Modules.Work.Unit.Fakes;

/// <summary>
/// In-memory <see cref="IWorkInbox"/> for unit tests. Mirrors the
/// shape of the EF-backed <c>EfWorkInbox</c> implementation
/// (claim-once semantics) so unit tests exercise the same
/// idempotency seam without standing up a Postgres round-trip.
/// </summary>
public sealed class InMemoryWorkInbox : IWorkInbox
{
    private readonly HashSet<string> claimed = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task<bool> TryClaimAsync(string messageId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(claimed.Add(messageId));
    }
}

/// <summary>
/// In-memory <see cref="IWorkInboxWatermarkStore"/> for unit tests.
/// Mirrors the EF-backed <c>EfWorkInboxWatermarkStore</c> contract:
/// monotonic per-type watermark advance via
/// <see cref="RecordAsync(string, Guid, CancellationToken)"/>.
/// </summary>
public sealed class InMemoryWorkInboxWatermarkStore : IWorkInboxWatermarkStore
{
    private readonly ConcurrentDictionary<string, Guid> byType = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task<Guid> GetAsync(string type, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(byType.TryGetValue(type, out var value) ? value : Guid.Empty);
    }

    /// <inheritdoc />
    public Task RecordAsync(string type, Guid lastSeenId, CancellationToken cancellationToken = default)
    {
        byType.AddOrUpdate(
            type,
            lastSeenId,
            (_, current) => lastSeenId > current ? lastSeenId : current);
        return Task.CompletedTask;
    }
}

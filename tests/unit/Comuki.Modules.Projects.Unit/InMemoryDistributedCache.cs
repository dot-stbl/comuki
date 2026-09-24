using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Distributed;

namespace Comuki.Modules.Projects.Unit;

/// <summary>
/// In-process <see cref="IDistributedCache"/> double used by the unit
/// tests for the distributed settings cache. Stores UTF-8 byte payloads
/// keyed by string, mimics the contract
/// <c>Microsoft.Extensions.Caching.StackExchangeRedis.RedisCache</c>
/// implements: <c>Get</c> returns null when the key is missing or
/// expired, <c>Set</c> with a TTL stores the bytes verbatim and respects
/// the absolute-expiry window when present. Sufficient for the cache
/// contract surface the settings cache exercises; not a Redis simulator.
/// </summary>
internal sealed class InMemoryDistributedCache() : IDistributedCache
{
    private readonly ConcurrentDictionary<string, Entry> store = new();

    public IReadOnlyDictionary<string, byte[]> Snapshot()
    {
        return store.ToDictionary(static pair => pair.Key, static pair => pair.Value.Bytes);
    }

    public byte[]? Get(string key)
    {
        return TryGetLiveEntry(key) is { } entry ? entry.Bytes : null;
    }

    public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
    {
        return Task.FromResult(Get(key));
    }

    public void Refresh(string key)
    {
        // Refresh resets the sliding-expiry clock; the contract here is
        // "if you set with a TTL, refresh extends it". The double below
        // has no sliding expiry to reset — absolute TTL is fixed — so
        // this is a no-op on the in-memory store, matching the
        // "nothing to refresh" semantics for absolute-only entries.
    }

    public Task RefreshAsync(string key, CancellationToken token = default)
    {
        Refresh(key);
        return Task.CompletedTask;
    }

    public void Remove(string key)
    {
        store.TryRemove(key, out _);
    }

    public Task RemoveAsync(string key, CancellationToken token = default)
    {
        Remove(key);
        return Task.CompletedTask;
    }

    public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
    {
        store[key] = new Entry(value, DateTimeOffset.UtcNow, options.AbsoluteExpiration, options.AbsoluteExpirationRelativeToNow);
    }

    public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
    {
        Set(key, value, options);
        return Task.CompletedTask;
    }

    private Entry? TryGetLiveEntry(string key)
    {
        if (!store.TryGetValue(key, out var candidate))
        {
            return null;
        }

        if (IsExpired(candidate))
        {
            store.TryRemove(key, out _);
            return null;
        }

        return candidate;
    }

    private static bool IsExpired(Entry entry)
    {
        var now = DateTimeOffset.UtcNow;
        return (entry.AbsoluteExpiration is { } absolute && now >= absolute)
            || (entry.AbsoluteExpirationRelativeToNow is { } relative && now >= entry.StoredAt + relative);
    }

    private sealed record Entry(
        byte[] Bytes,
        DateTimeOffset StoredAt,
        DateTimeOffset? AbsoluteExpiration,
        TimeSpan? AbsoluteExpirationRelativeToNow);
}

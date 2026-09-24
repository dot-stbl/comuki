namespace Comuki.Shared.Redis.Options;

/// <summary>
/// Redis distributed-cache wiring for multi-replica Host (issue #11,
/// "Redis cache при multi-replica Host"). Bound from the <c>Redis</c>
/// section; <see cref="RedisCacheOptionsValidator"/> fails the boot on a
/// missing connection string only when <see cref="Enabled"/> is true. A
/// disabled section (the default) leaves every consumer on its
/// in-process fallback — e.g. the Projects settings snapshot cache stays
/// on <c>Microsoft.Extensions.Caching.Memory.IMemoryCache</c>.
/// </summary>
public sealed class RedisCacheOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Redis";

    /// <summary>Default <see cref="InstanceName"/> when the section omits it.</summary>
    public const string DefaultInstanceName = "comuki:";

    /// <summary>
    /// Master switch. <c>false</c> (default) skips the
    /// <see cref="Microsoft.Extensions.Caching.Distributed.IDistributedCache"/>
    /// registration entirely — existing single-replica deployments keep
    /// working unchanged, no <c>Redis</c> section required.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// StackExchange.Redis configuration string — a host:port pair
    /// (<c>redis:6379</c>) or a full configuration string
    /// (<c>redis:6379,abortConnect=false,connectTimeout=2000</c>).
    /// Required when <see cref="Enabled"/> is true
    /// (<see cref="RedisCacheOptionsValidator"/>); not a data-annotation
    /// <c>[Required]</c> because that would trip a disabled deployment
    /// that ships no <c>Redis</c> section at all.
    /// </summary>
    public string ConnectionString { get; init; } = string.Empty;

    /// <summary>
    /// Prefixes every cache key so multiple Comuki stacks sharing one
    /// Redis (dev / staging split) never collide.
    /// </summary>
    public string InstanceName { get; init; } = DefaultInstanceName;
}

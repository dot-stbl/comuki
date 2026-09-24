using Comuki.Shared.Redis.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Comuki.Shared.Redis;

/// <summary>
/// Single composition point for the Redis-backed distributed cache
/// (issue #11, "Redis cache при multi-replica Host"). Callers depend on
/// the framework abstraction
/// <see cref="Microsoft.Extensions.Caching.Distributed.IDistributedCache"/>,
/// never on a Redis SDK type — the wire-format choice (Redis today,
/// potentially a different backend tomorrow) stays inside this shared
/// project.
/// </summary>
public static class RedisCacheExtensions
{
    /// <summary>
    /// Binds <see cref="RedisCacheOptions"/> from the <c>Redis</c>
    /// section and registers
    /// <see cref="Microsoft.Extensions.Caching.Distributed.IDistributedCache"/>
    /// when <see cref="RedisCacheOptions.Enabled"/> is <c>true</c>. When
    /// disabled (the default), the registration is a no-op — callers
    /// that resolve <c>IDistributedCache</c> optionally (e.g. the
    /// Projects settings snapshot cache factory) fall back to their
    /// in-process alternative. Validation runs at startup via
    /// <c>ValidateOnStart()</c> so a missing <c>ConnectionString</c>
    /// fails the boot instead of the first cache hit.
    /// </summary>
    /// <param name="services">DI container the cache registration is added to.</param>
    /// <param name="configuration">Application configuration; the <c>Redis</c> section is bound.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddComukiRedisCache(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RedisCacheOptions>()
            .Bind(configuration.GetSection(RedisCacheOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<RedisCacheOptions>, RedisCacheOptionsValidator>();

        var redisConfiguration = configuration.GetSection(RedisCacheOptions.SectionName).Get<RedisCacheOptions>();
        if (redisConfiguration is not { Enabled: true })
        {
            return services;
        }

        services.AddStackExchangeRedisCache(redisOptions =>
        {
            redisOptions.ConnectionMultiplexerFactory = () => RedisConnectionFactory.ConnectAsync(redisConfiguration.ConnectionString);
            redisOptions.InstanceName = redisConfiguration.InstanceName;
        });

        return services;
    }
}

/// <summary>
/// Connects to Redis from the bound <see cref="RedisCacheOptions.ConnectionString"/>.
/// Kept out of <see cref="RedisCacheExtensions"/> so the installer stays a
/// single, readable registration chain (class-layout-and-tooling.md §1a —
/// no private methods on the installer; a file-scoped helper is the
/// prescribed extraction for logic that belongs with just this file).
/// </summary>
file static class RedisConnectionFactory
{
    public static async Task<IConnectionMultiplexer> ConnectAsync(string connectionString)
    {
        return await ConnectionMultiplexer.ConnectAsync(ConfigurationOptions.Parse(connectionString));
    }
}

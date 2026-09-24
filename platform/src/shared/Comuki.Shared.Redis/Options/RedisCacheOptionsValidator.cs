using Microsoft.Extensions.Options;

namespace Comuki.Shared.Redis.Options;

/// <summary>
/// Validator for <see cref="RedisCacheOptions"/> that skips the
/// required-field check when the cache is disabled — the common case,
/// including build-time OpenAPI document generation, which boots the
/// host without a <c>Redis</c> section at all. Registered by
/// <see cref="RedisCacheExtensions.AddComukiRedisCache"/>;
/// <c>public</c> so the host composition (cross-assembly) can reference
/// it directly if it ever needs to.
/// </summary>
public sealed class RedisCacheOptionsValidator() : IValidateOptions<RedisCacheOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, RedisCacheOptions options)
    {
        return options.Enabled && string.IsNullOrWhiteSpace(options.ConnectionString)
            ? ValidateOptionsResult.Fail("Redis:ConnectionString is required when Redis:Enabled is true.")
            : ValidateOptionsResult.Success;
    }
}

using Comuki.Platform.Routing.Interfaces;
using Comuki.Platform.Routing.Options;
using Microsoft.Extensions.Options;

namespace Comuki.Platform.Routing.Services;

/// <inheritdoc />
public sealed class KeyPool : IKeyPool
{
    private readonly System.Threading.Lock gate = new();
    private readonly Dictionary<string, DateTimeOffset> cooldownUntil;
    private readonly string[] keys;
    private readonly TimeProvider timeProvider;
    private readonly TimeSpan defaultCooldown;

    public KeyPool(IOptions<RotationOptions> options, TimeProvider timeProvider)
    {
        var value = options.Value;
        keys = value.ApiKeys;
        defaultCooldown = value.DefaultCooldown;
        this.timeProvider = timeProvider;
        cooldownUntil = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public int Count => keys.Length;

    /// <inheritdoc />
    public string? TryAcquire()
    {
        lock (gate)
        {
            var now = timeProvider.GetUtcNow();
            foreach (var key in keys)
            {
                if (!cooldownUntil.TryGetValue(key, out var until) || until <= now)
                {
                    return key;
                }
            }

            return null;
        }
    }

    /// <inheritdoc />
    public void MarkExhausted(string apiKey, TimeSpan? retryAfter)
    {
        lock (gate)
        {
            cooldownUntil[apiKey] = timeProvider.GetUtcNow() + (retryAfter ?? defaultCooldown);
        }
    }
}

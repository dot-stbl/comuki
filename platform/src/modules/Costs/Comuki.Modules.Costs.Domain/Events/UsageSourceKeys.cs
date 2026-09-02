namespace Comuki.Modules.Costs.Domain.Events;

/// <summary>Stable wire / DB keys for <see cref="UsageSource"/>.</summary>
public static class UsageSourceKeys
{
    /// <summary><see cref="UsageSource.Proxy"/>.</summary>
    public const string Proxy = "proxy";

    /// <summary><see cref="UsageSource.Brain"/>.</summary>
    public const string Brain = "brain";

    /// <summary><see cref="UsageSource.Worker"/>.</summary>
    public const string Worker = "worker";

    /// <summary><see cref="UsageSource.System"/>.</summary>
    public const string System = "system";

    /// <summary>Maps enum → key.</summary>
    /// <param name="source"></param>
    public static string Of(UsageSource source)
    {
        return source switch
        {
            UsageSource.Proxy => Proxy,
            UsageSource.Brain => Brain,
            UsageSource.Worker => Worker,
            UsageSource.System => System,
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, null),
        };
    }

    /// <summary>Maps key → enum; unknown keys throw.</summary>
    /// <param name="key"></param>
    public static UsageSource Parse(string key)
    {
        return key switch
        {
            Proxy => UsageSource.Proxy,
            Brain => UsageSource.Brain,
            Worker => UsageSource.Worker,
            System => UsageSource.System,
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, "unknown usage source"),
        };
    }
}

namespace Comuki.Modules.Memory.Domain.Facts.Kinds;

/// <summary>Stable wire keys for <see cref="MemoryFactKind"/>.</summary>
public static class MemoryFactKindKeys
{
    /// <summary>Key of <see cref="MemoryFactKind.Standing"/>.</summary>
    public const string Standing = "standing";

    /// <summary>Key of <see cref="MemoryFactKind.Ephemeral"/>.</summary>
    public const string Ephemeral = "ephemeral";

    /// <summary>Key of <see cref="MemoryFactKind.BlackboardFinding"/>.</summary>
    public const string BlackboardFinding = "blackboard-finding";

    /// <summary>Maps a kind to its wire key.</summary>
    public static string Key(MemoryFactKind kind)
    {
        return kind switch
        {
            MemoryFactKind.Standing => Standing,
            MemoryFactKind.Ephemeral => Ephemeral,
            MemoryFactKind.BlackboardFinding => BlackboardFinding,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
    }

    /// <summary>Parses a wire key; null when unknown.</summary>
    public static MemoryFactKind? Parse(string key)
    {
        return key switch
        {
            Standing => MemoryFactKind.Standing,
            Ephemeral => MemoryFactKind.Ephemeral,
            BlackboardFinding => MemoryFactKind.BlackboardFinding,
            _ => null,
        };
    }

    /// <summary>Parses a wire key or throws — the EF converter path (expression trees cannot inline throws).</summary>
    /// <exception cref="InvalidOperationException">The key is unknown.</exception>
    public static MemoryFactKind ParseRequired(string key)
    {
        return Parse(key) ?? throw new InvalidOperationException($"unknown memory fact kind key '{key}'");
    }
}

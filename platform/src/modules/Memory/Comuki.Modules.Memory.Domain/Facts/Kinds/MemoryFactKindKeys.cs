namespace Comuki.Modules.Memory.Domain.Facts.Kinds;

/// <summary>Stable wire keys for <see cref="MemoryFactKind"/>.</summary>
public static class MemoryFactKindKeys
{
    /// <summary>Key of <see cref="MemoryFactKind.Standing"/>.</summary>
    public const string Standing = "standing";

    /// <summary>Key of <see cref="MemoryFactKind.Ephemeral"/>.</summary>
    public const string Ephemeral = "ephemeral";

    /// <summary>Maps a kind to its wire key.</summary>
    /// <param name="kind"></param>
    public static string Key(MemoryFactKind kind)
    {
        return kind switch
        {
            MemoryFactKind.Standing => Standing,
            MemoryFactKind.Ephemeral => Ephemeral,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
    }

    /// <summary>Parses a wire key; null when unknown.</summary>
    /// <param name="key"></param>
    public static MemoryFactKind? Parse(string key)
    {
        return key switch
        {
            Standing => MemoryFactKind.Standing,
            Ephemeral => MemoryFactKind.Ephemeral,
            _ => null,
        };
    }

    /// <summary>Parses a wire key or throws — the EF converter path (expression trees cannot inline throws).</summary>
    /// <param name="key"></param>
    /// <exception cref="InvalidOperationException">The key is unknown.</exception>
    public static MemoryFactKind ParseRequired(string key)
    {
        return Parse(key) ?? throw new InvalidOperationException($"unknown memory fact kind key '{key}'");
    }
}

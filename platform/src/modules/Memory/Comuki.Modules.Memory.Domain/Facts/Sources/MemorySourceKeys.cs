namespace Comuki.Modules.Memory.Domain.Facts.Sources;

/// <summary>Stable wire keys for <see cref="MemorySource"/>.</summary>
public static class MemorySourceKeys
{
    /// <summary>Key of <see cref="MemorySource.Chat"/>.</summary>
    public const string Chat = "chat";

    /// <summary>Key of <see cref="MemorySource.Human"/>.</summary>
    public const string Human = "human";

    /// <summary>Key of <see cref="MemorySource.Run"/>.</summary>
    public const string Run = "run";

    /// <summary>Key of <see cref="MemorySource.LearningApproved"/>.</summary>
    public const string LearningApproved = "learning-approved";

    /// <summary>Maps a source to its wire key.</summary>
    /// <param name="source"></param>
    public static string Key(MemorySource source)
    {
        return source switch
        {
            MemorySource.Chat => Chat,
            MemorySource.Human => Human,
            MemorySource.Run => Run,
            MemorySource.LearningApproved => LearningApproved,
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, null),
        };
    }

    /// <summary>Parses a wire key; null when unknown.</summary>
    /// <param name="key"></param>
    public static MemorySource? Parse(string key)
    {
        return key switch
        {
            Chat => MemorySource.Chat,
            Human => MemorySource.Human,
            Run => MemorySource.Run,
            LearningApproved => MemorySource.LearningApproved,
            _ => null,
        };
    }

    /// <summary>Parses a wire key or throws — the EF converter path (expression trees cannot inline throws).</summary>
    /// <param name="key"></param>
    /// <exception cref="InvalidOperationException">The key is unknown.</exception>
    public static MemorySource ParseRequired(string key)
    {
        return Parse(key) ?? throw new InvalidOperationException($"unknown memory source key '{key}'");
    }
}

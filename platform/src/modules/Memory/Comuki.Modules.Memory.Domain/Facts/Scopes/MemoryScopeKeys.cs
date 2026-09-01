namespace Comuki.Modules.Memory.Domain.Facts.Scopes;

/// <summary>
/// Stable wire keys for <see cref="MemoryScope"/> — the database column
/// values and the brain/chat tool surface use these strings.
/// </summary>
public static class MemoryScopeKeys
{
    /// <summary>Key of <see cref="MemoryScope.User"/>.</summary>
    public const string User = "user";

    /// <summary>Key of <see cref="MemoryScope.Project"/>.</summary>
    public const string Project = "project";

    /// <summary>Key of <see cref="MemoryScope.Global"/>.</summary>
    public const string Global = "global";

    /// <summary>Subject id used by global-scope facts (they have no narrower owner).</summary>
    public const string GlobalSubject = "global";

    /// <summary>Maps a scope to its wire key.</summary>
    /// <param name="scope"></param>
    public static string Key(MemoryScope scope)
    {
        return scope switch
        {
            MemoryScope.User => User,
            MemoryScope.Project => Project,
            MemoryScope.Global => Global,
            _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, null),
        };
    }

    /// <summary>Parses a wire key; null when unknown.</summary>
    /// <param name="key"></param>
    public static MemoryScope? Parse(string key)
    {
        return key switch
        {
            User => MemoryScope.User,
            Project => MemoryScope.Project,
            Global => MemoryScope.Global,
            _ => null,
        };
    }

    /// <summary>Parses a wire key or throws — the EF converter path (expression trees cannot inline throws).</summary>
    /// <param name="key"></param>
    /// <exception cref="InvalidOperationException">The key is unknown.</exception>
    public static MemoryScope ParseRequired(string key)
    {
        return Parse(key) ?? throw new InvalidOperationException($"unknown memory scope key '{key}'");
    }
}

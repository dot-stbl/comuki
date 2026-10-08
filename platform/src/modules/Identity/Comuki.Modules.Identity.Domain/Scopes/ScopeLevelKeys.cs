namespace Comuki.Modules.Identity.Domain.Scopes;

/// <summary>Stable stored keys of <see cref="ScopeLevel"/>.</summary>
public static class ScopeLevelKeys
{
    /// <summary>Key of <see cref="ScopeLevel.Platform"/>.</summary>
    public const string Platform = "platform";

    /// <summary>Key of <see cref="ScopeLevel.Project"/>.</summary>
    public const string Project = "project";

    /// <summary>Returns the key of a scope level; total over the enum.</summary>
    /// 
    public static string Key(ScopeLevel level)
    {
        return level switch
        {
            ScopeLevel.Platform => Platform,
            ScopeLevel.Project => Project,
            _ => throw new ArgumentOutOfRangeException(nameof(level), level, null),
        };
    }

    /// <summary>Parses a stored key back into a scope level; null when unknown.</summary>
    /// 
    public static ScopeLevel? Parse(string key)
    {
        return key switch
        {
            Platform => ScopeLevel.Platform,
            Project => ScopeLevel.Project,
            _ => null,
        };
    }
}

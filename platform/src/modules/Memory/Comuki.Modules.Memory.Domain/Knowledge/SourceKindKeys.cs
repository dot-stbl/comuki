namespace Comuki.Modules.Memory.Domain.Knowledge;

/// <summary>
/// Stable wire keys for <see cref="SourceKind"/> — the database column
/// values and the MCP <c>knowledge.ingest</c> tool surface use these
/// strings.
/// </summary>
public static class SourceKindKeys
{
    /// <summary>Key of <see cref="SourceKind.Git"/>.</summary>
    public const string Git = "git";

    /// <summary>Key of <see cref="SourceKind.Upload"/>.</summary>
    public const string Upload = "upload";

    /// <summary>Key of <see cref="SourceKind.Url"/>.</summary>
    public const string Url = "url";

    /// <summary>Maps a kind to its wire key.</summary>
    /// <param name="kind"></param>
    public static string Key(SourceKind kind)
    {
        return kind switch
        {
            SourceKind.Git => Git,
            SourceKind.Upload => Upload,
            SourceKind.Url => Url,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
    }

    /// <summary>Parses a wire key; null when unknown.</summary>
    /// <param name="key"></param>
    public static SourceKind? Parse(string key)
    {
        return key switch
        {
            Git => SourceKind.Git,
            Upload => SourceKind.Upload,
            Url => SourceKind.Url,
            _ => null,
        };
    }

    /// <summary>Parses a wire key or throws — the EF converter path (expression trees cannot inline throws).</summary>
    /// <param name="key"></param>
    /// <exception cref="InvalidOperationException">The key is unknown.</exception>
    public static SourceKind ParseRequired(string key)
    {
        return Parse(key) ?? throw new InvalidOperationException($"unknown source kind key '{key}'");
    }
}

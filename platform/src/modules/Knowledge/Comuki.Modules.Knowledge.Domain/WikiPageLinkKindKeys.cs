namespace Comuki.Modules.Knowledge.Domain;

/// <summary>
/// Stable wire keys for <see cref="WikiPageLinkKind"/> — the database
/// <c>link_graph</c> jsonb values and the markdown frontmatter
/// <c>link_graph</c> entries use these strings.
/// </summary>
public static class WikiPageLinkKindKeys
{
    /// <summary>Key of <see cref="WikiPageLinkKind.SeeAlso"/>.</summary>
    public const string SeeAlso = "see-also";

    /// <summary>Key of <see cref="WikiPageLinkKind.Supersedes"/>.</summary>
    public const string Supersedes = "supersedes";

    /// <summary>Key of <see cref="WikiPageLinkKind.DerivedFrom"/>.</summary>
    public const string DerivedFrom = "derived-from";

    /// <summary>Maps a kind to its wire key.</summary>
    public static string Key(WikiPageLinkKind kind)
    {
        return kind switch
        {
            WikiPageLinkKind.SeeAlso => SeeAlso,
            WikiPageLinkKind.Supersedes => Supersedes,
            WikiPageLinkKind.DerivedFrom => DerivedFrom,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
    }

    /// <summary>Parses a wire key; null when unknown.</summary>
    public static WikiPageLinkKind? Parse(string key)
    {
        return key switch
        {
            SeeAlso => WikiPageLinkKind.SeeAlso,
            Supersedes => WikiPageLinkKind.Supersedes,
            DerivedFrom => WikiPageLinkKind.DerivedFrom,
            _ => null,
        };
    }

    /// <summary>Parses a wire key or throws — used by the EF converter path.</summary>
    /// <exception cref="InvalidOperationException">The key is unknown.</exception>
    public static WikiPageLinkKind ParseRequired(string key)
    {
        return Parse(key) ?? throw new InvalidOperationException($"unknown wiki page link kind key '{key}'");
    }
}
